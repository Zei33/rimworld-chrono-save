# Chrono Save (rimworld-chrono-save)

> **All eight GitHub issues are closed as of 2026-09-17, and the code is on `main` but not released.**
> 598 subscribers still run the August 2025 build, so every defect below marked fixed is still live
> for them until slice 8 ships. `<modVersion>` is 1.1.0 in the repo and 1.0.1 is live. Before that
> upload, check two things in a running game, because nothing in this tree can. One, the Commitment rename flow end to
> end: the button appears, the dialog opens, RimWorld autosaves under the new name, the slot file is
> removed, the notice disappears. Two, that exactly one chronosave is written per interval at a
> one-minute setting. The change note must mention that the `brrainz.harmony` dependency is gone.


A second autosave system driven by real-world time instead of game time. A `GameComponent` polls
`Time.realtimeSinceStartup` every frame and, once the configured interval elapses (default 5 min, range
1-60), queues a vanilla save. The filename is chosen from the saves folder at the moment of writing:
`Chronosave-<Colony>-N` once the colony has a name, `Chronosave-N` before that, taking the first
unused slot or else the one written longest ago. Running on the frame update rather than the tick
means it keeps saving while the game is paused, which is the mod's selling point and the source of
both live user reports. Workshop 3549864494, 598 subscribers, `<modVersion>1.1.0</modVersion>` in the
repo against 1.0.1 live, last file update 22 Aug 2025.

## Architecture

| File | Type | Role |
|---|---|---|
| `1.6/ModEntry.cs` | `ChronoSaveMod : Mod` | Holds the static `Settings`, calls `harmony.PatchAll()` with id `com.zei33.chronosave`, delegates the settings window. 55 LOC. |
| `1.6/Core/ChronoSaveGameComponent.cs` | `ChronoSaveGameComponent : GameComponent` | All scheduling and saving. 207 LOC. |
| `1.6/Core/ChronoSaveSettings.cs` | `ChronoSaveSettings : ModSettings` | Three settings plus the IMGUI page, the Commitment notices and the leftover-backup report. |
| `1.6/Core/ChronoSaveSchedule.cs` | static | Every scheduling decision that can be stated over plain values, so it can be tested without the game. No game state. |
| `1.6/Core/ChronoSaveFiles.cs` | static | The two filesystem questions, over a plain path: list the saves folder, measure a written file. Never calls `GenFilePaths`, which is what makes it testable. |
| `1.6/Core/ChronoSaveOutcome.cs` | enum + struct | What a finished attempt did, and what follows from it. |
| `1.6/Core/ChronoSaveConditions.cs` | enum + struct | One frame's readings of the game state a save depends on, and the reasons it can be held back. |
| `1.6/Core/SaveFileStamp.cs` | struct | A save file as the rotation sees it: base name and `LastWriteTimeUtc`. |
| `1.6/Core/StrandedBackups.cs` | static + struct | Counts the `.rws.old` copies Commitment writes left behind. Reports, never deletes. |
| `1.6/Core/Dialog_RenameColony.cs` | `Dialog_GiveName` | Vanilla's faction naming dialog, opened from the settings window so a rebound Commitment colony can be moved out of a slot. |
| `Tests/` | NUnit, net472 | Not in the sln, excluded from the mod's compile items. See `Tests/README.md`. |
| `1.6/Languages/*/Keyed/ChronoSave_Keys.xml` | keyed strings | 21 keys, nine languages, key sets verified identical. |

Per frame: `Root_Play.Update` -> `Current.Game.UpdatePlay()` -> `GameComponentUtility.GameComponentUpdate()`
-> `ChronoSaveGameComponent.GameComponentUpdate()` (`:80`). `Root_Entry.Update` reaches the same method
via `Current.Game.UpdateEntry()`, which matters (trap 1). Save path: `PerformChronoSave` (`:110`) ->
`GetNextChronoSaveName` -> `QueueLongEvent(closure, "ChronoSave_SavingMessage", doAsynchronously:
false, null)`. Since #4, **nothing outside the closure reports anything**: the closure re-checks its
guards, calls `GameDataSaveLoader.SaveGame`, measures the written file, and only then reaches
`ApplyOutcome`, which is the sole writer of `lastSaveRealTime`, `currentSaveIndex`, the toast and the
log line. `savePending` latches across the queue window; see trap 16 for why it has to.

**There is no fragile surface at all as of #8.** No Harmony patches, no Harmony dependency, no Defs,
no XML patches, no `LoadFolders.xml`, no `DefOf`. The mod is one `GameComponent` that vanilla
constructs by itself, one `ModSettings`, and a pure decision layer beside them.

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
   `Page_SelectStartingSite` sets `absorbInputAroundWindow = false` and never sets it. Since #3 the
   mod supplements it with its own readings rather than relying on it; see `ChronoSaveConditions`.

   **Three different screens are called "the landing site" and they are not the same defect.** The
   new-game one is `Page_SelectStartingSite`, a `Page` in `ProgramState.Entry`, covered by the
   `ProgramState` gate. The gravship or move-colony one is `TilePicker`, covered by
   `SavingIsTemporarilyDisabled`. The shuttle or transport-pod destination pick is `WorldTargeter`
   (`CompLaunchable.StartChoosingDestination` ends in `Find.WorldTargeter.BeginTargeting`) and was
   covered by nothing at all. Conflating the first and the third is what would have closed #3 as
   already fixed.
4. Vanilla needs no more than that because `Autosaver.AutosaverTick()` is reached only from
   `TickManager.DoSingleTick()`, which does not run while paused. This mod removes that invariant
   deliberately and does not replace it.
5. **Fixed 2026-09-17 (#1).** Rotation used to be a serialised counter. `currentSaveIndex` went
   through `ExposeData`, and `Game.ExposeSmallComponents` deep-scribes `components`, so the slot was
   written into **every** save the game produced while the mod was active, manual saves and vanilla
   autosaves included. Loading any of them rewound the ring and the mod overwrote forward over newer
   chronosaves; and because names were globally flat, a new colony started at slot 1 and walked over
   the previous colony's whole set. It is now `Autosaver.NewAutosaveFileName`'s algorithm over a
   listing of the saves folder, taken inside the queued closure: first unused slot, else oldest by
   `LastWriteTimeUtc`, ties to the lowest slot. **Never put rotation state back on the component**;
   `HarnessTests.TheRotationSlotIsNotAFieldAndSoCannotBeScribed` asserts there is no `int` instance
   field at all.
6. Resolved 2026-09-17 in the true direction. `About/About.xml:23`, `README.md:18`,
   `Documentation/Features.md:14`, the key `ChronoSave_NumberOfSavesTooltip` and the equivalent line
   in all nine `Workshop/*.md` files all claimed the oldest save is overwritten, which was false.
   Trap 5's fix made it true rather than the copy being cut back. The two keys now also say that each
   named colony keeps its own set. The `Workshop/*.md` copy still needs the per-colony sentence, and
   the in-game uploader cannot republish a description, so that is website work; see `ship-mod`.
7. **Fixed 2026-09-17 (#4).** The success toast used to lie: `GameDataSaveLoader.SaveGame` returns
   `void` and swallows everything into `Log.Error`, and the `Log.Message` ran before the queued event
   had executed at all. The toast, the log line, the timer and the slot advance now all live in
   `ApplyOutcome`, which only the queued closure reaches, after the write has been attempted and the
   written file measured. Keep them there. Swallowing goes deeper than `SaveGame`: `Scribe_Deep.Look`
   catches anything short of an `OutOfMemoryException` thrown inside `ExposeData` and carries on, so
   a save that fails part way still reaches `SafeSaver.FinalizeSaving` and commits a well formed but
   truncated document. **The absence of an exception is not evidence of success**; the size of what
   landed is the only in-process signal there is.
8. **Fixed 2026-09-17 (#5).** The toast was `historical` (the bound overload defaults it true).
   `Archive.MaxNonPinnedArchivables` is 200 and culls oldest-first, and the archive is
   deep-serialised into every save, so twelve saves an hour evicted real letters from the history
   tab within about sixteen hours. Both toasts now pass `historical: false` explicitly. Any new
   `Messages.Message` in this mod must do the same; the default is the trap.
9. **Removed 2026-09-17 (#8). This mod now has no Harmony patches and no Harmony dependency.** The
   one patch was unreachable dead code: `Verse.Game.FillComponents` already constructs every
   non-abstract `GameComponent` subclass with `Activator.CreateInstance(type, this)`, and
   `AllSubclassesNonAbstract` filters `GenTypes.AllTypes`, which covers every loaded mod assembly
   through `AllActiveAssemblies`, so the postfix's `GetComponent<...>() != null` guard returned early
   every time and its log line had never appeared in anybody's log.

   The patch, `PatchAll()`, the `0Harmony` references in both `.csproj` files, the vendored
   `1.6/Libraries/0Harmony.dll` and the `brrainz.harmony` entry in `About.xml` all went in one
   commit, which is the only safe order: dropping the patch alone leaves the install prompt, and
   dropping the dependency while `ModEntry` still calls `PatchAll()` leaves a hard runtime dependency
   on a library that may then be absent. **If a patch is ever needed again, all six come back
   together.**
10. **Fixed 2026-09-17 (#2).** Nothing consulted `permadeathMode`, so a Commitment colony silently
    gained up to 25 rollback points in the one mode whose purpose is that you cannot roll back. There
    is now a gate in `GameComponentUpdate` and a second check inside the queued closure, because the
    identity check there only rules out the `Game` being *replaced*, not the same game turning out to
    be a Commitment one. Three findings from that work are worth keeping:

    - **Vanilla 1.6 has no player-facing rename.** `Faction.OfPlayer.Name` has exactly one writer,
      `NamePlayerFactionDialogUtility.Named`, reached from the one-time prompt (permanently gated on
      `!HasName`) and a dev-mode action. So "tell the player to rename their colony" was unfollowable
      advice, and `Dialog_RenameColony` opens vanilla's own dialog instead. The mod writes and deletes
      nothing itself on that path; `Named` does the autosave and the old-file delete.
    - **`FinalizeInit` cannot detect a first-load rebind.**
      `SavedGameLoaderNow.LoadGameFromSaveFileNow` calls
      `CheckUpdatePermadeathModeUniqueNameOnGameLoad` *after* `LoadGame()` returns, so after both
      `FinalizeInit` and `LoadedGame`. A check there reads the pre-rebind name and is one session
      late every time. It runs on the first `Playing` frame instead, which is guaranteed to be after
      the rebind because loading is an asynchronous long event and `Root_Play.Update` returns before
      `UpdatePlay` while one is running.
    - **The English save suffix is `(Permadeath)`, not `Commitment`.** The storyteller screen says
      "Commitment mode" (`CommitmentMode`) and the filename says `(Permadeath)`
      (`PermadeathModeSaveSuffix`). Player-facing text uses the first; the second matters only
      because it is why `IsRingSaveName` must be an exact-shape parser rather than a `StartsWith`: a
      colony legitimately named `Chronosave-3` has the unique name `Chronosave-3 (Permadeath)`.

    The stranded `Chronosave-N.rws.old` files are counted in the settings window and **never
    deleted**. `SaveGame` passes `leaveOldFile: permadeathMode`, so each Commitment write left one,
    and `SafeSaver` only clears it on the next write to that path, which will now never come. They
    cannot be attributed safely, because the same filename shape is vanilla's own safety copy.
11. Corrupt writes are not a risk. `SafeSaver.Save` writes `.new`, moves the live file to `.old`, swaps,
    restores from `.old` if the swap fails, then pops `GenUI.ErrorDialog("ProblemSavingFile")`. The mod
    inherits that by routing through the vanilla entry point.
12. Two of the three `Current.Game == null` checks are dead: `GameComponentUtility.GameComponentUpdate()`
    opens with `Current.Game.components`, so `:85` and `:116` cannot fire. The one in the closure was
    real but insufficient, because a null check does not cover Game *replacement*: load another colony
    in that window and the closure writes the new colony under the old captured `saveName`.
    **Fixed 2026-09-17 (#4)** by capturing the `Game` at queue time and comparing with
    `ReferenceEquals`, not by null-checking. One residual hazard is knowingly left open: if
    `Game.Dispose()` has run but `Current.Game` still points at the same object, the identity check
    passes and a disposed `Game` is serialised. The managed graph survives `Dispose`, so it probably
    writes something wrong rather than throwing. Size verification gives partial cover, holding the
    slot and warning rather than reporting success.
13. The save is synchronous (`doAsynchronously: false` routes to `UpdateCurrentSynchronousEvent` on the
    main thread, as vanilla's does). No timer thread, no cross-thread state. Only the docs call it async.
14. A failed guard defers the save, it does not cancel it: `GameComponentUpdate` returns before touching
    `lastSaveRealTime`, so the interval condition stays true and the save fires on the first frame the
    guard clears. That is already what Huehuecoyotl asked for. Preserve it when adding guards.
15. Gone as of #1, and the shape is worth remembering. `GetNextChronoSaveName` read as if it searched
    for a free slot and did not: it returned on its first iteration for any `NumberOfSaves >= 1`, and
    the real wrap was a separate increment elsewhere, so the policy lived in two places that could
    disagree. There is now one place, `ChooseSaveName`, and it runs inside the queued closure so the
    faction, the folder listing and the write all see the same `Game`.
16. **A queued long event does not pause the game.** This is the least obvious thing in the file and
    it decides the shape of the save path. `LongEventHandler.ShouldWaitForEvent` returns **false**
    while the current event uses the standard window, and `UseStandardWindow` is
    `canEverUseStandardWindow && !doAsynchronously && eventActionEnumerator == null`, which this
    mod's call satisfies. So `Root_Play.Update` keeps calling `UpdatePlay`, `GameComponentUpdate`
    keeps firing in the frames between queueing a save and the save running, and the interval
    condition is still true in those frames. #4 moved the timer reset into the closure, which made
    that a live duplicate-save hazard rather than a curiosity.

    It is handled by the `LongEventPending` reading in the condition set, not by a latch of its own.
    A short-lived `savePending` flag was written for #4 and deleted again in #3, because
    `LongEventHandler.AnyEventNowOrWaiting` covers strictly more (any long event, not just this
    mod's) and needs no watchdog: if an event is dropped, `GenScene.GoToMainMenu` being the vanilla
    path that does it, the reading simply goes false and the timer was never written, so the save
    re-queues on the next clear frame. **Do not add a second mechanism for this.** One invariant, one
    place, which is also why the closure's own re-check uses `IgnoringOwnLongEvent()` rather than
    skipping the reading: `UpdateCurrentSynchronousEvent` invokes the action and clears
    `currentEvent` afterwards, so while the closure runs it is still looking at itself.
17. **Fixed 2026-09-17 (#6).** The interval field never wrote its clamp back, so `70` displayed with
    60 in effect, and it refilled the instant it was cleared so the player could not select all and
    retype. It is now `Widgets.TextFieldNumeric`, which keeps the buffer and the value apart:
    `IsPartiallyOrFullyTypedNumber` returns true for the empty string so the buffer is accepted,
    `IsFullyTypedNumber` returns false for it so nothing refills the field, and a fully typed edit is
    clamped and written back. The `buffer == null` refill fires on null only, never on empty. The
    remaining edge, a window closed on an empty field, is settled in `WriteSettings`, which
    `Dialog_ModSettings.PreClose` calls on every close.

    The slider tooltip bound to `listing.GetRect(0f)`, a zero-height rect that can never be hovered,
    so nine translations of it had never been seen by anyone. `Listing_Standard.Slider` returns the
    value rather than the rect, which is why; the rect is now taken first, which is that method
    inlined, so the layout does not move. **Attaching it made the text visible for the first time**,
    which is why #1's rewording of `ChronoSave_NumberOfSavesTooltip` had to land first: its claim
    that the oldest save is overwritten was false until then.

    Still outstanding, for #7 or #8: `ModEntry.cs` logs "Loaded version 1.0" against a `<modVersion>`
    of 1.0.1. The per-save `Log.Message` is gated on `Prefs.DevMode` as of #4.

## Defect register

| Sev | Defect | file:line | What breaks |
|---|---|---|---|
| critical | Saves fire on the pre-game entry screens | `ChronoSaveGameComponent.cs:85` | `Game.ExposeData` -> `Find.CameraDriver.Expose()` NREs (the entry scene nulls `cameraDriverInt`): red error plus a modal ProblemSavingFile dialog every interval, slot burned, success toast still posted |
| ~~high~~ | ~~Rotation is a serialised counter, not oldest-first~~ | fixed 2026-09-17, #1 | The slot is derived from the saves folder at write time and the ring is scoped per named colony |
| ~~high~~ | ~~No guard for targeting, float menus or paused interactions~~ | fixed 2026-09-17, #3 | `ChronoSaveConditions` plus `FirstBlocker`, taken again inside the queued closure |
| ~~medium~~ | ~~Success message posted even when the save threw~~ | fixed 2026-09-17, #4 | Reporting now happens inside the queued closure, after the write, and after the written file has been measured |
| ~~medium~~ | ~~Toast is `historical`~~ | fixed 2026-09-17, #5 | Both toasts pass `historical: false` |
| ~~medium~~ | ~~No permadeath handling, one `.rws.old` per slot there~~ | fixed 2026-09-17, #2 | Gated, disclosed, and affected colonies get a letter plus a rename button |
| medium | Name collision with user saves, orphaned slots when the max is lowered | `ChooseSaveName` | A user file named `Chronosave-3` is still overwritten. Slots above a lowered max no longer rotate but do sit on disk; so do a named colony's files after it is abandoned. Both are visible in the Load list with its own delete button, which is the argument for leaving them |
| ~~medium~~ | ~~Harmony patch unreachable, dependency unnecessary~~ | fixed 2026-09-17, #8 | Patch, `PatchAll()`, both `0Harmony` references, the vendored DLL and the `About.xml` dependency all removed together |

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
- Tests: `dotnet test Tests/ChronoSave.Tests.csproj`, 74 passing as of 2026-09-17, against the real
  `Assembly-CSharp.dll`. Deliberately outside `rimworld-chrono-save.sln` so the solution build stays
  mod-only and warning-free, and `Compile Remove="Tests/**"` in `ChronoSave.csproj` keeps the test
  sources out of the shipped DLL. `Tests/README.md` has the detail.
- The seam is `ChronoSaveSchedule` plus `ChronoSaveFiles`. Everything in the first is pure;
  everything in the second takes a plain path and is exercised against a temporary directory, which
  works because `System.IO` is the one part of the game's surface that is fully reachable here.
- Still out of reach, and not a harness defect: anything reading the static `ChronoSaveMod.Settings`,
  anything reading `Time.realtimeSinceStartup`, and the game-facing half of `GameComponentUpdate`.
  Quote coverage against `ChronoSaveSchedule`, `ChronoSaveFiles` and `StrandedBackups`, never the repo.
- **Two behaviours have no automated coverage and must be checked in game.** The `savePending` latch
  of trap 16. Set the interval to one minute and confirm exactly one status box and one written file per
  minute. A regression there shows up as duplicate saves burning two ring slots per interval. And the
  whole Commitment rename flow: the button, the dialog, the autosave under the new name, the slot file
  being removed, and the notice disappearing afterwards.
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
- `About/About.xml` no longer declares the `brrainz.harmony` dependency (#8). It still carries no
  `<url>`, so GPLv3 binaries ship with no source pointer. Worth fixing at the next upload.
- `Workshop/*.md` holds the nine store descriptions, but `Verse.Steam.Workshop.SetWorkshopItemDataFrom`
  calls `SteamUGC.SetItemDescription` only inside `if (creating)`, so the in-game uploader cannot
  republish them. Changing store copy needs the Steam website, which is region-blocked from this machine.
  See the `ship-mod` skill before publishing.

## One naming decision worth not re-litigating

The chronosave ring is keyed on **`Faction.OfPlayer.Name`, the player faction's name**, not on the
settlement name. RimWorld treats those as two separate things: `Dialog_NamePlayerFaction` sets the
first and `NamePlayerFactionBaseMessage` names the second, and several vanilla translations use a
clearly different word for them (German "Fraktion", Russian "фракция", Polish "frakcja").

The faction is the right key, because it is the identity that persists across a whole playthrough
including a move to a different settlement. But the mod's own user-facing strings say "colony" in all
nine languages, because that is what players call it and it is what the single naming prompt reads as
in play. The gap is real and known: a player who renames only their settlement will not see the
filenames change. It has not been judged worth two more strings to explain.
