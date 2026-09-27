using System;

namespace SizeChange;

internal static class BoneHeartbeatMath
{
    // One BPM cycle contains both beats: a strong "lub", a smaller "dub",
    // then rest. Smooth lobes have zero slope at their boundaries.
    public static float Sample(double phase)
    {
        phase -= Math.Floor(phase);
        return Lobe(phase, 0.0, 0.20) + 0.65f * Lobe(phase, 0.26, 0.18);
    }

    private static float Lobe(double phase, double start, double duration)
    {
        double t = (phase - start) / duration;
        if (t <= 0 || t >= 1) return 0f;
        double sine = Math.Sin(Math.PI * t);
        return (float)(sine * sine);
    }
}
