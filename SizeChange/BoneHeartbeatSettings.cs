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
    // Runtime-only offset resolved from the selected chain, measured in cycles.
    internal double DelayCycles { get; set; }
}

[Serializable]
public sealed class BoneHeartbeatSettings
{
    public bool Enabled { get; set; }
    public BoneHeartbeatMode Mode { get; set; } = BoneHeartbeatMode.WhileAccumulating;
    // Empty means the normal Customize+ profile currently active on self.
    public Guid BaseProfileId { get; set; }
    public float BeatsPerMinute { get; set; } = 72f;
    public bool DamageDrivenBpm { get; set; }
    public float MaximumBeatsPerMinute { get; set; } = 180f;
    public float Strength { get; set; } = 1f;
    public List<HeartbeatBone> Bones { get; set; } = new();

    public JawBreathingSettings Jaw { get; set; } = new();

    public HeartbeatSoundSettings Sound { get; set; } = new();

    public List<HeartbeatChainSettings> Chains { get; set; } = new();

    // Reuse targets across frames. Custom entries override a preset's amount,
    // retaining its timing; selecting a bone twice never doubles the addition.
    public void ResolveBones(List<HeartbeatBone> targets)
    {
        int count = 0;
        void Add(string name, float amount, double delay)
        {
            for (int i = 0; i < count; i++)
                if (targets[i].Name == name) { targets[i].Strength = amount; return; }
            if (count == targets.Count) targets.Add(new HeartbeatBone());
            var target = targets[count++];
            target.Name = name;
            target.Strength = amount;
            target.DelayCycles = delay;
        }
        foreach (var definition in HeartbeatChains.All)
        {
            var chain = Chains.Find(x => x.Id == definition.Id);
            if (chain == null || !chain.Enabled) continue;
            double step = chain.Stagger ? Math.Clamp(chain.DelayPercent, 0f, 10f) / 100.0 : 0;
            foreach (var bone in definition.Bones)
                Add(bone.Name, chain.Strength, bone.Depth * step);
        }
        foreach (var bone in Bones) Add(bone.Name, bone.Strength, 0);
        if (count < targets.Count) targets.RemoveRange(count, targets.Count - count);
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) Mode = BoneHeartbeatMode.WhileAccumulating;
        BeatsPerMinute = float.IsFinite(BeatsPerMinute) ? Math.Clamp(BeatsPerMinute, 30f, 180f) : 72f;
        MaximumBeatsPerMinute = float.IsFinite(MaximumBeatsPerMinute)
            ? Math.Clamp(MaximumBeatsPerMinute, BeatsPerMinute, 180f) : 180f;
        Strength = float.IsFinite(Strength) ? Math.Clamp(Strength, 0f, 5f) : 1f;
        Jaw ??= new();
        Jaw.Validate();
        Sound ??= new();
        Sound.Validate();
        Chains ??= new();
        var chainIds = new HashSet<string>(StringComparer.Ordinal);
        Chains.RemoveAll(chain => chain == null || !chainIds.Add(chain.Id) ||
            !Array.Exists(HeartbeatChains.All, definition => definition.Id == chain.Id));
        foreach (var chain in Chains)
        {
            chain.Strength = float.IsFinite(chain.Strength) ? Math.Clamp(chain.Strength, 0f, 5f) : 0.05f;
            chain.DelayPercent = float.IsFinite(chain.DelayPercent) ? Math.Clamp(chain.DelayPercent, 0f, 10f) : 6f;
        }
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
