using System;
using System.Collections.Generic;

namespace SizeChange;

internal interface ICustomizeHeartbeatApi
{
    void CheckAvailable();
    IReadOnlyList<(Guid Id, string Name)> GetProfiles();
    Guid GetActiveProfile(ushort index);
    string GetProfile(Guid id);
    bool HasTemporaryProfile(ushort index);
    Guid SetTemporary(ushort index, string json);
    bool DeleteTemporary(Guid id);
}

internal sealed class BoneHeartbeatPlayer : IDisposable
{
    private readonly ICustomizeHeartbeatApi api;
    private Guid? ownedProfile;
    private BoneHeartbeatProfile? baseline;
    private string? baselineJson;
    private Guid requestedProfile;
    private nint actorAddress;
    private uint entityId;
    private double phase;
    private readonly List<HeartbeatBone> resolvedBones = new();
    private float envelope;
    private float sendElapsed;
    private float baselineElapsed = 1f;
    private float cleanupElapsed = 1f;
    private bool faulted;
    private bool wasEnabled;
    private bool wasJawEnabled;
    private bool wasGrowthEnabled;
    private bool wasHeightEnabled;
    private double jawPhase;
    private float jawEnvelope;
    private string? lastSent;

    public double Phase => phase;
    public long PhaseGeneration { get; private set; }

    public bool IsPulsing => ownedProfile.HasValue && !faulted && envelope > 0f;

    public string Status { get; private set; } = "Disabled.";
    public IReadOnlyList<(Guid Id, string Name)> Profiles { get; private set; } = Array.Empty<(Guid, string)>();

    public BoneHeartbeatPlayer(ICustomizeHeartbeatApi api) => this.api = api;

    public void RefreshProfiles()
    {
        try
        {
            api.CheckAvailable();
            Profiles = api.GetProfiles();
        }
        catch (Exception ex) { Status = "Customize+ unavailable: " + ex.Message; }
    }

    public void Retry()
    {
        if (!Release()) return;
        faulted = false;
        baselineElapsed = 1f;
        baseline = null;
        baselineJson = null;
        Status = "Ready.";
    }

    public void Tick(BoneHeartbeatSettings settings, ushort objectIndex, nint address,
        uint actorEntityId, bool allowed, bool accumulating, float seconds, bool growing = false,
        float pendingDamageRatio = 0f, float maximumDamageRatio = 1f, float settledScale = 1f, float rootHeightOffset = 0f)
    {
        seconds = float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, 1f) : 0f;
        cleanupElapsed += seconds;
        bool heightEnabled = float.IsFinite(rootHeightOffset) && rootHeightOffset != 0f;
        bool growthEnabled = (settings.CustomGrowthEnabled && settings.Bones.Count > 0 &&
            settings.CustomGrowthPerScale > 0f && settings.CustomGrowthLimit > 0f) || settings.Chains.Exists(chain => chain.GrowthEnabled && chain.GrowthPerScale > 0f && chain.GrowthLimit > 0f);
        if ((heightEnabled && !wasHeightEnabled || growthEnabled && !wasGrowthEnabled || settings.Enabled && !wasEnabled || settings.Jaw.Enabled && !wasJawEnabled) && faulted) Retry();
        wasHeightEnabled = heightEnabled;
        wasGrowthEnabled = growthEnabled;
        wasEnabled = settings.Enabled;
        wasJawEnabled = settings.Jaw.Enabled;
        if (faulted && ownedProfile.HasValue && cleanupElapsed < 1f) return;
        if (address != actorAddress || actorEntityId != entityId || requestedProfile != settings.BaseProfileId)
        {
            if (!Release()) return;
            actorAddress = address;
            entityId = actorEntityId;
            requestedProfile = settings.BaseProfileId;
            baseline = null;
            baselineJson = null;
            baselineElapsed = 1f;
            faulted = false;
        }

        settings.ResolveBones(resolvedBones, settledScale);
        bool growthRun = resolvedBones.Exists(bone => bone.GrowthOffset > 0f);
        bool bonesEnabled = settings.Enabled && settings.Strength > 0f && resolvedBones.Exists(bone => bone.Strength > 0f);
        bool jawEnabled = settings.Jaw.Enabled && settings.Jaw.OpeningDegrees > 0f;
        if (!allowed || address == 0 || (!bonesEnabled && !jawEnabled && !growthRun && !heightEnabled))
        {
            if (faulted && ownedProfile.HasValue && cleanupElapsed < 1f) return;
            if (Release()) Status = settings.Enabled ? "Inactive (self unavailable, disabled, or no bones/strength)." : "Disabled.";
            return;
        }
        if (faulted)
        {
            if (ownedProfile.HasValue && cleanupElapsed >= 1f) Release();
            return;
        }

        bool run = bonesEnabled && (settings.Mode == BoneHeartbeatMode.Always || accumulating);
        bool jawRun = jawEnabled && (settings.Jaw.Mode == JawBreathingMode.Always || growing);
        if (!bonesEnabled) envelope = 0;
        if (!jawEnabled) jawEnvelope = 0;
        // Fade an interrupted beat back to baseline instead of snapping when
        // the accumulator releases. Hard disable/logout removes our profile.
        envelope = Math.Clamp(envelope + (run ? seconds : -seconds) / 0.12f, 0f, 1f);
        jawEnvelope = Math.Clamp(jawEnvelope + (jawRun ? seconds : -seconds) / 0.25f, 0f, 1f);
        if (envelope <= 0f && phase != 0) { PhaseGeneration++; phase = 0; }
        if (jawEnvelope <= 0f) jawPhase = 0;
        if (!run && !jawRun && !growthRun && !heightEnabled && envelope <= 0f && jawEnvelope <= 0f)
        {
            if (Release()) Status = "Waiting for accumulated damage.";
            return;
        }

        try
        {
            baselineElapsed += seconds;
            if (baseline == null || baselineElapsed >= 1f)
            {
                api.CheckAvailable();
                Guid id = settings.BaseProfileId == Guid.Empty
                    ? api.GetActiveProfile(objectIndex) : settings.BaseProfileId;
                string json = api.GetProfile(id);
                if (json != baselineJson)
                {
                    baseline = new BoneHeartbeatProfile(json);
                    baselineJson = json;
                    lastSent = null;
                }
                baselineElapsed = 0f;
            }

            float bpm = settings.DamageDrivenBpm
                ? BoneHeartbeatMath.DamageBpm(settings.BeatsPerMinute, settings.MaximumBeatsPerMinute,
                    pendingDamageRatio, maximumDamageRatio)
                : settings.BeatsPerMinute;
            if (envelope > 0f) phase += seconds * bpm / 60.0;
            if (jawEnvelope > 0f) jawPhase = (jawPhase + seconds / settings.Jaw.CycleSeconds) % 1.0;
            sendElapsed += seconds;
            if (sendElapsed < 1f / 60f) return;
            sendElapsed %= 1f / 60f;
            float jawAngle = -settings.Jaw.SampleCycle(jawPhase) * settings.Jaw.OpeningDegrees * jawEnvelope;
            string frame = baseline!.Build(resolvedBones, envelope, settings.Strength, phase, jawAngle, 2, rootHeightOffset);
            if (frame != lastSent)
            {
                if (!ownedProfile.HasValue && api.HasTemporaryProfile(objectIndex))
                    throw new InvalidOperationException("Another temporary Customize+ profile is active. Stop the other morph, then Retry.");
                ownedProfile = api.SetTemporary(objectIndex, frame);
                lastSent = frame;
            }
            Status = run ? $"Heartbeat active — {bpm:0} BPM." : jawRun ? "Jaw breathing active." : growthRun ? "Bone growth active." : heightEnabled ? "Height offset active." : "Returning to baseline.";
        }
        catch (Exception ex)
        {
            faulted = true;
            Release();
            Status = "Heartbeat stopped: " + ex.Message + " Use Reload / Retry after fixing the cause.";
        }
    }

    private bool Release()
    {
        cleanupElapsed = 0f;
        if (ownedProfile is Guid id)
        {
            try
            {
                if (!api.DeleteTemporary(id))
                {
                    Status = "Customize+ could not remove our temporary profile; retrying cleanup.";
                    faulted = true;
                    return false;
                }
                ownedProfile = null;
            }
            catch (Exception ex)
            {
                Status = "Waiting for Customize+ to restore the baseline: " + ex.Message;
                faulted = true;
                return false;
            }
        }
        if (phase != 0) PhaseGeneration++;
        phase = 0;
        envelope = 0;
        jawPhase = 0;
        jawEnvelope = 0;
        sendElapsed = 0;
        lastSent = null;
        baselineElapsed = 1f;
        return true;
    }

    public void Dispose() => Release();
}
