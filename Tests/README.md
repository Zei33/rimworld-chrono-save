# Tests

```sh
export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api
dotnet test Tests/ChronoSave.Tests.csproj
```

net472 NUnit, running against the real `Assembly-CSharp.dll` from the installed game rather than a
stub. `RimWorldDir` must be set; the workspace `.claude/settings.json` and `~/.zshrc` both export it.

This project is deliberately not in `rimworld-chrono-save.sln`, so `dotnet build` on the solution
still builds only the mod and stays at zero warnings. The mod's own `.csproj` removes `Tests/**` from
its compile items, so nothing here can reach the shipped assembly.

## How it is wired

The mod's sources are compiled into the test assembly rather than referenced as a built DLL. That
keeps private and internal members reachable without an `InternalsVisibleTo`, and it avoids a
`ProjectReference` rebuilding the mod into `1.6/Assemblies/net472` in Debug, which is where
`build.sh` expects to stage a Release artefact from.

## What can and cannot be tested

Unity types load fine outside a Unity player. The real boundary is static game state and native
calls, which is narrower than it sounds but rules out most of this mod:

- `ChronoSaveGameComponent` reads settings through the static `ChronoSaveMod.Settings`, which only
  the game populates, and reads `UnityEngine.Time.realtimeSinceStartup`, which is a native call.
- `GameComponentUpdate` additionally needs `Current.Game`, `Find.World` and `GameDataSaveLoader`.
- Harmony cannot patch on this runtime at all, in or out of the game process, so
  `GameComponentInjectionPatch` has no path to automated coverage.

So every decision that can be stated as arithmetic lives in `ChronoSaveSchedule` and is tested
directly. `HarnessTests` pins the boundary itself: if the settings static ever becomes injectable,
`TheGameComponentStillCannotBeExercisedWithoutTheModSettingsStatic` starts failing, which is the
signal to delete it and test the component directly.

Coverage should be quoted against `ChronoSave.Core.ChronoSaveSchedule`, not the repo. A whole-repo
figure would be misleading, because the settings UI, the game component's game-facing half and the
Harmony patch are all unreachable and are roughly half the code.

The background to all of this is `docs/spikes/test-harness/README.md` in the workspace, which
records what each experiment established, including the two whose failure is the finding.
