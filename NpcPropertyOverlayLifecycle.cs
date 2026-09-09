using HarmonyLib;

namespace SaltExtraDebug;

[HarmonyPatch(typeof(Player), "Start")]
internal static class NpcPropertyOverlayLifecycle
{
    private static void Postfix(Player __instance)
    {
        EnsureForPlayer(__instance);
    }

    internal static void EnsureForPlayer(Player player)
    {
        if (player == null)
        {
            return;
        }

        // Runs for the actual gameplay player, including a replacement loaded
        // from a save. Never attach this to Plugin.gameObject: it is temporary.
        NpcPropertyBoundaryOverlay overlay = player.GetComponent<NpcPropertyBoundaryOverlay>();
        if (overlay == null)
        {
            overlay = player.gameObject.AddComponent<NpcPropertyBoundaryOverlay>();
        }
        overlay.enabled = true;
        overlay.RefreshNow();
    }
}
