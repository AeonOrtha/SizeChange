using System;
using System.Collections.Generic;
using System.Numerics;

namespace SizeChange;

// Optional presentation module: snapshots and owned VFX handles only.
// Removing this component cannot change the drain's growth calculations.
internal interface IDrainProjectileVfx
{
    long CreateProjectile(string path, Vector3 position, float scale);
    bool MoveProjectile(long id, Vector3 position);
    void RemoveProjectile(long id);
}

internal sealed class DrainProjectilePlayer(IDrainProjectileVfx vfx)
{
    internal const int GlobalMaximum = 128;
    private readonly record struct Receiver(nint Address, uint EntityId);
    private readonly record struct Destination(Vector3 Position, SizeDrainSettings Settings);
    private sealed class Flight
    {
        public long Travel, Launch, Impact;
        public Receiver Receiver;
        public Vector3 Start;
        public float Age, ArrivalAge, Duration, LaunchDuration, ImpactDuration, Scale, Height;
        public string ImpactPath = string.Empty;
        public bool Arrived;
    }
    private readonly List<Flight> flights = new();
    private readonly Dictionary<Receiver, Destination> receivers = new();
    private readonly Dictionary<Receiver, int> counts = new();
    public int ActiveCount => flights.Count;
    public void BeginFrame() => receivers.Clear();
    public void SetReceiver(nint address, uint entityId, Vector3 position, SizeDrainSettings settings)
    {
        if (settings.ProjectileEnabled && Finite(position))
            receivers[new(address, entityId)] = new(position, settings);
    }
    public void Launch(nint address, uint entityId, Vector3 source)
    {
        var receiver = new Receiver(address, entityId);
        if (!receivers.TryGetValue(receiver, out var destination) || !Finite(source)) return;
        var s = destination.Settings;
        if (flights.Count >= GlobalMaximum || counts.GetValueOrDefault(receiver) >= s.MaximumProjectiles) return;
        if (string.IsNullOrWhiteSpace(s.ProjectilePath) && string.IsNullOrWhiteSpace(s.ProjectileLaunchPath) &&
            string.IsNullOrWhiteSpace(s.ProjectileImpactPath)) return;
        source.Y += s.ProjectileSourceHeight;
        var flight = new Flight {
            Receiver = receiver, Start = source, Duration = s.ProjectileTravelSeconds,
            LaunchDuration = s.ProjectileLaunchSeconds, ImpactDuration = s.ProjectileImpactSeconds,
            ImpactPath = s.ProjectileImpactPath, Scale = s.ProjectileScale, Height = s.ProjectileReceiverHeight,
            Travel = Create(s.ProjectilePath, source, s.ProjectileScale),
            Launch = Create(s.ProjectileLaunchPath, source, s.ProjectileScale)
        };
        flights.Add(flight);
        counts[receiver] = counts.GetValueOrDefault(receiver) + 1;
    }
    private long Create(string path, Vector3 position, float scale) =>
        string.IsNullOrWhiteSpace(path) ? 0 : vfx.CreateProjectile(path, position, scale);
    private void Remove(ref long id) { if (id != 0) vfx.RemoveProjectile(id); id = 0; }

    public void EndFrame(float seconds, bool stop = false)
    {
        if (stop) { Clear(); return; }
        seconds = float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, 1f) : 0f;
        for (int i = flights.Count - 1; i >= 0; i--)
        {
            var f = flights[i];
            bool remove = !receivers.TryGetValue(f.Receiver, out var destination);
            if (!remove)
            {
                f.Age += seconds;
                var target = destination.Position;
                target.Y += f.Height;
                if (f.Age >= f.LaunchDuration) Remove(ref f.Launch);
                if (!f.Arrived)
                {
                    float progress = Math.Clamp(f.Age / Math.Max(.05f, f.Duration), 0f, 1f);
                    if (f.Travel != 0 && !vfx.MoveProjectile(f.Travel, Vector3.Lerp(f.Start, target, progress)))
                        Remove(ref f.Travel);
                    if (progress >= 1f)
                    {
                        Remove(ref f.Travel);
                        f.Arrived = true;
                        f.ArrivalAge = f.Age;
                        f.Impact = Create(f.ImpactPath, target, f.Scale);
                    }
                }
                else if (f.Impact != 0 && !vfx.MoveProjectile(f.Impact, target)) Remove(ref f.Impact);
                if (f.Arrived && f.Age >= f.ArrivalAge + f.ImpactDuration) Remove(ref f.Impact);
                remove = f.Arrived && f.Impact == 0 && f.Launch == 0;
                if (counts.GetValueOrDefault(f.Receiver) > destination.Settings.MaximumProjectiles) remove = true;
            }
            if (remove) { Release(f); flights.RemoveAt(i); }
        }
    }
    private void Release(Flight f)
    {
        Remove(ref f.Travel); Remove(ref f.Launch); Remove(ref f.Impact);
        int remaining = counts.GetValueOrDefault(f.Receiver) - 1;
        if (remaining <= 0) counts.Remove(f.Receiver); else counts[f.Receiver] = remaining;
    }
    public void Clear()
    {
        foreach (var flight in flights) Release(flight);
        flights.Clear(); counts.Clear(); receivers.Clear();
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
