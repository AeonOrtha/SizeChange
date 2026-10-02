using System;

namespace SizeChange;

public enum JawBreathingMode { Always, DuringGrowth }

[Serializable]
public sealed class JawBreathingSettings
{
    public bool Enabled { get; set; }
    public JawBreathingMode Mode { get; set; } = JawBreathingMode.Always;
    public float BreathsPerMinute { get; set; } = 18f;
    public float HoldOpenSeconds { get; set; }
    public float PauseSeconds { get; set; }
    public float OpeningDegrees { get; set; } = 10f;
    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) Mode = JawBreathingMode.Always;
        BreathsPerMinute = float.IsFinite(BreathsPerMinute) ? Math.Clamp(BreathsPerMinute, 2f, 60f) : 18f;
        PauseSeconds = float.IsFinite(PauseSeconds) ? Math.Clamp(PauseSeconds, 0f, 10f) : 0f;
        HoldOpenSeconds = float.IsFinite(HoldOpenSeconds) ? Math.Clamp(HoldOpenSeconds, 0f, 10f) : 0f;
        OpeningDegrees = float.IsFinite(OpeningDegrees) ? Math.Clamp(OpeningDegrees, 0f, 45f) : 10f;
    }
    internal double MotionSeconds => 60.0 / BreathsPerMinute;
    internal double CycleSeconds => MotionSeconds + HoldOpenSeconds + PauseSeconds;
    internal float SampleCycle(double phase)
    {
        double elapsed = phase * CycleSeconds;
        double halfMotion = MotionSeconds * 0.5;
        if (elapsed < halfMotion) return Sample(elapsed / MotionSeconds);
        if (elapsed < halfMotion + HoldOpenSeconds) return 1f;
        elapsed -= HoldOpenSeconds;
        return elapsed >= MotionSeconds ? 0f : Sample(elapsed / MotionSeconds);
    }
    internal static float Sample(double phase) => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
}
