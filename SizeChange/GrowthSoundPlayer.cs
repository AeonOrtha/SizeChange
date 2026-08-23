using System;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Sound;
using Vector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace SizeChange;

/// <summary>
/// Plays an SCD through the same game wrapper used by VFXEditor to preview
/// sound effects. The initialization detour selects the requested SCD sound
/// entry and makes the resulting sound positional.
/// </summary>
internal sealed unsafe class GrowthSoundPlayer : IDisposable
{
    // Current signatures and call shape are cross-checked against VFXEditor's
    // ResourceLoader.Sound implementation.
    private const string PlaySoundSignature =
        "E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? FE C2";
    private const string InitializeSoundSignature =
        "E8 ?? ?? ?? ?? 8B 5D 77";

    private delegate nint PlaySoundDelegate(nint path, byte play);

    private delegate SoundData* InitializeSoundDelegate(
        SoundManager* manager,
        nint path,
        float volume,
        uint soundIndex,
        uint unknown1,
        bool unknown2,
        SoundVolumeCategory category);

    [Signature(PlaySoundSignature)]
    private readonly PlaySoundDelegate? playSound = null;

    [Signature(
        InitializeSoundSignature,
        DetourName = nameof(InitializeSoundDetour))]
    private readonly Hook<InitializeSoundDelegate>? initializeSoundHook = null;

    private nint requestedPath;
    private int requestedSoundIndex = -1;
    private float requestedVolume = 1f;
    private Vector3 requestedPosition;
    private SoundData* initializedSound;

    public GrowthSoundPlayer()
    {
        Plugin.GameInteropProvider.InitializeFromAttributes(this);
        initializeSoundHook?.Enable();
    }

    public void Dispose()
    {
        initializeSoundHook?.Dispose();
    }

    public string? TryPlay(
        string path,
        int soundIndex,
        float volume,
        Vector3 position)
    {
        if (playSound == null || initializeSoundHook == null)
        {
            return "The native SCD preview functions could not be located.";
        }

        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        nint pathPointer = Marshal.AllocHGlobal(pathBytes.Length + 1);

        try
        {
            Marshal.Copy(pathBytes, 0, pathPointer, pathBytes.Length);
            Marshal.WriteByte(pathPointer + pathBytes.Length, 0);

            requestedPath = pathPointer;
            requestedSoundIndex = soundIndex;
            requestedVolume = volume;
            requestedPosition = position;
            initializedSound = null;

            // The second argument is the game's actual play switch. VFXEditor
            // uses 1 here for its working SCD preview action.
            playSound(pathPointer, 1);

            return initializedSound == null
                ? "FFXIV did not initialize a sound from the SCD preview request."
                : null;
        }
        finally
        {
            requestedPath = 0;
            requestedSoundIndex = -1;
            requestedVolume = 1f;
            initializedSound = null;
            Marshal.FreeHGlobal(pathPointer);
        }
    }

    private SoundData* InitializeSoundDetour(
        SoundManager* manager,
        nint path,
        float volume,
        uint soundIndex,
        uint unknown1,
        bool unknown2,
        SoundVolumeCategory category)
    {
        bool isRequestedSound =
            requestedPath != 0 &&
            path == requestedPath &&
            requestedSoundIndex >= 0;

        SoundData* soundData = initializeSoundHook!.Original(
            manager,
            path,
            isRequestedSound ? requestedVolume : volume,
            isRequestedSound ? (uint)requestedSoundIndex : soundIndex,
            unknown1,
            unknown2,
            category);

        if (isRequestedSound && soundData != null)
        {
            soundData->SetPosition(
                true,
                requestedPosition.X,
                requestedPosition.Y,
                requestedPosition.Z);
            initializedSound = soundData;
        }

        return soundData;
    }
}
