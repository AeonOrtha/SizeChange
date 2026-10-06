using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SizeChange;

[Serializable]
public sealed class PlayerProfileHeightSettings
{
    public bool Enabled { get; set; }
    public float Offset { get; set; } = 0.5f;
    public List<string> Players { get; set; } = new();
    public void Validate()
    {
        Offset = float.IsFinite(Offset) ? Math.Clamp(Offset, 0f, 100f) : 0f;
        Players = (Players ?? new()).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public bool Includes(string name) => Enabled && Players.Contains(name, StringComparer.OrdinalIgnoreCase);
}

// Pure JSON copy: do not mutate the sender's profile or any other bone.
internal static class PlayerProfileHeightJson
{
    public static string Add(string original, float offset)
    {
        if (!float.IsFinite(offset) || offset < 0f || offset > 100f)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset == 0f) return original;
        var profile = JsonNode.Parse(original) as JsonObject ?? throw new FormatException("Invalid profile.");
        var bones = profile["Bones"] as JsonObject ?? throw new FormatException("Missing Bones.");
        if (bones["n_root"] is not JsonObject root)
        {
            if (bones["n_root"] != null) throw new FormatException("Invalid root bone.");
            root = new JsonObject
            {
                ["Translation"] = Vector(0f), ["Rotation"] = Vector(0f),
                ["Scaling"] = Vector(1f), ["ChildScaling"] = Vector(1f),
                ["ChildScaleIndependent"] = false,
                ["PropagateTranslation"] = false, ["PropagateRotation"] = false, ["PropagateScale"] = false,
            };
            bones["n_root"] = root;
        }
        var translation = root["Translation"] as JsonObject;
        if (translation == null)
        {
            if (root["Translation"] != null) throw new FormatException("Invalid root translation.");
            root["Translation"] = translation = Vector(0f);
        }
        float y = translation["Y"]?.GetValue<float>() ?? 0f;
        if (!float.IsFinite(y)) throw new FormatException("Invalid root height.");
        translation["Y"] = Math.Clamp(y + offset, -512f, 512f);
        return profile.ToJsonString();
    }
    private static JsonObject Vector(float value) => new() { ["X"] = value, ["Y"] = value, ["Z"] = value };
}
