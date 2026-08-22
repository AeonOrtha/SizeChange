using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SizeChange.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    
    public ConfigWindow(Plugin plugin) : base("SizeChange Config")
    {

        //Size = new Vector2(350, 280);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var speed = configuration.Speed;
        var minScaleMultiplier = configuration.MinScaleMultiplier;
        var maxScaleMultiplier = configuration.MaxScaleMultiplier;
        var deltaGrowthMultiplier = configuration.DeltaGrowthMultiplier;
        var limitDeltaGrowth = configuration.LimitDeltaGrowth;
        var deltaMaxScaleMultiplier = configuration.DeltaMaxScaleMultiplier;
        var ambientShrinkRate = configuration.AmbientShrinkRate;
        var outOfCombatDecayMultiplier = configuration.OutOfCombatDecayMultiplier;
        var enableDeltaHeightOffset = configuration.EnableDeltaHeightOffset;
        var deltaHeightOffsetPerScale = configuration.DeltaHeightOffsetPerScale;
        var AlterAnyone = configuration.AlterAnyone;
        var Enable = configuration.Enable;
        var GrowFromDamage = configuration.GrowFromDamage;
        var GrowthFromDelta = configuration.GrowthFromDelta;
        var OnlyActiveInCombat = configuration.OnlyActiveInCombat;

        if (ImGui.Checkbox("Enable", ref Enable))
        {
            configuration.Enable = Enable;
            configuration.Save();
        }
        
        if (ImGui.Checkbox("Scale Anyone", ref AlterAnyone))
        {
            configuration.AlterAnyone = AlterAnyone;
            configuration.Save();
        }

        if (ImGui.Checkbox("Only Active in Combat", ref OnlyActiveInCombat))
        {
            configuration.OnlyActiveInCombat = OnlyActiveInCombat;
            configuration.Save();
        }
        
        if (ImGui.Checkbox("Grow From Damage", ref GrowFromDamage))
        {
            configuration.GrowFromDamage = GrowFromDamage;
            if (GrowFromDamage)
            {
                GrowthFromDelta = false;
                configuration.GrowthFromDelta = false;
            }
            configuration.Save();
        }

        if (ImGui.Checkbox("Growth From Delta", ref GrowthFromDelta))
        {
            configuration.GrowthFromDelta = GrowthFromDelta;
            if (GrowthFromDelta)
            {
                GrowFromDamage = false;
                configuration.GrowFromDamage = false;
            }
            configuration.Save();
        }

        if (ImGui.DragFloat("Speed", ref speed, 0.1F, 0.1F, 100.0F))
        {
            if(speed <= 0){
                speed = 0.1f;
            }
            configuration.Speed = speed;
            configuration.Save();
        }

        if (ImGui.DragFloat("Minimum Size Multiplier", ref minScaleMultiplier, 0.01F, 0.01F, 1.00F))
        {
            if (minScaleMultiplier > 1.00F){ minScaleMultiplier = 1.00F; }
            configuration.MinScaleMultiplier = minScaleMultiplier;
            configuration.Save();
        }

        if (GrowFromDamage && ImGui.DragFloat("Maximum Size Multiplier", ref maxScaleMultiplier, 0.1F, 1.00F, 10.00F))
        {
            if (maxScaleMultiplier < 1.00F){ maxScaleMultiplier = 1.00F; }
            configuration.MaxScaleMultiplier = maxScaleMultiplier;
            configuration.Save();
        }

        if (GrowthFromDelta && ImGui.DragFloat("Damage Growth Multiplier", ref deltaGrowthMultiplier, 0.1F, 0.00F, 10.00F))
        {
            if (deltaGrowthMultiplier < 0.00F){ deltaGrowthMultiplier = 0.00F; }
            configuration.DeltaGrowthMultiplier = deltaGrowthMultiplier;
            configuration.Save();
        }

        if (GrowthFromDelta && ImGui.Checkbox("Limit Delta Growth", ref limitDeltaGrowth))
        {
            configuration.LimitDeltaGrowth = limitDeltaGrowth;
            configuration.Save();
        }

        if (GrowthFromDelta && limitDeltaGrowth &&
            ImGui.DragFloat("Delta Maximum Size Multiplier", ref deltaMaxScaleMultiplier, 0.1F, 1.00F, 100.00F))
        {
            if (deltaMaxScaleMultiplier < 1.00F){ deltaMaxScaleMultiplier = 1.00F; }
            configuration.DeltaMaxScaleMultiplier = deltaMaxScaleMultiplier;
            configuration.Save();
        }

        if (GrowthFromDelta && ImGui.DragFloat("Ambient Shrink Per Second", ref ambientShrinkRate, 0.01F, 0.00F, 10.00F))
        {
            if (ambientShrinkRate < 0.00F){ ambientShrinkRate = 0.00F; }
            configuration.AmbientShrinkRate = ambientShrinkRate;
            configuration.Save();
        }

        if (GrowthFromDelta && OnlyActiveInCombat &&
            ImGui.DragFloat("Out of Combat Decay Multiplier", ref outOfCombatDecayMultiplier, 0.5F, 1.00F, 100.00F))
        {
            if (outOfCombatDecayMultiplier < 1.00F){ outOfCombatDecayMultiplier = 1.00F; }
            configuration.OutOfCombatDecayMultiplier = outOfCombatDecayMultiplier;
            configuration.Save();
        }

        if (GrowthFromDelta && ImGui.Checkbox("Enable Growth Height Offset", ref enableDeltaHeightOffset))
        {
            configuration.EnableDeltaHeightOffset = enableDeltaHeightOffset;
            configuration.Save();
        }

        if (GrowthFromDelta && enableDeltaHeightOffset &&
            ImGui.DragFloat("Height Offset Per Extra 1x", ref deltaHeightOffsetPerScale, 0.01F, 0.00F, 5.00F))
        {
            if (deltaHeightOffsetPerScale < 0.00F){ deltaHeightOffsetPerScale = 0.00F; }
            configuration.DeltaHeightOffsetPerScale = deltaHeightOffsetPerScale;
            configuration.Save();
        }

        if (ImGui.Button("Default")) 
        {
            configuration.AlterAnyone = false;
            configuration.MinScaleMultiplier = 0.1f;
            configuration.MaxScaleMultiplier = 1.0f;
            configuration.DeltaGrowthMultiplier = 1.0f;
            configuration.LimitDeltaGrowth = false;
            configuration.DeltaMaxScaleMultiplier = 5.0f;
            configuration.AmbientShrinkRate = 0.05f;
            configuration.OutOfCombatDecayMultiplier = 10.0f;
            configuration.EnableDeltaHeightOffset = false;
            configuration.DeltaHeightOffsetPerScale = 0.5f;
            configuration.Speed = 2.0f;
            configuration.Enable = true;
            configuration.OnlyActiveInCombat = false;
            configuration.GrowFromDamage = false;
            configuration.GrowthFromDelta = false;
            configuration.Save();
        }
        
        ImGui.Text("This plugin is disabled in PVP");
    }
}
