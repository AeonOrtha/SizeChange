using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SizeChange;

// A pristine baseline plus a reusable output tree. Every animated value is
// rebuilt from cached baseline coordinates, never the previous pulse frame.
internal sealed class BoneHeartbeatProfile
{
    private readonly JsonObject baseline;
    private JsonObject? working;
    private bool jawTouched;
    private readonly List<string> names = new();
    private readonly List<Target> targets = new();
    private readonly record struct Target(int Index, JsonObject Scale, float X, float Y, float Z);

    public BoneHeartbeatProfile(string json)
    {
        baseline = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Customize+ returned an invalid profile.");
        if (baseline["Bones"] is not JsonObject)
            throw new InvalidOperationException("Customize+ profile has no Bones object.");
    }

    public string Build(IReadOnlyList<HeartbeatBone> selectedBones, float pulse, float strength, double? phase = null, float jawAngle = 0f, int jawAxis = 0)
    {
        bool changed = working == null || names.Count != selectedBones.Count;
        for (int i = 0; !changed && i < names.Count; i++)
            changed = names[i] != selectedBones[i].Name;
        if (changed) Rebuild(selectedBones);
        foreach (var target in targets)
        {
            var bone = selectedBones[target.Index];
            // With phase supplied, pulse is the global fade envelope. Keep an
            // unwrapped timeline so delayed children do not pulse before their
            // parent on startup by wrapping a negative phase into the last beat.
            double delayed = (phase ?? 0) - bone.DelayCycles;
            float wave = phase.HasValue ? (delayed < 0 ? 0 : BoneHeartbeatMath.Sample(delayed)) : 1f;
            float offset = bone.Strength * strength * pulse * wave;
            if (!float.IsFinite(offset) || offset < 0f) offset = 0f;
            target.Scale["X"] = Math.Clamp(target.X + offset, -512f, 512f);
            target.Scale["Y"] = Math.Clamp(target.Y + offset, -512f, 512f);
            target.Scale["Z"] = Math.Clamp(target.Z + offset, -512f, 512f);
        }
        ApplyJaw(jawAngle, jawAxis);
        return working!.ToJsonString();
    }

    private void ApplyJaw(float angle, int axis)
    {
        if (!float.IsFinite(angle)) angle = 0;
        if (angle == 0 && !jawTouched) return;
        const string name = "j_f_ago";
        var bones = (JsonObject)working!["Bones"]!;
        var original = baseline["Bones"]![name] as JsonObject;
        if (angle == 0)
        {
            // Restore just rotation: the jaw may also be a custom scale target.
            if (original == null && !names.Contains(name)) bones.Remove(name);
            else if (bones[name] is JsonObject resting)
            {
                if (original?["Rotation"] != null) resting["Rotation"] = original["Rotation"]!.DeepClone();
                else if (original != null) resting.Remove("Rotation");
                else resting["Rotation"] = Vector(0f);
            }
            jawTouched = false;
            return;
        }
        var bone = bones[name] as JsonObject;
        if (bone == null)
        {
            bone = new JsonObject
            {
                ["Translation"] = Vector(0f), ["Rotation"] = Vector(0f),
                ["Scaling"] = Vector(1f), ["ChildScaling"] = Vector(1f),
                ["ChildScaleIndependent"] = false,
                ["PropagateTranslation"] = false, ["PropagateRotation"] = false, ["PropagateScale"] = false,
            };
            bones[name] = bone;
        }
        var rotation = original?["Rotation"]?.DeepClone() as JsonObject ?? Vector(0f);
        string component = axis == 1 ? "Y" : axis == 2 ? "Z" : "X";
        rotation[component] = (rotation[component]?.GetValue<float>() ?? 0f) + angle;
        bone["Rotation"] = rotation;
        jawTouched = true;
    }

    private void Rebuild(IReadOnlyList<HeartbeatBone> selectedBones)
    {
        working = (JsonObject)baseline.DeepClone();
        jawTouched = false;
        var bones = (JsonObject)working["Bones"]!;
        names.Clear();
        targets.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < selectedBones.Count; i++)
        {
            string name = selectedBones[i].Name;
            names.Add(name);
            if (string.IsNullOrWhiteSpace(name) ||
                name.Equals("n_root", StringComparison.OrdinalIgnoreCase) || !seen.Add(name)) continue;
            if (bones[name] is not JsonObject bone)
            {
                // Identity for an unedited bone; IPC DTO vector defaults are
                // not necessarily identity, so supply all vectors explicitly.
                bone = new JsonObject
                {
                    ["Translation"] = Vector(0f), ["Rotation"] = Vector(0f),
                    ["Scaling"] = Vector(1f), ["ChildScaling"] = Vector(1f),
                    ["ChildScaleIndependent"] = false,
                    ["PropagateTranslation"] = false, ["PropagateRotation"] = false,
                    ["PropagateScale"] = false,
                };
                bones[name] = bone;
            }
            var scaling = bone["Scaling"] as JsonObject ?? Vector(1f);
            if (bone["Scaling"] is not JsonObject) bone["Scaling"] = scaling;
            targets.Add(new Target(i, scaling,
                scaling["X"]?.GetValue<float>() ?? 0f,
                scaling["Y"]?.GetValue<float>() ?? 0f,
                scaling["Z"]?.GetValue<float>() ?? 0f));
        }
    }

    private static JsonObject Vector(float value)
        => new() { ["X"] = value, ["Y"] = value, ["Z"] = value };
}
