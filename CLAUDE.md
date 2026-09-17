# Chrono Save (rimworld-chrono-save)

A second autosave system driven by real-world time instead of game time. A `GameComponent` polls
`Time.realtimeSinceStartup` every frame and, once the configured interval elapses (default 5 min, range
1-60), queues a vanilla save named `Chronosave-N`, rotating N from 1 to a configured maximum (default 10,
range 1-25). Running on the frame update rather than the tick means it keeps saving while the game is
paused, which is the mod's selling point and the source of both live user reports. Workshop 3549864494,
598 subscribers, `<modVersion>1.0.1</modVersion>`, last file update 22 Aug 2025, 422 LOC.

## Architecture

| File | Type | Role |
|---|---|---|
| `1.6/ModEntry.cs` | `ChronoSaveMod : Mod` | Holds the static `Settings`, calls `harmony.PatchAll()` with id `com.zei33.chronosave`, delegates the settings window. 55 LOC. |
| `1.6/Core/ChronoSaveGameComponent.cs` | `ChronoSaveGameComponent : GameComponent` | All scheduling and saving. 207 LOC. |
| `1.6/Core/ChronoSaveSettings.cs` | `ChronoSaveSettings : ModSettings` | Three settings plus the IMGUI page. 118 LOC. |
| `1.6/Core/ChronoSaveSchedule.cs` | static | The scheduling arithmetic, extracted 2026-09-17 so it can be tested without the game. No game state. |
| `Tests/` | NUnit, net472 | Not in the sln, excluded from the mod's compile items. See `Tests/README.md`. |
| `1.6/Patches/GameComponentInjectionPatch.cs` | Harmony postfix on `Verse.Game.FillComponents` | Unreachable, see trap 9. 42 LOC. |
| `1.6/Languages/*/Keyed/ChronoSave_Keys.xml` | keyed strings | 11 keys, nine languages, key sets verified identical. |

Per frame: `Root_Play.Update` -> `Current.Game.UpdatePlay()` -> `GameComponentUtility.GameComponentUpdate()`
-> `ChronoSaveGameComponent.GameComponentUpdate()` (`:80`). `Root_Entry.Update` reaches the same method
via `Current.Game.UpdateEntry()`, which matters (trap 1). Save path: `PerformChronoSave` (`:110`) ->
`GetNextChronoSaveName` (`:156`) -> `QueueLongEvent(closure, "ChronoSave_SavingMessage", doAsynchronously:
false, null)`; `lastSaveRealTime` and `currentSaveIndex` update synchronously at `:138-143`, and the
closure calls `GameDataSaveLoader.SaveGame` then `Messages.Message` one or two frames later.

The whole fragile surface is one Harmony patch, a postfix on the private `Verse.Game.FillComponents()`,
and it does nothing (trap 9). No Defs, no XML patches, no `LoadFolders.xml`, no `DefOf` of its own.

## Invariants and traps

Verified against the source and RimWorld 1.6.4871 (`ilspycmd` over `Assembly-CSharp.dll`) on 2026-09-17.
Game members are cited by name, not by decompiled line, because a single-type `ilspycmd -t` and a
project decompile number the same file differently.

1. The component runs on the pre-game entry screens. `Root_Entry.Update` calls `Current.Game.UpdateEntry()`,
   and the body of `Verse.Game.UpdateEntry` is just `GameComponentUtility.GameComponentUpdate()`. Once world
   generation finishes, `Find.World`, `Find.WorldInterface` and `Current.Game` are all non-null, so the
   guard at `:85` passes on `Page_SelectStartingSite` and `Page_ConfigureStartingPawns`. Any new guard must
   lead with `Current.ProgramState != ProgramState.Playing`, which is what vanilla's ESC-menu Save uses.
2. `Find.Targeter` throws on the entry screen: it is `((UIRoot_Play)Find.UIRoot).mapUI.targeter`, and there
   `Find.UIRoot` is a `UIRoot_Entry`. Guard order is load bearing: `ProgramState` first, targeter after.
3. `GameDataSaveLoader.SavingIsTemporarilyDisabled` covers exactly three things: `Find.TilePicker.Active`,
   `Find.WindowStack.WindowsPreventSave` and `WorldComponent_GravshipController.CutsceneInProgress`. Not
   `Find.Targeter`, `Find.WorldTargeter` or `Find.WorldRoutePlanner`, not float menus, not pause. Only
   `Dialog_ChooseThingsForNewColony` and `Dialog_ConfigureIdeo` set `preventSave`, and
   `Page_SelectStartingSite` sets `absorbInputAroundWindow = false` and never sets it.
4. Vanilla needs no more than that because `Autosaver.AutosaverTick()` is reached only from
   `TickManager.DoSingleTick()`, which does not run while paused. This mod removes that invariant
   deliberately and does not replace it.
5. Rotation is a serialised counter, not oldest-file-first. `currentSaveIndex` goes through `ExposeData`
   (`:192`), so loading `Chronosave-4` resumes at slot 5 and overwrites the newest snapshots, and a new
   colony starts at 1 and walks over the previous colony's whole ring. Vanilla's
   `Autosaver.NewAutosaveFileName()` takes an unused name then `MinBy(LastWriteTime)`; this never stats the disk.
6. Shipped copy contradicts trap 5. `About/About.xml:23`, `README.md:18`, `Documentation/Features.md:14`,
   the key `ChronoSave_NumberOfSavesTooltip` and the equivalent line in all nine `Workshop/*.md` files
   claim the oldest save is overwritten. Fix the code or fix the copy, but do not leave them disagreeing.
7. The success toast lies. `GameDataSaveLoader.SaveGame` returns `void` and swallows everything into
   `Log.Error`, so `Messages.Message` at `:134` runs even when the save threw. The `Log.Message` at `:145`
   is worse: it runs before the queued event has executed at all.
8. That toast is `historical` (the bound overload defaults it true). `Archive.MaxNonPinnedArchivables` is
   200 and culls oldest-first, and the archive is deep-serialised into every save, so twelve saves an hour
   evicts real letters from the history tab within about sixteen hours. Pass `historical: false`.
9. The Harmony patch is unreachable dead code. `Verse.Game.FillComponents` already constructs every
   non-abstract `GameComponent` subclass with `Activator.CreateInstance(type, this)`, and
   `GenTypes.AllActiveAssemblies` includes mod assemblies, so the postfix's `GetComponent<...>() != null`
   guard always returns early. `harmony.PatchAll()` and the `brrainz.harmony` hard dependency buy nothing.
10. Permadeath is not respected. Nothing consults `Find.GameInfo.permadeathMode`; vanilla's
    `Autosaver.DoAutosave()` writes `Current.Game.Info.permadeathModeUniqueName` instead of a rotating
    name, so a Commitment colony silently gains up to 25 rollback points. `SaveGame` also passes
    `leaveOldFile: permadeathMode` to `SafeSaver.Save`, leaving a `Chronosave-N.rws.old` per slot until
    that slot is next written. `Features.md` claims the opposite twice.
11. Corrupt writes are not a risk. `SafeSaver.Save` writes `.new`, moves the live file to `.old`, swaps,
    restores from `.old` if the swap fails, then pops `GenUI.ErrorDialog("ProblemSavingFile")`. The mod
    inherits that by routing through the vanilla entry point.
12. Two of the three `Current.Game == null` checks are dead: `GameComponentUtility.GameComponentUpdate()`
    opens with `Current.Game.components`, so `:85` and `:116` cannot fire. The one in the closure (`:128`)
    is real, but a null check does not cover Game *replacement*: load another colony in that one-frame
    window and the closure writes the new colony under the old captured `saveName`.
13. The save is synchronous (`doAsynchronously: false` routes to `UpdateCurrentSynchronousEvent` on the
    main thread, as vanilla's does). No timer thread, no cross-thread state. Only the docs call it async.
14. A failed guard defers the save, it does not cancel it: `GameComponentUpdate` returns before touching
    `lastSaveRealTime`, so the interval condition stays true and the save fires on the first frame the
    guard clears. That is already what Huehuecoyotl asked for. Preserve it when adding guards.
15. `GetNextChronoSaveName` reads as if it searches for a free slot. It does not; it returns on the first
    iteration for any `NumberOfSaves >= 1`, and the real wrap is the separate increment at `:139-143`.
    Change the rotation policy in both places or neither.
16. Cosmetics worth knowing: `ChronoSaveSettings.cs:68-78` never writes the clamp back, so `70` displays
    with 60 in effect and clearing the field refills it instantly; `TipRegion(listing.GetRect(0f), ...)` at
    `:85` binds a tooltip to a zero-height rect, translated nine times and never shown; `ModEntry.cs:37`
    logs "Loaded version 1.0"; `ChronoSaveGameComponent.cs:145` logs per save outside `Prefs.DevMode`.

## Defect register

| Sev | Defect | file:line | What breaks |
|---|---|---|---|
| critical | Saves fire on the pre-game entry screens | `ChronoSaveGameComponent.cs:85` | `Game.ExposeData` -> `Find.CameraDriver.Expose()` NREs (the entry scene nulls `cameraDriverInt`): red error plus a modal ProblemSavingFile dialog every interval, slot burned, success toast still posted |
| high | No guard for targeting, float menus or paused interactions | `:91` | Save captures a half-finished interaction; neither `Targeter` nor `WorldTargeter` is serialised, so the pending callback cannot be restored |
| high | Rotation is a serialised counter, not oldest-first | `:156`, `:192` | A new colony destroys the previous colony's ring; loading an old chronosave overwrites the newest ones |
| medium | Success message posted even when the save threw | `:133` | Player believes a recovery point exists that does not |
| medium | Toast is `historical` | `:134` | Archive evicts real gameplay history and bloats every save |
| medium | No permadeath handling, one `.rws.old` per slot there | `:121` | Commitment mode silently gains rollback points |
| medium | Name collision with user saves, orphaned slots when the max is lowered | `:161` | A user file named `Chronosave-3` is overwritten; slots above the new max sit on disk forever |
| medium | Harmony patch unreachable, dependency unnecessary | `GameComponentInjectionPatch.cs:20` | No runtime failure; a dependency prompt and patch surface for nothing |

Full evidence, plus five low-severity entries, is in
`/Users/matthewscott/Programming/rimworld/docs/recon/2026-09-17-recon-dossier.md` under `# rimworld-chrono-save`.

## Open user reports

Use the `workshop-feedback` skill to pull comments. The author's last reply on any mod was 23 Sep 2025.

- Huehuecoyotl, 4 Aug 2026, unanswered. Wants a setting to stop saving while paused, ideally saving on
  resume instead, because saving mid-pause breaks shuttle landing-site picker mods.
  Actually wrong: `SavingIsTemporarilyDisabled` does not cover `Find.Targeter` or `Find.WorldTargeter`,
  and no pause-related setting exists.
- DeleteC, 16 Aug 2025. Red errors loop while choosing a landing site on a new game. Zei replied "已解决"
  and the null checks (d84acfc, 30b4f6c) shipped before the 22 Aug 2025 update.
  Actually wrong: still open. Those checks do not gate on `ProgramState`, and vanilla's
  `TilePicker.Active` does not cover `Page_SelectStartingSite`. The dossier's triage pass marked this
  closed; the adversarial critic overturned it after re-verifying against the decompiled game.
- Tyrant, 16 Aug 2025. Asks for save compression support. Answered ("this mod uses the vanilla saving
  mechanism, so it should be compatible"), never tested. The inference is sound: everything routes
  through `GameDataSaveLoader.SaveGame`.
- Unanswered praise, so nobody re-reads it as a bug: Jaggid Edje (5 Jul 2026), Quod Volo (9 Sep 2025),
  hazelsteacup (1 Sep 2025).

## Build and test

```
cd /Users/matthewscott/Programming/rimworld/rimworld-chrono-save
export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api
dotnet build rimworld-chrono-save.sln -c Release   # clean, zero warnings
```

- Clean as of 2026-09-17. The MSB3245 warning for the bare `UnityEngine` `<Reference>` is gone:
  `ChronoSave.csproj` now carries a real HintPath, so a new warning is a real regression.
- `./build.sh` builds Release then deletes and replaces `$RimWorldDir/Mods/ChronoSave`. Destructive, so
  never run it to check something.
- Tests: `dotnet test Tests/ChronoSave.Tests.csproj`, 18 passing as of 2026-09-17, against the real
  `Assembly-CSharp.dll`. Deliberately outside `rimworld-chrono-save.sln` so the solution build stays
  mod-only and warning-free, and `Compile Remove="Tests/**"` in `ChronoSave.csproj` keeps the test
  sources out of the shipped DLL. `Tests/README.md` has the detail.
- The seam is `ChronoSaveSchedule`: `IsDue`, `SaveNameForSlot`, `SlotInRange`, `AdvanceSlot` and
  `SanitiseLoadedSlot`, all pure. The extraction preserved behaviour exactly, including the mutation
  `GetNextChronoSaveName` performs on `currentSaveIndex`, so trap 15 still holds and the rotation
  policy still lives in two places. `chrono-save#4` and `#1` land here.
- Still out of reach, and not a harness defect: anything reading the static `ChronoSaveMod.Settings`
  (`:33`), anything reading `Time.realtimeSinceStartup`, all of `GameComponentUpdate`, and every
  Harmony patch, because Harmony cannot patch on this runtime at all. Quote coverage against
  `ChronoSaveSchedule`, never the repo.
- In-game: 1.6.4871 only. Set the interval to 1 minute to exercise a rotation quickly. To reproduce the
  entry-screen defect, start a new colony, let world generation finish, then sit on the landing-site page
  past the interval. Use the `refsrc` skill for game API lookups.

## Repo conventions that differ from the workspace defaults

- Everything new is C# plus keyed strings. Adding the pause setting is three or four keys times nine files.
- No longer a difference, as of 2026-09-17: `.gitignore` excludes `1.6/Assemblies/net472/` outright,
  `ChronoSave.dll`, `.pdb` and the two game-owned DLLs came out of the index, and `build.sh` deletes
  everything in the staged output bar `ChronoSave.dll`. The published build is the part that is still
  wrong. The 22 Aug 2025 Workshop file carries `ISharpZipLib.dll` (byte-identical to the game's copy)
  and `com.rlabrecque.steamworks.net.dll` (a different build), which RimWorld loads as mod assemblies
  for all 598 subscribers, so that alone is a reason to upload again.
- `About/About.xml` declares a hard `brrainz.harmony` dependency the code does not need, and carries no
  `<url>`, so GPLv3 binaries ship with no source pointer.
- `Workshop/*.md` holds the nine store descriptions, but `Verse.Steam.Workshop.SetWorkshopItemDataFrom`
  calls `SteamUGC.SetItemDescription` only inside `if (creating)`, so the in-game uploader cannot
  republish them. Changing store copy needs the Steam website, which is region-blocked from this machine.
  See the `ship-mod` skill before publishing.
