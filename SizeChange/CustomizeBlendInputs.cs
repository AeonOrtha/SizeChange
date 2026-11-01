using System;

namespace SizeChange;

// Poll persistent profiles, never our own generated temporary output. Kept out
// of the Added Players interception callback; it only reads these snapshots.
internal sealed class CustomizeBlendInputs(ICustomizeHeartbeatApi api)
{
    private long nextRead;
    private Guid baseId, targetId;
    private bool enabled;
    public string? BaseJson { get; private set; }
    public string? TargetJson { get; private set; }
    public string Status { get; private set; } = "Blend disabled";
    public void Retry() => nextRead = 0;

    public void Refresh(BoneHeartbeatSettings settings, bool loadBase)
    {
        Guid requestedBase = loadBase ? settings.BaseProfileId : Guid.Empty;
        if (enabled != settings.Blend.Enabled || baseId != requestedBase || targetId != settings.Blend.TargetProfileId)
        {
            enabled = settings.Blend.Enabled; baseId = requestedBase; targetId = settings.Blend.TargetProfileId;
            nextRead = 0; BaseJson = TargetJson = null;
        }
        if (!enabled) { Status = "Blend disabled"; return; }
        if (Environment.TickCount64 < nextRead) return;
        nextRead = Environment.TickCount64 + 1000;
        try
        {
            if (targetId == Guid.Empty) throw new InvalidOperationException("Choose a target profile");
            api.CheckAvailable();
            string targetJson = api.GetProfile(targetId);
            string? baseJson = baseId == Guid.Empty ? null : api.GetProfile(baseId);
            // Validate only on changes; never hand malformed transforms to C+.
            if (targetJson != TargetJson) CustomizeProfileBlend.Parse(targetJson);
            if (baseJson != null && baseJson != BaseJson) CustomizeProfileBlend.Parse(baseJson);
            BaseJson = baseJson; TargetJson = targetJson;
            Status = "Blend ready";
        }
        catch (Exception ex)
        {
            BaseJson = TargetJson = null;
            Status = "Blend unavailable: " + ex.GetBaseException().Message;
        }
    }
}
