using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace SizeChange;

// Remove this adapter + Hook + Settings/JSON files and their Plugin/UI wiring
// to remove the feature. The growth simulation never depends on this service.
internal sealed class PlayerProfileHeightCompatibility : IDisposable
{
    private readonly Func<PlayerProfileHeightSettings> settings;
    private readonly PlayerProfileHeightHook hook;
    private readonly ICallGateSubscriber<(int, int)> version;
    private readonly ICallGateSubscriber<bool> ready;
    private long nextCheck;
    private bool failed;
    private readonly Action<string> log;
    private string? availabilityStatus;
    public string Status => availabilityStatus ?? hook.Status;
    public string CallStatus => $"Incoming: {hook.ReceivedCalls} | Applied: {hook.AppliedCalls}";
    public string LastCall => hook.LastCall;
    public int AppliedCount => hook.AppliedCount;
    public PlayerProfileHeightCompatibility(Func<PlayerProfileHeightSettings> settings,
        Func<ushort, PlayerProfileHeightHook.Actor?> resolve,
        Func<PlayerProfileHeightHook.Actor, bool> eligible,
        IDalamudPluginInterface plugin, Action<string> log)
    {
        this.settings = settings; this.log = log;
        hook = new(resolve, actor => eligible(actor) && settings().Includes(actor.Name) ? settings().Offset : null, log);
        version = plugin.GetIpcSubscriber<(int,int)>("CustomizePlus.General.GetApiVersion");
        ready = plugin.GetIpcSubscriber<bool>("CustomizePlus.General.IsValid");
    }
    public void Retry() { failed = false; nextCheck = 0; }
    public void Tick()
    {
        if (!settings().Enabled)
        {
            hook.Stop(); availabilityStatus = null; failed = false; return;
        }
        if (Environment.TickCount64 >= nextCheck)
        {
            nextCheck = Environment.TickCount64 + 1000;
            try
            {
                var api = version.InvokeFunc();
                if (api.Item1 != 6)
                {
                    hook.Stop(false); failed = false;
                    availabilityStatus = $"Unsupported Customize+ API: {api.Item1}.{api.Item2}"; return;
                }
                if (!ready.InvokeFunc())
                {
                    hook.Stop(false); failed = false;
                    availabilityStatus = "Customize+ is not ready"; return;
                }
            }
            catch (Exception ex)
            {
                hook.Stop(false); failed = false;
                availabilityStatus = "Customize+ IPC unavailable: " + ex.GetBaseException().Message; return;
            }
            availabilityStatus = null;
            if (!hook.Installed && !failed)
            {
                var types = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name == "CustomizePlus")
                    .Select(a => a.GetType("CustomizePlus.Api.CustomizePlusIpc"))
                    .Where(t => t != null).ToArray();
                if (types.Length == 1) failed = !hook.Install(types[0]!);
                else availabilityStatus = $"Customize+ runtime types found: {types.Length}; expected 1";
            }
        }
        hook.Tick();
    }
    public void Dispose()
    {
        hook.StopAccepting();
        if (Plugin.Framework.IsInFrameworkUpdateThread) Cleanup();
        else _ = FinishDispose();
    }
    private void Cleanup()
    {
        bool available;
        try { available = version.InvokeFunc().Item1 == 6 && ready.InvokeFunc(); }
        catch { available = false; }
        if (!available) hook.Stop(false);
        hook.Dispose();
    }
    private async System.Threading.Tasks.Task FinishDispose()
    {
        try { await Plugin.Framework.RunOnFrameworkThread(Cleanup); }
        catch (Exception ex) { log("Height cleanup failed: " + ex.Message); }
    }
}
