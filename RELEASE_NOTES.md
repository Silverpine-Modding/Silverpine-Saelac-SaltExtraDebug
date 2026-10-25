## SaltExtraDebug v0.26.1

### Independent debug-menu scrolling

- Remember each debug-list screen's own scroll position, including the main menu, Teleport NPC, Spectate NPC, nested native debug lists, and the manual custom-item wizard.
- New screens start at the top. Returning to the main menu or toggling a cheat restores its own position instead of inheriting a submenu's scroll.
- Restore after deferred row destruction and layout rebuilding, stop scroll inertia, and keep rapid close/reopen transitions from overwriting saved positions.
- State belongs to the actual list UI and is not written to saves/config. Unrelated menus sharing GenericListUI are not given debug scroll positions.

### Validation

Release build completed with zero warnings/errors. Passed 116 targeted scroll-state
and native Harmony-routing checks in the game's Mono runtime, covering all 15 native
debug-list draw sites, independent positions, toggle redraws, rapid transitions,
overscroll, and unrelated menus. The headless host cannot render Unity layouts;
in-game scroll-position confirmation is still needed.

## SaltExtraDebug v0.26.0

### Integrated manual custom-item generator

- Moved the manual item wizard and older enchantment-scroll compatibility repair into SaltExtraDebug. The native Generate Custom Item action now opens the integrated wizard.
- Replaced the preloader's save-loading rewrite with a runtime type-resolution handler. Existing appearance/rarity component names and binary data are unchanged; other loaded plugins' serializable types remain discoverable.
- No dependency on CustomItemFreezeFix, CustomItemPromptPatcher, CustomItemLoader, or ModdingTools. No generic-method serialization patches or OnDestroy cleanup.
- Upgrading requires moving both CustomItemFreezeFix.dll and CustomItemPromptPatcher.dll outside the active BepInEx folders, with the game closed. Back up the pair before retiring them.

### Validation

The release build succeeded without warnings/errors. All 43 native Mono migration
checks passed, reading component and full-item fixtures written by the backed-up legacy
plugin in a separate process, preserving appearance, every rarity, durability,
enchantment, component references, and deep cloning. They also verify other
plugin types, late-loaded types, missing-type handling, and real debug-action
Harmony routing without either legacy DLL loaded. Existing debug-mod regression
tests also passed. In-game testing confirmed a generated item remained unchanged
after saving, fully closing/reopening the game, and reloading. This confirms the
tested item; it is not a claim that every wizard combination was manually tested.

## SaltExtraDebug v0.25.1

### Property overlay correction

- Merge each NPC's touching/overlapping plot rectangles into a combined outline with one label per connected property, removing the horizontal strip lines and repeated names.
- Preserve irregular edges, courtyards/holes, disconnected properties, and per-NPC colors. Shared footprints retain inset outlines and stacked labels.
- Cache the merged geometry until the plot signature changes. No changes to actual ownership, native plot objects, or mod dependencies.

### Validation

Release build completed with zero warnings/errors. Passed 821 exact-union
geometry scenarios (47,966 assertions), including every 3x3 tile arrangement,
random overlapping rectangles, negative/large coordinates, shared footprints,
holes, and corner-only contacts. All 97 existing routine/lifecycle/camera
regression assertions also passed. In-game visual confirmation is still needed.

Use the installation instructions below to replace the DLL with the game closed.

## SaltExtraDebug v0.25.0

### New since v0.22.0

- **NPC property boundaries:** toggle colored world-space outlines and owner labels, with a consistent color per NPC and separate outlines for shared plots.
- **Larger property labels:** names maintain an approximately 24-pixel text height as the camera zoom changes.
- **View Whole World Tile:** center and zoom the camera to fit the current tile without moving the player. Turning it off restores the previous camera and visibility settings. Dungeon instances use their loaded turf bounds.
- **Teleport NPC To Player:** choose a loaded NPC to bring beside you. They stand still for three game turns, then resume their previous routine. Repeating the action restarts the wait; summoned NPCs can share a landing tile.

### Fixes and lifecycle handling

- Fixed property overlays disappearing when Silverpine destroys the temporary plugin startup object. The renderer now belongs to the gameplay player and can be recreated after save loading.
- Kept property outlines and labels fixed in world space rather than following player movement or sprite scaling.
- Preserved existing temporary/follower routines beneath the teleport wait, without adding dependencies on other plugins.
- Added camera-state restoration when loading a save, switching to NPC spectating, or disabling the camera controller.

### Installation

Requires Silverpine 1.7.3 with BepInEx 5.

Close the game and replace your existing `SaltExtraDebug.dll` with the one
from the ZIP or the standalone download. For a new installation, extract the
ZIP into `BepInEx/plugins/SaltExtraDebug/`. Keep only one copy of the plugin DLL
under `BepInEx/plugins`, including its subfolders. Existing cheat settings are
preserved. Do not load `debugtest.dll` or `FreeBuild.dll` alongside SaltExtraDebug
because their patches overlap.

Press **O** to open the debug menu. **View Whole World Tile** is a session-only
toggle; it does not load or discover neighboring world tiles.

### Validation

Release build completed without warnings or errors. All 97 offline regression
assertions passed across NPC routines, overlay lifecycle, camera restoration,
zoom fitting, and label sizing. These include modeled-engine tests; they are
not a substitute for in-game visual testing.
