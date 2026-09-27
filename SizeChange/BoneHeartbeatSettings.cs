using System;
using System.Collections.Generic;

namespace SizeChange;

public enum BoneHeartbeatMode
{
    WhileAccumulating,
    Always,
}

[Serializable]
public sealed class HeartbeatBone
{
    public string Name { get; set; } = string.Empty;
    public float Strength { get; set; } = 0.05f;
}

[Serializable]
public sealed class BoneHeartbeatSettings
{
    public bool Enabled { get; set; }
    public BoneHeartbeatMode Mode { get; set; } = BoneHeartbeatMode.WhileAccumulating;
    // Empty means the normal Customize+ profile currently active on self.
    public Guid BaseProfileId { get; set; }
    public float BeatsPerMinute { get; set; } = 72f;
    public float Strength { get; set; } = 1f;
    public List<HeartbeatBone> Bones { get; set; } = new();

    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) Mode = BoneHeartbeatMode.WhileAccumulating;
        BeatsPerMinute = float.IsFinite(BeatsPerMinute) ? Math.Clamp(BeatsPerMinute, 30f, 180f) : 72f;
        Strength = float.IsFinite(Strength) ? Math.Clamp(Strength, 0f, 5f) : 1f;
        Bones ??= new();
        var names = new HashSet<string>(StringComparer.Ordinal);
        Bones.RemoveAll(bone =>
        {
            if (bone == null) return true;
            bone.Name = bone.Name?.Trim() ?? string.Empty;
            bone.Strength = float.IsFinite(bone.Strength) ? Math.Clamp(bone.Strength, 0f, 5f) : 0.05f;
            return bone.Name.Length == 0 ||
                bone.Name.Equals("n_root", StringComparison.OrdinalIgnoreCase) || !names.Add(bone.Name);
        });
    }
}
