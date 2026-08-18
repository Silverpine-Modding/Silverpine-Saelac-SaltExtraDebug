# Salt Extra Debug

A BepInEx 5 plugin for Silverpine that enables the game's debug menu and adds
persistent runtime toggles for Free Build.

## Debug menu

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

The indented options are controlled by the Free Build master switch. Settings
are saved in `BepInEx/config/salt.silverpine.extradebug.cfg`.

God Mode prevents positive damage to the player and continues to allow healing.
Toggle Stat Changes prevents hunger and thirst from increasing and prevents
sanity and rest from decreasing. Beneficial stat changes remain enabled.
NoGoldDecrease prevents the player's gold total from falling regardless of
which game method attempts the deduction, while still allowing gold gains.

## Build

Open `SaltExtraDebug.csproj` in Visual Studio and build the Debug or Release
configuration. Copy `bin/Debug/netstandard2.1/SaltExtraDebug.dll` (or the
Release equivalent) into `BepInEx/plugins`.

Do not load `debugtest.dll` or `FreeBuild.dll` at the same time, because their
patches overlap with this plugin.

## Credits

Created by **Saelac and ChatGPT**.
