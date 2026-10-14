using System;

namespace SizeChange;

// Compare world position with an anchor, rather than each preceding frame, so
// slow movement still accumulates past the jitter threshold at high frame rates.
internal struct MonsterIdleMovement
{
    private const float DistanceThreshold = 0.005f;
    private const long StillnessMilliseconds = 300;
    private bool initialized, settling;
    private float anchorX, anchorY, anchorZ;
    private long lastMovement, lastSample;

    public bool Observe(float x, float y, float z, long now)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
        { initialized = false; settling = true; return false; }
        if (!initialized)
        {
            initialized = true;
            anchorX = x; anchorY = y; anchorZ = z;
            lastMovement = lastSample = now;
            return !settling;
        }
        float dx = x - anchorX, dy = y - anchorY, dz = z - anchorZ;
        if (now < lastSample || dx * dx + dy * dy + dz * dz >= DistanceThreshold * DistanceThreshold)
        {
            anchorX = x; anchorY = y; anchorZ = z;
            lastMovement = now; settling = true;
        }
        lastSample = now;
        if (settling && now - lastMovement >= StillnessMilliseconds) settling = false;
        return !settling;
    }
}
