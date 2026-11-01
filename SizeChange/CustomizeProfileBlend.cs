using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SizeChange;

// Pure profile composition. No IPC, live actor state, native memory or profile ownership.
internal sealed class CustomizeProfileBlend
{
    private readonly JsonObject source;
    private readonly JsonObject target;
    private sealed record BoneMix(string Name, JsonObject Template, float[] Start, float[] Delta,
        bool[] SourceFlags, bool[] TargetFlags, bool NormalizeScale, bool BridgeScale,
        bool BridgeTranslation, bool BridgeRotation, float SourceCoupling, float TargetCoupling);
    public string? Notice { get; }
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
        int bridges = 0;
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
            // Root height/scale are applied separately by Customize+; its child
            // propagation flags do not control the ordinary ModelBone path.
            bool root = name == "n_root";
            bool normalize = !root && (sourceFlags[3] || targetFlags[3]) &&
                (sourceFlags[0] != targetFlags[0] || sourceFlags[3] != targetFlags[3]);
            bool sourcePropagates = sourceFlags[1] || sourceFlags[2] || sourceFlags[3];
            bool targetPropagates = targetFlags[1] || targetFlags[2] || targetFlags[3];
            bool bridgeTranslation = !root && (sourceFlags[1] != targetFlags[1] || sourcePropagates != targetPropagates);
            bool bridgeRotation = !root && sourceFlags[2] != targetFlags[2];
            bool bridgeScale = normalize && (WouldActivateTranslation(sourceFlags, start, delta, false) ||
                WouldActivateTranslation(targetFlags, start, delta, true));
            if (normalize)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    float childA = !sourceFlags[3] ? 1 : sourceFlags[0] ? start[9 + axis] : start[6 + axis];
                    float childB = !targetFlags[3] ? 1 : targetFlags[0]
                        ? start[9 + axis] + delta[9 + axis] : start[6 + axis] + delta[6 + axis];
                    start[9 + axis] = childA;
                    delta[9 + axis] = childB - childA;
                }
            }
            if (bridgeTranslation || bridgeRotation || bridgeScale) bridges++;
            var mix = new BoneMix(name, (a ?? b)!, start, delta, sourceFlags, targetFlags, normalize,
                bridgeScale, bridgeTranslation, bridgeRotation,
                sourceFlags[3] && !sourceFlags[0] ? 1 : 0,
                targetFlags[3] && !targetFlags[0] ? 1 : 0);
            mixes.Add(mix);
            byName.Add(name, mix);
        }
        if (bridges > 0) Notice = $"Blend ready ({bridges} inheritance transitions)";
    }

    private static float Ease(float value)
    {
        float t = Math.Clamp(value, 0, 1);
        return t * t * t * (t * (6 * t - 15) + 10);
    }

    // The IPC cannot express fractional translation/rotation propagation. Ease
    // just that component to identity, change its flag at identity, then ease
    // to the target. This is continuous in both directions, with zero slope at
    // the handoff. Other bones and the bone's own size keep blending normally.
    private static float ThroughNeutral(float start, float end, float amount) =>
        amount < .5f ? start * (1 - Ease(amount * 2)) : end * Ease(amount * 2 - 1);

    private static float ChildProgress(BoneMix mix, float amount) => !mix.BridgeScale ? amount :
        mix.SourceFlags[3] ? Ease(amount * 2) : Ease(amount * 2 - 1);

    public float JawLayerWeight(float amount) => amount <= 0 || amount >= 1 ||
        !byName.TryGetValue("j_f_ago", out var mix) || !mix.BridgeRotation
        ? 1 : ThroughNeutral(1, 1, amount);

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
        float coupling = float.Lerp(mix.SourceCoupling, mix.TargetCoupling, ChildProgress(mix, amount));
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
                    float progress = v == 3 ? ChildProgress(mix, amount) : amount;
                    float mixed = (v == 0 && mix.BridgeTranslation || v == 1 && mix.BridgeRotation)
                        ? ThroughNeutral(mix.Start[i], mix.Start[i] + mix.Delta[i], amount)
                        : mix.Start[i] + mix.Delta[i] * progress;
                    if (v == 1) mixed = MathF.IEEERemainder(mixed, 360);
                    if (value[Axes[axis]]?.GetValue<float>() != mixed) value[Axes[axis]] = mixed;
                }
            }
            var flags = amount < .5f ? mix.SourceFlags : mix.TargetFlags;
            for (int f = 0; f < Flags.Length; f++)
            {
                bool flag = (mix.NormalizeScale && (f == 0 || (f == 3 && !mix.BridgeScale))) || flags[f];
                if (bone[Flags[f]]?.GetValue<bool>() != flag) bone[Flags[f]] = flag;
            }
        }
    }
}
