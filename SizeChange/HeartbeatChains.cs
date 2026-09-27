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
        new("clavicles", "Clavicles to hands",
            new("j_sako_l", 0), new("j_sako_r", 0),
            new("j_ude_a_l", 1), new("j_ude_a_r", 1),
            new("j_ude_b_l", 2), new("j_ude_b_r", 2),
            new("n_hte_l", 3), new("n_hte_r", 3),
            new("j_te_l", 4), new("j_te_r", 4)),
        new("torso", "Upper torso (spine)",
            new("j_sebo_a", 0), new("j_sebo_b", 1), new("j_sebo_c", 2)),
        new("pelvis", "Pelvis", new HeartbeatChainBone("j_kosi", 0)),
        new("neck", "Neck to head", new("j_kubi", 0), new("j_kao", 1)),
        new("legs", "Legs to toes",
            new("j_asi_a_l", 0), new("j_asi_a_r", 0),
            new("j_asi_b_l", 1), new("j_asi_b_r", 1),
            new("j_asi_c_l", 2), new("j_asi_c_r", 2),
            new("j_asi_d_l", 3), new("j_asi_d_r", 3),
            new("j_asi_e_l", 4), new("j_asi_e_r", 4)),
    };
}
