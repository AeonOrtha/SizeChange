using System;

namespace SizeChange;

[Serializable]
public sealed class HeartbeatChainSettings
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public float Strength { get; set; } = 0.05f;
    public bool Stagger { get; set; }
    // A fraction of the heartbeat period: changing BPM also changes ripple speed.
    public float DelayPercent { get; set; } = 6f;
}

internal sealed record HeartbeatChainDefinition(string Id, string Label, params HeartbeatChainBone[] Bones);
internal readonly record struct HeartbeatChainBone(string Name, int Depth);

internal static class HeartbeatChains
{
    // CustomizePlus/Core/Data/BoneData.cs, BoneRawTable: exact internal names
    // and parent relationships. Depth is relative to the first bone in THIS
    // chain; paired branches have the same depth. n_root is never targeted.
    internal static readonly HeartbeatChainDefinition[] All =
    {
        // Retain the saved ID so existing arm settings survive the rename.
        // Customize+ parents the shoulder helper to the upper arm, alongside
        // the forearm. Both children therefore have depth 1, on both sides.
        new("clavicles", "Arms",
            new("j_ude_a_l", 0), new("j_ude_a_r", 0),
            new("n_hkata_l", 1), new("n_hkata_r", 1),
            new("j_ude_b_l", 1), new("j_ude_b_r", 1)),
        new("torso", "Upper torso (spine)",
            new("j_sebo_a", 0), new("j_sebo_b", 1), new("j_sebo_c", 2)),
        new("pelvis", "Pelvis", new HeartbeatChainBone("j_kosi", 0)),
        new("neck", "Neck to head", new("j_kubi", 0), new("j_kao", 1)),
        new("legs", "Legs",
            new("j_asi_a_l", 0), new("j_asi_a_r", 0),
            new("j_asi_b_l", 1), new("j_asi_b_r", 1),
            new("j_asi_c_l", 2), new("j_asi_c_r", 2)),
    };
}
