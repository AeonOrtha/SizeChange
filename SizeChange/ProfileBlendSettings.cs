using System;

namespace SizeChange;

[Serializable]
public sealed class ProfileBlendSettings
{
    public bool Enabled { get; set; }
    public Guid TargetProfileId { get; set; }
    public float StartScale { get; set; } = 1f;
    public float EndScale { get; set; } = 3f;
    public float BaseHeightOffset { get; set; }
    public float TargetHeightOffset { get; set; }
    public float TransitionSeconds { get; set; } = .35f;

    public float Progress(float settledScale) => !Enabled || !float.IsFinite(settledScale) ? 0 :
        Math.Clamp((settledScale - StartScale) / Math.Max(0.001f, EndScale - StartScale), 0, 1);

    // Extra root Y associated with each endpoint, before existing pose/growth
    // height layers. Uses exactly the same progress as the bone blend.
    public float HeightOffset(float progress) => !Enabled ? 0 :
        float.Lerp(ClampHeight(BaseHeightOffset), ClampHeight(TargetHeightOffset),
            float.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0);

    private static float ClampHeight(float value) => float.IsFinite(value) ? Math.Clamp(value, -100, 100) : 0;

    public void Validate()
    {
        StartScale = float.IsFinite(StartScale) ? Math.Clamp(StartScale, 0.01f, 9999f) : 1f;
        EndScale = float.IsFinite(EndScale) ? Math.Clamp(EndScale, StartScale + 0.01f, 10000f) : Math.Max(3, StartScale + 0.01f);
        BaseHeightOffset = ClampHeight(BaseHeightOffset);
        TargetHeightOffset = ClampHeight(TargetHeightOffset);
        TransitionSeconds = float.IsFinite(TransitionSeconds) ? Math.Clamp(TransitionSeconds, .05f, 10f) : .35f;
    }
}
