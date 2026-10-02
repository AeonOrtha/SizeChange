using System;

namespace SizeChange;

internal sealed class HeartbeatSoundPlayer : IDisposable
{
    private readonly HeartbeatLoopPlayer loop;
    private readonly BeatVoice first;
    private readonly BeatVoice second;
    private double previousPhase = -double.Epsilon;
    private long generation = -1;
    private bool wasLoop;
    private bool wasEnabled;
    private bool wasRun;
    public string Status => wasLoop ? loop.Status :
        first.Error.Length > 0 ? "First beat: " + first.Error :
        second.Error.Length > 0 ? "Second beat: " + second.Error : string.Empty;

    public HeartbeatSoundPlayer(IHeartbeatSoundVoice loopVoice, IHeartbeatSoundVoice firstVoice, IHeartbeatSoundVoice secondVoice)
    {
        loop = new HeartbeatLoopPlayer(loopVoice);
        first = new BeatVoice(firstVoice);
        second = new BeatVoice(secondVoice);
    }

    public void Retry()
    {
        loop.Retry();
        first.Retry();
        second.Retry();
    }

    public void Tick(HeartbeatSoundSettings settings, bool pulsing, bool available, float seconds,
        double phase = 0, long phaseGeneration = 0, float settledScale = 1f)
    {
        if (settings.Loop != wasLoop)
        {
            Retry();
            wasLoop = settings.Loop;
            // Switching modes mid-cycle must not play missed beats.
            previousPhase = phase;
        }
        if (settings.Enabled && !wasEnabled) Retry();
        wasEnabled = settings.Enabled;
        if (generation != phaseGeneration || phase < previousPhase)
        {
            generation = phaseGeneration;
            previousPhase = -double.Epsilon;
        }
        float playbackRate = settings.RateForSize(settledScale);
        float firstVolume = settings.VolumeForSize(settings.FirstBeat.Volume, settledScale);
        float secondVolume = settings.VolumeForSize(settings.SecondBeat.Volume, settledScale);
        first.Sync(settings.FirstBeat, playbackRate, firstVolume);
        second.Sync(settings.SecondBeat, playbackRate, secondVolume);
        if (settings.Loop)
        {
            loop.Tick(settings, pulsing, available, seconds, playbackRate, settledScale);
            previousPhase = phase;
            wasRun = false;
            return;
        }
        bool run = settings.Enabled && pulsing && available;
        if (!run)
        {
            first.Stop();
            second.Stop();
            previousPhase = phase;
            wasRun = false;
            return;
        }
        // The first visual submission can be delayed by the 60 Hz IPC limiter.
        // Join the initial first lobe when it becomes visible rather than lose
        // the first hit entirely at high render frame rates.
        if (!wasRun && phase > 0 && phase < 0.20) previousPhase = -double.Epsilon;
        wasRun = true;
        bool hitFirst = Math.Floor(phase) > Math.Floor(previousPhase);
        bool hitSecond = Math.Floor(phase - BoneHeartbeatMath.SecondBeatStart) >
            Math.Floor(previousPhase - BoneHeartbeatMath.SecondBeatStart);
        if (phase > previousPhase)
        {
            // A stalled frame can cross several onsets: only play the latest,
            // never emit a burst of historical sounds on recovery.
            double latestFirst = Math.Floor(phase);
            double latestSecond = Math.Floor(phase - BoneHeartbeatMath.SecondBeatStart) + BoneHeartbeatMath.SecondBeatStart;
            if (hitFirst && (!hitSecond || latestFirst > latestSecond)) first.Play(settings.FirstBeat, playbackRate, firstVolume);
            else if (hitSecond) second.Play(settings.SecondBeat, playbackRate, secondVolume);
        }
        previousPhase = phase;
    }

    public void Dispose()
    {
        loop.Dispose();
        first.Stop();
        second.Stop();
    }

    private sealed class BeatVoice(IHeartbeatSoundVoice voice)
    {
        private string path = string.Empty;
        private int index = -1;
        private bool validated;
        public string Error { get; private set; } = string.Empty;
        public void Sync(HeartbeatSoundSlot slot, float playbackRate, float volume)
        {
            string selectedPath = slot.Path.Trim().Replace('\\', '/');
            if (selectedPath != path || slot.Index != index)
            {
                Retry();
                path = selectedPath;
                index = slot.Index;
            }
            if (slot.Volume <= 0) Stop();
            else if (Error.Length == 0)
            {
                try { voice.SetPlaybackRate(playbackRate); voice.SetVolume(volume); }
                catch (Exception ex) { Stop(); Error = ex.Message; }
            }
        }
        public void Retry() { Stop(); validated = false; Error = string.Empty; }
        public void Stop() => voice.Stop();
        public void Play(HeartbeatSoundSlot slot, float playbackRate, float volume)
        {
            if (path.Length == 0 || slot.Volume <= 0 || Error.Length > 0) return;
            try
            {
                if (!validated)
                {
                    string? error = voice.Validate(path, index);
                    if (error != null) throw new InvalidOperationException(error);
                    validated = true;
                }
                // Bound ownership to one voice per slot, even if an SCD has
                // an unexpectedly long tail or internal loop points.
                Stop();
                voice.Start(path, index, volume, playbackRate);
            }
            catch (Exception ex) { Stop(); Error = ex.Message; }
        }
    }
}
