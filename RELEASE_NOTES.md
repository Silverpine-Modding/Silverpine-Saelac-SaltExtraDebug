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
