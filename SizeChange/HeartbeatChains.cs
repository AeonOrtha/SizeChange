using System;

namespace SizeChange;

[Serializable]
public sealed class HeartbeatChainSettings
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool GrowthEnabled { get; set; }
    public bool InverseGrowth { get; set; }
    public bool FullGrowthChain { get; set; }
    public float GrowthPerScale { get; set; } = 0.1f;
    public float GrowthLimit { get; set; } = 0.4f;
    public float Strength { get; set; } = 0.05f;
    public bool Stagger { get; set; }
    // A fraction of the heartbeat period: changing BPM also changes ripple speed.
    public float DelayPercent { get; set; } = 6f;
}

internal sealed record HeartbeatChainDefinition(string Id, string Label, params HeartbeatChainBone[] Bones);
internal readonly record struct HeartbeatChainBone(string Name, int Depth);

internal static class HeartbeatChains
{
    private static readonly HeartbeatChainBone[] ArmExtras = {
        new("iv_nitoukin_l", 1),
        new("n_hhiji_l", 2),
        new("n_hte_l", 2),
        new("j_te_l", 3),
        new("j_oya_a_l", 4),
        new("j_oya_b_l", 5),
        new("j_hito_a_l", 4),
        new("j_hito_b_l", 5),
        new("j_naka_a_l", 4),
        new("j_naka_b_l", 5),
        new("j_kusu_a_l", 4),
        new("j_kusu_b_l", 5),
        new("j_ko_a_l", 4),
        new("j_ko_b_l", 5),
        new("iv_hito_c_l", 6),
        new("iv_naka_c_l", 6),
        new("iv_kusu_c_l", 6),
        new("iv_ko_c_l", 6),
        new("iv_nitoukin_r", 1),
        new("n_hhiji_r", 2),
        new("n_hte_r", 2),
        new("j_te_r", 3),
        new("j_oya_a_r", 4),
        new("j_oya_b_r", 5),
        new("j_hito_a_r", 4),
        new("j_hito_b_r", 5),
        new("j_naka_a_r", 4),
        new("j_naka_b_r", 5),
        new("j_kusu_a_r", 4),
        new("j_kusu_b_r", 5),
        new("j_ko_a_r", 4),
        new("j_ko_b_r", 5),
        new("iv_hito_c_r", 6),
        new("iv_naka_c_r", 6),
        new("iv_kusu_c_r", 6),
        new("iv_ko_c_r", 6)
    };
    private static readonly HeartbeatChainBone[] TorsoExtras = { new("j_kosi", 0) };
    internal static HeartbeatChainBone[] GrowthExtras(string id) => id switch
    {
        "clavicles" => ArmExtras,
        "torso" => TorsoExtras,
        _ => Array.Empty<HeartbeatChainBone>(),
    };

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
