using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SizeChange;

internal sealed class ProfileBlendCompatibilityException(string message) : InvalidOperationException(message);

// Pure profile composition. No IPC, live actor state, native memory or profile ownership.
internal sealed class CustomizeProfileBlend
{
    private readonly JsonObject source;
    private readonly JsonObject target;
    private sealed record BoneMix(string Name, JsonObject Template, float[] Start, float[] Delta,
        bool[] Flags, bool NormalizeScale, float SourceCoupling, float TargetCoupling);
    private readonly List<BoneMix> mixes = new();
    private readonly Dictionary<string, BoneMix> byName = new(StringComparer.Ordinal);
    private static readonly string[] Vectors = { "Translation", "Rotation", "Scaling", "ChildScaling" };
    private static readonly string[] Axes = { "X", "Y", "Z" };
    private static readonly string[] Flags = { "ChildScaleIndependent", "PropagateTranslation", "PropagateRotation", "PropagateScale" };

    public CustomizeProfileBlend(JsonObject source, string targetJson)
    {
        this.source = source;
        target = Parse(targetJson);
        Validate(source);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in (JsonObject)source["Bones"]!) seen.Add(pair.Key);
        foreach (var pair in (JsonObject)target["Bones"]!) seen.Add(pair.Key);
        foreach (string name in seen)
        {
            var a = source["Bones"]![name] as JsonObject;
            var b = target["Bones"]![name] as JsonObject;
            var start = new float[12]; var delta = new float[12];
            for (int v = 0; v < Vectors.Length; v++)
                for (int axis = 0; axis < Axes.Length; axis++)
                {
                    int i = v * 3 + axis;
                    float identity = v >= 2 ? 1 : 0;
                    start[i] = a?[Vectors[v]]?[Axes[axis]]?.GetValue<float>() ?? identity;
                    float end = b?[Vectors[v]]?[Axes[axis]]?.GetValue<float>() ?? identity;
                    delta[i] = v == 1 ? MathF.IEEERemainder(end - start[i], 360) : end - start[i];
                }
            var sourceFlags = new bool[4]; var targetFlags = new bool[4];
            for (int f = 0; f < Flags.Length; f++)
            {
                sourceFlags[f] = a?[Flags[f]]?.GetValue<bool>() ?? false;
                targetFlags[f] = b?[Flags[f]]?.GetValue<bool>() ?? false;
            }
            // Position/rotation have no separate child transform in the IPC DTO.
            // Do not silently change their inheritance halfway or promise an
            // equivalent interpolation that this representation cannot express.
            for (int f = 1; f <= 2; f++)
                if (sourceFlags[f] != targetFlags[f])
                    throw new ProfileBlendCompatibilityException($"Blend paused: match {Flags[f]} on {name} in both profiles.");

            bool normalize = (sourceFlags[3] || targetFlags[3]) &&
                (sourceFlags[0] != targetFlags[0] || sourceFlags[3] != targetFlags[3]);
            var stableFlags = (bool[])sourceFlags.Clone();
            if (normalize)
            {
                // ModelBone.ApplyModelTransform enters a different path when ANY
                // propagation flag is set. That path also moves descendants for
                // a nonzero local translation, even with PropagateTranslation off.
                // Enabling it at an endpoint with a position offset is therefore
                // not equivalent; leave that pair alone and explain the mismatch.
                if (WouldActivateTranslation(sourceFlags, start, delta, false) ||
                    WouldActivateTranslation(targetFlags, start, delta, true))
                    throw new ProfileBlendCompatibilityException($"Blend paused: match child propagation on {name}; its position offset cannot be blended independently.");

                for (int axis = 0; axis < 3; axis++)
                {
                    float childA = !sourceFlags[3] ? 1 : sourceFlags[0] ? start[9 + axis] : start[6 + axis];
                    float childB = !targetFlags[3] ? 1 : targetFlags[0]
                        ? start[9 + axis] + delta[9 + axis] : start[6 + axis] + delta[6 + axis];
                    start[9 + axis] = childA;
                    delta[9 + axis] = childB - childA;
                }
                // An explicit child scale represents BOTH endpoints without a
                // boolean handoff: off = identity; coupled = the parent's scale.
                stableFlags[0] = true;
                stableFlags[3] = true;
            }
            var mix = new BoneMix(name, (a ?? b)!, start, delta, stableFlags, normalize,
                sourceFlags[3] && !sourceFlags[0] ? 1 : 0,
                targetFlags[3] && !targetFlags[0] ? 1 : 0);
            mixes.Add(mix);
            byName.Add(name, mix);
        }
    }

    private static bool WouldActivateTranslation(bool[] flags, float[] start, float[] delta, bool target)
    {
        if (flags[1] || flags[2] || flags[3]) return false;
        for (int axis = 0; axis < 3; axis++)
            if (start[axis] + (target ? delta[axis] : 0) != 0) return true;
        return false;
    }

    // Existing pulse/growth additions follow the parent's influence on children.
    // Recompute from the blended baseline, never the previous animated frame.
    public void LayerScale(string name, JsonObject baseline, JsonObject output, float amount)
    {
        if (amount <= 0 || amount >= 1 || !byName.TryGetValue(name, out var mix) || !mix.NormalizeScale) return;
        float coupling = float.Lerp(mix.SourceCoupling, mix.TargetCoupling, amount);
        var original = baseline["Bones"]![name]!;
        var bone = output["Bones"]![name]!;
        for (int axis = 0; axis < 3; axis++)
        {
            string key = Axes[axis];
            float addition = bone["Scaling"]![key]!.GetValue<float>() - original["Scaling"]![key]!.GetValue<float>();
            float child = original["ChildScaling"]![key]!.GetValue<float>() + coupling * addition;
            bone["ChildScaling"]![key] = Math.Clamp(child, -512f, 512f);
        }
    }

    internal static JsonObject Parse(string json)
    {
        if (json.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Customize+ profile exceeds 8 MB.");
        var result = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Customize+ returned an invalid profile.");
        Validate(result);
        return result;
    }

    private static void Validate(JsonObject profile)
    {
        if (profile["Bones"] is not JsonObject bones || bones.Count > 2048)
            throw new InvalidOperationException("Customize+ profile has an invalid Bones object.");
        foreach (var pair in bones)
        {
            if (pair.Value is not JsonObject bone) throw new InvalidOperationException("Invalid bone transform: " + pair.Key);
            foreach (string vector in Vectors)
            {
                if (bone[vector] == null) continue;
                if (bone[vector] is not JsonObject value) throw new InvalidOperationException("Invalid bone vector: " + pair.Key);
                foreach (string axis in Axes)
                    if (value[axis] != null && (value[axis] is not JsonValue number ||
                        !number.TryGetValue<float>(out float v) || !float.IsFinite(v) || Math.Abs(v) > 512f))
                        throw new InvalidOperationException("Invalid bone coordinate: " + pair.Key);
            }
            foreach (string flag in Flags)
                if (bone[flag] != null && (bone[flag] is not JsonValue value || !value.TryGetValue<bool>(out _)))
                    throw new InvalidOperationException("Invalid bone flag: " + pair.Key);
        }
    }

    public JsonObject At(float amount)
    {
        var result = (JsonObject)source.DeepClone();
        ApplyTo(result, amount);
        return result;
    }

    // Reuse the trees while size changes. Source coordinates above never come
    // from these mutable outputs; pulse/jaw/root edits are reset before layering.
    public void ApplyTo(JsonObject output, float amount)
    {
        amount = float.IsFinite(amount) ? Math.Clamp(amount, 0, 1) : 0;
        if (amount is 0 or 1)
        { output["Bones"] = (amount == 0 ? source : target)["Bones"]!.DeepClone(); return; }
        var bones = (JsonObject)output["Bones"]!;
        foreach (var mix in mixes)
        {
            if (bones[mix.Name] is not JsonObject bone)
            { bone = (JsonObject)mix.Template.DeepClone(); bones[mix.Name] = bone; }
            for (int v = 0; v < Vectors.Length; v++)
            {
                if (bone[Vectors[v]] is not JsonObject value)
                { value = new JsonObject(); bone[Vectors[v]] = value; }
                for (int axis = 0; axis < Axes.Length; axis++)
                {
                    // Customize+ represents rotation as degrees. Take the short
                    // arc per Euler axis rather than rotating 340 degrees.
                    int i = v * 3 + axis;
                    float mixed = mix.Start[i] + mix.Delta[i] * amount;
                    if (v == 1) mixed = MathF.IEEERemainder(mixed, 360);
                    if (value[Axes[axis]]?.GetValue<float>() != mixed) value[Axes[axis]] = mixed;
                }
            }
            for (int f = 0; f < Flags.Length; f++)
                if (bone[Flags[f]]?.GetValue<bool>() != mix.Flags[f]) bone[Flags[f]] = mix.Flags[f];
        }
    }
}
