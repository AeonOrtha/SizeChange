using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SizeChange;

// Pure profile composition. No IPC, live actor state, native memory or profile ownership.
internal sealed class CustomizeProfileBlend
{
    private readonly JsonObject source;
    private readonly JsonObject target;
    private sealed record BoneMix(string Name, JsonObject Template, float[] Start, float[] Delta, bool[] SourceFlags, bool[] TargetFlags);
    private readonly List<BoneMix> mixes = new();
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
            mixes.Add(new(name, (a ?? b)!, start, delta, sourceFlags, targetFlags));
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
            // Boolean inheritance modes cannot interpolate; endpoints remain exact.
            var flags = amount < .5f ? mix.SourceFlags : mix.TargetFlags;
            for (int f = 0; f < Flags.Length; f++)
                if (bone[Flags[f]]?.GetValue<bool>() != flags[f]) bone[Flags[f]] = flags[f];
        }
    }
}
