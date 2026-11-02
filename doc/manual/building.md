# Building


## Requirements

- *Probably Stolen*, game version 0.46D. The build compiles against the game's own assemblies in `Probably Stolen_Data/Managed`.
- Git Bash or another POSIX shell on Windows.
- The Roslyn C# compiler from Visual Studio Build Tools. The C# 5 compiler that ships with Windows cannot read some of the game's types.

## Build

```bash
./build.sh            # out/performance_fixes
./build.sh --install  # also copies it to the game's Mods folder
```

Two environment variables override the defaults:

| Variable | Default |
| --- | --- |
| `GAME` | `/d/SteamLibrary/steamapps/common/Probably Stolen` |
| `CSC` | the Roslyn `csc.exe` of Visual Studio 18 Build Tools |

## Tests

The decisions that need neither Unity nor the game live in `src/*.Pure.cs`, and each has a test in `tests/` that compiles with `csc` alone:

```bash
./tests/run.sh
```

| Test | What it checks |
| --- | --- |
| `BuyerRuleTest` | The buying rule on small stores, nesting past the depth limit (no answer, so the game's own check runs), refusal, each item asked once, and 2,000 random stores against the rule as it reads |
| `LayoutBatchTest` | When an inventory layout waits for the end of the frame and when it runs at once |
| `FormationFitTest` | When the group-drag fit check must run again |
| `HighlightDiffTest` | Which highlight squares are switched off after a refresh |
| `PatchRollbackTest` | Which patches a fix that fails halfway removes again |
| `LocaleGateTest` | When the buyer check may be installed, given the game's language |

Each test prints `ok` or `FAIL` per case and exits with a non-zero code if any case fails.

## Languages

The options' text is in `Localization/`, one file per game language, named by the game's locale code: `en`, `zh`, `ja`, `ko`, `de`, `fr`, `es`, `it`, `pl`, `ru` and `ptBR`. A missing line falls back to English.

## Install by hand

Copy the folder `out/performance_fixes` into the game's `Mods` folder, start the game, and enable the mod in the Mods menu. Mods load when the game starts, so restart after enabling or updating one.

The mod declares no capabilities, so the game enables it without a permission prompt.
