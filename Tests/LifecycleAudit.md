# Startup-host lifecycle audit (0.25.0)

The BepInEx runtime log confirms that Silverpine destroys plugin startup hosts
during bootstrap. Plugin configuration entries and Harmony hooks are static or
held by the patch system, so destruction of the startup component must not undo
hooks or own long-running gameplay work.

| Feature | Runtime owner / conclusion |
| --- | --- |
| Property boundaries | Previously attached to `Plugin.gameObject`: affected. Now attached idempotently from `Player.Start`, with menu-open recovery. Geometry is detached in world space; the controller clears geometry/material in `OnDisable`. |
| Whole-world-tile camera and larger labels | Overview component belongs to the live camera and never detaches it from its gameplay parent. Camera/FOV snapshots restore on OFF, `OnDisable`, before save load, and before Spectate NPC. A replacement player or external camera target change ends the old overview. Labels scale on the player-owned overlay, not a startup-owned updater. |
| Debug activation and dialogue debug button | Static Harmony patches, no startup-host callbacks. |
| Cheat toggles and close/picker buttons | Static patch/callback methods; generated objects belong to the live game UI and are recreated when opened. |
| Free Build, harvesting, instant interaction and debug-spawn deconstruction | Static Harmony patches; added deconstruction components belong to spawned game objects. The short construction-turn callback belongs to the active construction interaction and is removed by its completion callback, not plugin teardown. |
| God Mode, stat lock, gold, durability, energy and negative-status prevention | Static Harmony patches using static config entries and the current player. |
| Talk-in-combat bypass | Static prefix/postfix/finalizer, no continuing work on the startup host. |
| Daily reset and item duplication | Actions invoked by live game UI, no startup-owned timer or coroutine. |
| NPC teleport | Coroutine runs on the gameplay player and validates the selected NPC/player before use. |
| Three-turn wait and restoration | Native serializable NPC routine/timer plus static scoped Harmony patches; not tied to plugin lifetime. |

No other production MonoBehaviour is attached to the plugin startup object.
There is no plugin `OnDestroy`/`OnDisable` unpatch operation and no plugin-owned
recurring event subscription. No other feature shared this specific defect.
This is a source/lifecycle audit plus offline regression testing, not a claim
that every gameplay feature has been re-tested interactively in Unity.
