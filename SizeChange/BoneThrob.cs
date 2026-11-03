using System;
using System.Text.Json.Nodes;

namespace SizeChange;

[Serializable]
public sealed class BoneThrobSettings
{
    public bool Enabled { get; set; }
    public string Root { get; set; } = "";
    public int Axis { get; set; } = 2;
    public float Degrees { get; set; } = 4;
    public float SwellPercent { get; set; } = 3;
    public float Bpm { get; set; } = 68;
    internal bool Active => Enabled && Root.Length > 0 && !Root.Equals("n_root", StringComparison.OrdinalIgnoreCase) &&
        (Degrees != 0 || SwellPercent != 0);

    public void Validate()
    {
        Root = (Root ?? "").Trim();
        Axis = Math.Clamp(Axis, 0, 2);
        Degrees = float.IsFinite(Degrees) ? Math.Clamp(Degrees, -20, 20) : 4;
        SwellPercent = float.IsFinite(SwellPercent) ? Math.Clamp(SwellPercent, 0, 20) : 3;
        Bpm = float.IsFinite(Bpm) ? Math.Clamp(Bpm, 30, 150) : 68;
    }
}

internal static class BoneThrob
{
    internal static double Advance(double phase, float seconds, BoneThrobSettings settings)
        => settings.Active ? (phase + seconds * settings.Bpm / 60.0) % 1 : 0;

    internal static float Beat(double phase)
    {
        // Fast pickup, slower fall, then a quiet interval. Flat tangent at every join.
        double t = phase < 0.17 ? phase / 0.17 : (0.65 - phase) / 0.48;
        t = Math.Clamp(t, 0, 1);
        return (float)(t * t * (3 - 2 * t));
    }

    private static readonly string[] Axes = { "X", "Y", "Z" };
    private static JsonObject Vec(float n) => new() { ["X"] = n, ["Y"] = n, ["Z"] = n };
    private static bool Edited(JsonNode? v) => v != null &&
        ((v["X"]?.GetValue<float>() ?? 0) != 0 || (v["Y"]?.GetValue<float>() ?? 0) != 0 ||
         (v["Z"]?.GetValue<float>() ?? 0) != 0);

    internal static string Render(JsonObject frame, BoneThrobSettings? settings, double phase)
    {
        if (settings?.Active != true) return frame.ToJsonString();
        float wave = Beat(phase);
        if (wave <= 0) return frame.ToJsonString();
        var bones = (JsonObject)frame["Bones"]!;
        string key = settings.Root;
        var original = bones[key];
        var overlay = original?.DeepClone() as JsonObject ?? new JsonObject
        {
            ["Translation"] = Vec(0), ["Rotation"] = Vec(0), ["Scaling"] = Vec(1), ["ChildScaling"] = Vec(1),
            ["ChildScaleIndependent"] = false, ["PropagateTranslation"] = false,
            ["PropagateRotation"] = false, ["PropagateScale"] = false,
        };
        bool inheritedRot = overlay["PropagateRotation"]?.GetValue<bool>() == true;
        bool inheritedScale = overlay["PropagateScale"]?.GetValue<bool>() == true;
        // Switching C+ into propagation mode can also move children by the
        // root's existing translation. Preserve intentional root-only offsets.
        bool canPropagate = inheritedRot || inheritedScale || overlay["PropagateTranslation"]?.GetValue<bool>() == true ||
            !Edited(overlay["Translation"]);
        if (settings.Degrees != 0)
        {
            var rot = (JsonObject)(overlay["Rotation"] ??= Vec(0));
            if (canPropagate && !Edited(rot)) overlay["PropagateRotation"] = true;
            string axis = Axes[settings.Axis];
            rot[axis] = (rot[axis]?.GetValue<float>() ?? 0) + settings.Degrees * wave;
        }
        if (settings.SwellPercent > 0)
        {
            var scale = (JsonObject)(overlay["Scaling"] ??= Vec(1));
            bool independent = overlay["ChildScaleIndependent"]?.GetValue<bool>() == true;
            var child = overlay["ChildScaling"];
            float k = 1 + wave * settings.SwellPercent / 100;
            var childScale = Vec(1);
            foreach (string axis in Axes)
            {
                float parentBase = scale[axis]?.GetValue<float>() ?? 1;
                float childBase = !inheritedScale ? 1 : independent ? child?[axis]?.GetValue<float>() ?? 1 : parentBase;
                scale[axis] = Math.Clamp(parentBase * k, -512, 512);
                childScale[axis] = Math.Clamp(childBase * k, -512, 512);
            }
            if (canPropagate)
            {
                overlay["ChildScaling"] = childScale;
                overlay["ChildScaleIndependent"] = true;
                overlay["PropagateScale"] = true;
            }
        }
        // Compose after jaw, growth and blend. Restore the exact node (including
        // references held by the heartbeat cache), even if serialization fails.
        bones[key] = overlay;
        try { return frame.ToJsonString(); }
        finally { if (original == null) bones.Remove(key); else bones[key] = original; }
    }
}
