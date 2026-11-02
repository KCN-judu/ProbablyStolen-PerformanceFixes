# Performance Fixes

![Performance Fixes](preview.png)

A mod for *Probably Stolen*. Removes stutters from the base game when you work with large, packed storage: opening and closing full windows, loading a large store, and trading with nested containers. Every fix keeps the game's behaviour and can be switched off on its own.

**Documentation:** <https://kcn-judu.github.io/ProbablyStolen-Mods/en/performance-fixes/> ([简体中文](https://kcn-judu.github.io/ProbablyStolen-Mods/zh-cn/performance-fixes/))

Part of the [Probably Stolen mods collection](https://github.com/KCN-judu/ProbablyStolen-Mods).

## Install

Subscribe on the Steam Workshop, or copy a release folder into the game's `Mods` folder. Then enable the mod in the game's Mods menu and restart the game.

## Build

```bash
./build.sh            # out/performance_fixes
./build.sh --install  # also copies it to the game's Mods folder
```

See [Building](doc/manual/building.md) for the requirements and the `GAME` and `CSC` variables.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/` | The mod's source |
| `doc/manual/` | Documentation, in English (the source of every translation) |
| `doc/locale/` | Translations of the documentation, as gettext catalogs |
| `manifest.xml`, `preview.png` | What the game reads, next to the DLL |
| `WORKSHOP.txt` | The Steam Workshop description, in BBCode |
| `CHANGELOG.md` | Changes by version |

## License

[Apache License 2.0](LICENSE).
