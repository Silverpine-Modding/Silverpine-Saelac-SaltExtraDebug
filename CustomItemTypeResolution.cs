using System;
using System.IO;
using System.Reflection;
using SilverpineMods.CustomItemFreezeFix;

namespace SaltExtraDebug;

// The game saves component FullName (not AssemblyQualifiedName). Its native
// Type.GetType call cannot normally see plugin assemblies. Unity's Mono raises
// TypeResolve for these misses, so no generic-method or preloader patch is needed.
internal static class CustomItemTypeResolution
{
    private static readonly object Gate = new object();
    private static bool _installed;
    [ThreadStatic] private static bool _resolving;

    internal static void Install()
    {
        lock (Gate)
        {
            if (_installed) return;
            AppDomain.CurrentDomain.TypeResolve += Resolve;
            _installed = true;
        }
        // This static subscription must survive the BepInEx startup host being
        // destroyed. It captures no plugin/Unity objects and is installed once.
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        if (_resolving || string.IsNullOrEmpty(args.Name)) return null;
        _resolving = true;
        try
        {
            // Prefer the migrated types if an old DLL was accidentally loaded.
            // The legacy pair should still be removed from active installation.
            if (args.Name == typeof(ManualAppearanceComponent).FullName
                || args.Name == typeof(ManualQualityComponent).FullName)
                return typeof(ManualQualityComponent).Assembly;

            // Match the old patcher's loaded-assembly lookup for other mods'
            // serializable components too. No negative cache: plugins can load
            // additional assemblies later in the session.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetType(args.Name, throwOnError: false) != null)
                        return assembly;
                }
                catch (TypeLoadException) { }
                catch (FileNotFoundException) { }
                catch (FileLoadException) { }
            }
            return null;
        }
        finally { _resolving = false; }
    }
}
