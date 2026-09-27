using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace SizeChange;

internal sealed class CustomizeHeartbeatApi : ICustomizeHeartbeatApi
{
    private readonly ICallGateSubscriber<(int, int)> version;
    private readonly ICallGateSubscriber<bool> valid;
    private readonly ICallGateSubscriber<IList<(Guid, string, string, List<(string, ushort, byte, ushort)>, int, bool)>> list;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> active;
    private readonly ICallGateSubscriber<Guid, (int, string?)> get;
    private readonly ICallGateSubscriber<ushort, (int, string?)> getTemporary;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> set;
    private readonly ICallGateSubscriber<Guid, int> delete;

    public CustomizeHeartbeatApi(IDalamudPluginInterface pluginInterface)
    {
        version = pluginInterface.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        valid = pluginInterface.GetIpcSubscriber<bool>("CustomizePlus.General.IsValid");
        list = pluginInterface.GetIpcSubscriber<IList<(Guid, string, string, List<(string, ushort, byte, ushort)>, int, bool)>>("CustomizePlus.Profile.GetList");
        active = pluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        get = pluginInterface.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        getTemporary = pluginInterface.GetIpcSubscriber<ushort, (int, string?)>("CustomizePlus.Profile.GetTemporaryProfileOnCharacter");
        set = pluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        delete = pluginInterface.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");
    }

    public void CheckAvailable()
    {
        if (version.InvokeFunc().Item1 != 6)
            throw new InvalidOperationException("This heartbeat integration requires Customize+ IPC version 6.");
        if (!valid.InvokeFunc()) throw new InvalidOperationException("Customize+ is not ready.");
    }

    public IReadOnlyList<(Guid Id, string Name)> GetProfiles()
        => list.InvokeFunc().Select(p => (p.Item1, p.Item2)).OrderBy(p => p.Item2).ToList();

    public Guid GetActiveProfile(ushort index)
    {
        var (code, id) = active.InvokeFunc(index);
        if (code != 0 || id == null || id == Guid.Empty)
            throw new InvalidOperationException("No active Customize+ base profile. Select one in Bone Heartbeat settings.");
        return id.Value;
    }

    public string GetProfile(Guid id)
    {
        var (code, json) = get.InvokeFunc(id);
        if (code != 0 || string.IsNullOrEmpty(json))
            throw new InvalidOperationException("The selected Customize+ base profile is unavailable.");
        return json;
    }

    public bool HasTemporaryProfile(ushort index)
    {
        // Optional endpoint; stock API 6.4 does not expose it. Absence is not
        // proof that the slot is free, so the UI also states the exclusivity rule.
        try
        {
            var (code, json) = getTemporary.InvokeFunc(index);
            return code == 0 && !string.IsNullOrEmpty(json);
        }
        catch (IpcNotReadyError) { return false; }
    }

    public Guid SetTemporary(ushort index, string json)
    {
        var (code, id) = set.InvokeFunc(index, json);
        if (code != 0 || id == null || id == Guid.Empty)
            throw new InvalidOperationException($"Customize+ rejected the heartbeat profile (code {code}).");
        return id.Value;
    }

    public bool DeleteTemporary(Guid id)
    {
        int code = delete.InvokeFunc(id);
        // Delete by our last returned ID, never by actor: don't remove another
        // plugin's replacement. NotFound means ours has already gone.
        return code == 0 || code == 3;
    }
}
