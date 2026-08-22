using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace SizeChange;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    
    // Apply SizeChange to the local player.
    public bool AffectSelf { get; set; } = true;
    // Exact, case-insensitive player names that SizeChange should affect.
    public List<string> TrackedPlayerNames { get; set; } = new();
    // the speed at which the model scales, higher is faster
    public float Speed { get; set; } = 2.0f;
    // the minimum size of the model
    public float MinScaleMultiplier { get; set; } = 0.1f;
    // maximum size used by the original Grow From Damage mode
    public float MaxScaleMultiplier { get; set; } = 1.0f;
    // amplitude applied to each detected health-loss ratio in Growth From Delta
    public float DeltaGrowthMultiplier { get; set; } = 1.0f;
    // optionally cap the total multiplier accumulated by Growth From Delta
    public bool LimitDeltaGrowth { get; set; } = false;
    public float DeltaMaxScaleMultiplier { get; set; } = 5.0f;
    // amount removed from accumulated damage growth per second 
    public float AmbientShrinkRate { get; set; } = 0.05f;
    // multiplier applied to ambient shrink after combat ends
    public float OutOfCombatDecayMultiplier { get; set; } = 10.0f;
    // move the visual root upward as Growth From Delta increases the scale
    public bool EnableDeltaHeightOffset { get; set; } = false;
    // Y offset added for each visible 1x of scale above the player's base scale
    public float DeltaHeightOffsetPerScale { get; set; } = 0.5f;
    public bool OnlyActiveInCombat { get; set; } = false;
    public bool Enable { get; set; } = true;
    public bool GrowFromDamage { get; set; } = false;
    public bool GrowthFromDelta { get; set; } = false;
    
    public void Save()
    {
        if (MinScaleMultiplier > MaxScaleMultiplier) { MinScaleMultiplier = MaxScaleMultiplier; }
        if (MaxScaleMultiplier < MinScaleMultiplier) { MaxScaleMultiplier = MinScaleMultiplier; }
        if (DeltaGrowthMultiplier < 0) { DeltaGrowthMultiplier = 0.0f; }
        if (DeltaMaxScaleMultiplier < 1.0f) { DeltaMaxScaleMultiplier = 1.0f; }
        if (AmbientShrinkRate < 0) { AmbientShrinkRate = 0.0f; }
        if (OutOfCombatDecayMultiplier < 1.0f) { OutOfCombatDecayMultiplier = 1.0f; }
        if (DeltaHeightOffsetPerScale < 0) { DeltaHeightOffsetPerScale = 0.0f; }
        if (Speed <= 0)
        {
            Speed = 0.1f;
        }

        TrackedPlayerNames ??= new List<string>();
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < TrackedPlayerNames.Count;)
        {
            string playerName = TrackedPlayerNames[index].Trim();
            if (playerName.Length == 0 || !uniqueNames.Add(playerName))
            {
                TrackedPlayerNames.RemoveAt(index);
                continue;
            }

            TrackedPlayerNames[index] = playerName;
            index++;
        }
        
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
