using System;

namespace SizeChange;

[Serializable]
public sealed class ProfileBlendSettings
{
    public bool Enabled { get; set; }
    public Guid TargetProfileId { get; set; }
    public float StartScale { get; set; } = 1f;
    public float EndScale { get; set; } = 3f;

    public float Progress(float settledScale) => !Enabled || !float.IsFinite(settledScale) ? 0 :
        Math.Clamp((settledScale - StartScale) / Math.Max(0.001f, EndScale - StartScale), 0, 1);

    public void Validate()
    {
        StartScale = float.IsFinite(StartScale) ? Math.Clamp(StartScale, 0.01f, 9999f) : 1f;
        EndScale = float.IsFinite(EndScale) ? Math.Clamp(EndScale, StartScale + 0.01f, 10000f) : Math.Max(3, StartScale + 0.01f);
    }
}
