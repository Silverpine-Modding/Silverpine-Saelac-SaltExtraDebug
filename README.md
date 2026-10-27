# Salt Extra Debug

A BepInEx 5 plugin for Silverpine that enables the game's debug menu and adds
persistent runtime toggles for Free Build.

## Debug menu

Each debug-list screen remembers its own scroll position while that UI is loaded.
The main menu, Teleport NPC, Spectate NPC, other native debug lists, and custom-item
wizard pickers no longer inherit each other's scrolling. New screens start at
the top; closing/reopening a screen or changing a cheat toggle restores its
previous position after the list rebuilds. Scroll positions are not saved to disk.

Debug lists are modal: world clicks, movement/hotkeys, and prefab placement input
are blocked while the main menu or a debug submenu is open. An invisible backdrop
intercepts clicks on UI underneath; the list, scrollbar, and X remain usable.
The closing click (including its release) cannot activate an object behind the
menu. Use X or the configured close-menu key to dismiss a list. These protections
do not change the game's input-lock counter or block the selected debug action.

Open the normal Silverpine debug options and use:

- Free Build
- Ignore property ownership
- No hammer required
- No materials required
- Instant construction
- Ignore placement zones
- God Mode
- Toggle Stat Changes
- NoGoldDecrease
- NoDurabilityDecrease
- Infinite Energy
- Block Negative Status Effects
- Allow NPC Talk In Combat
- Show NPC Property Boundaries
- View Whole World Tile
- Force NPC Daily Reset
- Duplicate Inventory Item
- Teleport NPC To Player
- Generate Custom Item (manual wizard)

The indented options are controlled by the Free Build master switch. Settings
are saved in `BepInEx/config/salt.silverpine.extradebug.cfg`.

God Mode prevents positive damage to the player and continues to allow healing.
Toggle Stat Changes prevents hunger and thirst from increasing and prevents
sanity and rest from decreasing. Beneficial stat changes remain enabled.
NoGoldDecrease prevents the player's gold total from falling regardless of
which game method attempts the deduction, while still allowing gold gains.
NoDurabilityDecrease preserves item durability, Infinite Energy prevents
energy loss, and Block Negative Status Effects rejects harmful player effects.
Allow NPC Talk In Combat bypasses the combat dialogue restriction. Force NPC
Daily Reset immediately runs the NPC daily relationship-cap update. Duplicate
Inventory Item opens a single-item player-inventory picker and creates a deep
copy that preserves the selected item's serialized quality, durability,
enchantments, and other instance data.

Show NPC Property Boundaries combines touching/overlapping plots belonging to
the same NPC into one outline and one owner label per connected property, with
a stable color per NPC. Internal strip lines are removed without filling gaps,
courtyards, or irregular corners. Disconnected areas (including corner-only
contacts) stay separate. Shared footprints receive inset colored outlines and
stacked owner labels. The overlay updates as plots or owners change; geometry
is cached between changes rather than recomputed every frame.
This is display-only: it does not merge the game's actual Plot objects or alter
ownership/build permissions, and it has no dependency on NamedNPCProperty.
The overlay is created on the gameplay player, not the temporary BepInEx
startup host, and is recreated for a replacement player after loading a save.
Its outlines and labels remain fixed to world coordinates as the player moves.
Owner labels are larger and maintain an approximately 24-pixel text height as
the camera zoom changes, including in the whole-tile view.

View Whole World Tile is a session-only ON/OFF camera toggle. It centers on the
current world tile and fits its 100x100 turf area with a small margin; dungeon
instances use their loaded turf bounds instead. The player is not teleported.
While active, the normal visibility mask and line-of-sight sprite hiding are
temporarily bypassed. Turning it off restores the prior camera pose relative
to its follow target, zoom, projection, pixel-perfect component, and FOV setting.
Save loading, camera teardown, and starting Spectate NPC also end the overview.
It does not discover or load neighboring world tiles or change NPC AI sight.

Teleport NPC To Player opens a named NPC picker. The selected NPC teleports to
an unobstructed tile immediately beside the player, stands still for three processed
game turns, then resumes their previous routine (including an existing
temporary/follower routine). Repeating the action restarts the three-turn
timer without stacking extra waits. Automatic routine overrides and movement
are blocked during the wait. No turn is spent merely selecting the action;
advance normal game turns to finish the wait. If all adjacent tiles are
blocked, the action reports that there is no safe landing spot. NPCs may share
the landing tile, so multiple summoned NPCs can stack together. Walls, water,
and other impassable obstacles still prevent landing.

The wait timer and previous routines use the game's own save format. This
feature has no dependency on the Acacia or follower plugins.

## Manual custom-item generator

As of v0.26.0, the native **Generate Custom Item** debug action opens the manual
wizard directly from SaltExtraDebug. Choose a name, category, base gameplay item,
independent appearance, description, rarity, and (where applicable) enchantment,
letter sender/content, and food expiration. This performs no inference/API calls.
The compatibility repair for older generated enchantment scrolls is included.

Existing generated items keep the same serialized component names and data.
A static runtime type-resolution handler lets the native game serializer find
components in loaded plugin assemblies. It survives destruction of the temporary
BepInEx startup host; no OnDestroy callback, generic serializer patch, or separate
preloader patcher is used. ModdingTools and CustomItemLoader are not required.
The separate CustomItemLoader pack editor is not part of this migration.

### Upgrading from the separate generator

Close the game and back up the old DLLs. Replace SaltExtraDebug.dll, then move
both of these legacy files outside BepInEx's active plugin/patcher folders:

- `BepInEx/plugins/CustomItemFreezeFix.dll`
- `BepInEx/patchers/CustomItemPromptPatcher.dll`

Do not leave the old patcher installed: it still injects calls to the old plugin
assembly. Keep the backed-up DLLs together if rolling back. Saves and settings
do not need to be edited. Check existing items and test creation/save/reload on
a separate save before continuing normal play.

## Build

Open `SaltExtraDebug.csproj` in Visual Studio and build the Debug or Release
configuration. Copy `bin/Debug/netstandard2.1/SaltExtraDebug.dll` (or the
Release equivalent) into `BepInEx/plugins`.

Do not load `debugtest.dll` or `FreeBuild.dll` at the same time, because their
patches overlap with this plugin.

## Credits

Created by **Saelac and ChatGPT**.
