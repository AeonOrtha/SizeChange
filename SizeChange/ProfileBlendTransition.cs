using System;

namespace SizeChange;

// A critically damped transition of profile weight. Retargeting preserves the
// current value, so accumulator releases cannot step the body shape. This does
// not change earned size, pulse timing, the accumulator, or visual overshoot.
internal sealed class ProfileBlendTransition
{
    private double current, velocity;
    public float Current => (float)current;
    public void Reset() { current = velocity = 0; }

    public float Update(float target, float seconds, float transitionSeconds)
    {
        target = float.IsFinite(target) ? Math.Clamp(target, 0, 1) : 0;
        if (!float.IsFinite(seconds) || seconds <= 0) return Current;
        double dt = Math.Min(seconds, 1);
        double duration = float.IsFinite(transitionSeconds) ? Math.Clamp(transitionSeconds, .05f, 10f) : .35;
        double difference = current - target;
        // A reversed target should start moving back, not continue away from it.
        if (velocity * difference > 0) velocity = 0;
        double omega = 6.64 / duration; // About 99% of a step completed in duration.
        double c = velocity + omega * difference;
        double decay = Math.Exp(-omega * dt);
        double next = target + (difference + c * dt) * decay;
        velocity = (velocity - omega * c * dt) * decay;
        if ((next - target) * difference < 0 || Math.Abs(next - target) < .00001 && Math.Abs(velocity) < .0001)
        { next = target; velocity = 0; }
        current = Math.Clamp(next, 0, 1);
        return Current;
    }
}
