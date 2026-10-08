using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace SizeChange;

// Harmony/MonoMod emits non-collectible helper assemblies. Those cannot resolve
// Harmony from Dalamud's collectible plugin context. Keep only the patch engine
// in a named process-lifetime context; never load SizeChange/Customize+ there.
// Reflection at this boundary prevents an implicit collectible Harmony load.
internal sealed class PlayerHeightPatchRuntime
{
    private const string ContextName = "SizeChange.Height.Harmony.2.4.2";
    private readonly object harmony;
    private readonly Type harmonyMethod;
    private readonly MethodInfo patch;
    private readonly MethodInfo unpatch;
    private readonly object all;
    private readonly string id;

    public PlayerHeightPatchRuntime(string id)
    {
        this.id = id;
        string path = Path.Combine(Path.GetDirectoryName(typeof(PlayerHeightPatchRuntime).Assembly.Location)!, "0Harmony.dll");
        var context = AssemblyLoadContext.All.FirstOrDefault(c => c.Name == ContextName)
            ?? new AssemblyLoadContext(ContextName, isCollectible: false);
        var assembly = context.Assemblies.FirstOrDefault(a => a.GetName().Name == "0Harmony")
            ?? context.LoadFromAssemblyPath(path);
        if (assembly.IsCollectible || assembly.GetName().Version != new Version(2, 4, 2, 0))
            throw new NotSupportedException("Expected non-collectible Harmony 2.4.2.");
        var type = assembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
        harmonyMethod = assembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true)!;
        var patchType = assembly.GetType("HarmonyLib.HarmonyPatchType", throwOnError: true)!;
        harmony = Activator.CreateInstance(type, id)!;
        patch = type.GetMethods().Single(m => m.Name == "Patch" && m.GetParameters().Length == 5);
        unpatch = type.GetMethod("Unpatch", new[] { typeof(MethodBase), patchType, typeof(string) })!;
        all = Enum.Parse(patchType, "All");
    }

    public void Patch(MethodInfo target, MethodInfo prefix, MethodInfo? postfix = null)
    {
        object Make(MethodInfo method) => Activator.CreateInstance(harmonyMethod, method)!;
        patch.Invoke(harmony, new object?[] { target, Make(prefix), postfix == null ? null : Make(postfix), null, null });
    }

    public void Unpatch(MethodInfo target) => unpatch.Invoke(harmony, new[] { (object)target, all, id });
}
