# Runtime regression tests

From the game workspace root, run:

```powershell
dotnet run --project 'Plugin Development\SaltExtraDebug\Tests\SaltExtraDebug.Tests.csproj' -c Release
dotnet run --project 'Plugin Development\SaltExtraDebug\Tests\Camera\Camera.Tests.csproj' -c Release
```

The engine-free harness compiles the actual plugin routine/patch source and
the decompiled game's `RoutineArgument_EndOverrideIf_AfterTurns` unchanged.
The surrounding Unity/game lifecycle and integer storage are test doubles.
It checks the three-turn duration, repeat teleports, nested/follower routine
preservation, timer serialization, stale completion, fallback recovery, and
the scope of the movement/override patches. The overlay lifecycle checks
compile the production Player.Start patch and component-ownership helper with
fake hosts, covering bootstrap independence, duplicate prevention, recovery,
and replacement players. They do not validate Harmony
installation, real save-file compatibility, landing collisions, or UI rendering.

The same harness compiles the production property-union geometry helper without
Unity. It checks exact directed boundary edges, connected components/labels,
irregular outlines, overlapping/duplicate plots, holes, shared-footprint insets,
empty plots, negative/large coordinates, all 512 possible 3x3 tile arrangements,
and 300 deterministic randomized rectangle sets. This is 821 exact-union
scenarios, plus targeted compression/inset checks; it does not render Unity UI.

The camera suite compiles the production overview and view-math files with a
small engine model. It checks whole-tile fitting, portrait/landscape resizing,
screen-size label math, snapshot restoration, pixel-perfect suspension,
visibility scope, repeat toggles, save-load cleanup, camera-target changes,
and replacement players. Actual rendering and Unity destroyed-object behavior
still require the in-game smoke tests below.

In-game smoke test:

1. Press O, select **Teleport NPC To Player**, and choose an NPC. Check the
   picker Back/X controls and that teleporting itself spends no game turn.
2. Advance two turns: the NPC should stay still. After the third, the previous
   activity should resume; movement towards its destination can start next turn.
3. Repeat a teleport during the wait: the NPC should wait three new turns, not
   resume an older wait afterward. Test an NPC with an existing follower routine.
4. Summon multiple NPCs without advancing turns: they may share the same tile.
   Walls/water/other obstacles should not be chosen as a landing tile.
5. Save after one waiting turn, reload, and check that two further turns resume
   the previous routine. Other NPCs and the player should move normally.
6. Enable property boundaries, reload a save, and verify the colored outlines
   and labels appear without toggling again. Walk around: neither outlines nor
   labels should move with the player. Turn the overlay off/on to check cleanup.
7. Enable **View Whole World Tile**: check that the entire tile fits and the
   player has not moved. NPC property labels should remain readable at this zoom.
   Turn it off: camera follow, zoom and prior FOV setting should return.
8. Repeat with Toggle FOV already ON, resize the window, change world tiles,
   and try a dungeon. Try Spectate NPC while overview is active, then load a
   save. Neither path should leave a stuck zoom or detached camera.
9. Check an NPC property made of horizontal strips: it should show one outline
   and one name, including around any stepped corners. Courtyard holes should
   retain their inner border; separate islands should each have a label. Check
   a shared property for the different NPC colors and stacked labels. Change
   property ownership/extent and confirm the overlay updates without reloading.
