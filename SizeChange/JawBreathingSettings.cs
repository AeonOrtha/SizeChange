using System;

namespace SizeChange;

public enum JawBreathingMode { Always, DuringGrowth }

[Serializable]
public sealed class JawBreathingSettings
{
    public bool Enabled { get; set; }
    public JawBreathingMode Mode { get; set; } = JawBreathingMode.Always;
    public float BreathsPerMinute { get; set; } = 18f;
    public float OpeningDegrees { get; set; } = 10f;
    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) Mode = JawBreathingMode.Always;
        BreathsPerMinute = float.IsFinite(BreathsPerMinute) ? Math.Clamp(BreathsPerMinute, 2f, 60f) : 18f;
        OpeningDegrees = float.IsFinite(OpeningDegrees) ? Math.Clamp(OpeningDegrees, 0f, 45f) : 10f;
    }
    internal static float Sample(double phase) => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
}
