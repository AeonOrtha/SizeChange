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
        var ambientShrinkRate = configuration.AmbientShrinkRate;
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

        if (GrowthFromDelta && ImGui.DragFloat("Ambient Shrink Per Second", ref ambientShrinkRate, 0.01F, 0.00F, 10.00F))
        {
            if (ambientShrinkRate < 0.00F){ ambientShrinkRate = 0.00F; }
            configuration.AmbientShrinkRate = ambientShrinkRate;
            configuration.Save();
        }

        if (ImGui.Button("Default"))
        {
            configuration.AlterAnyone = false;
            configuration.MinScaleMultiplier = 0.1f;
            configuration.MaxScaleMultiplier = 1.0f;
            configuration.DeltaGrowthMultiplier = 1.0f;
            configuration.AmbientShrinkRate = 0.05f;
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
