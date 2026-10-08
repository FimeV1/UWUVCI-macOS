# UWUVCI for macOS

A native macOS version of [UWUVCI AIO](https://github.com/stuff-by-3-random-dudes/UWUVCI-AIO-WPF) (V3.N2), the
Wii U Virtual Console injector. It runs as a normal `.app` on Apple Silicon and Intel Macs: no Wine, no .NET install,
and no separate helper program.

Supported consoles: Nintendo DS, Game Boy Advance (and Game Boy / Game Boy Color), Nintendo 64, SNES, NES,
TurboGrafx-16, MSX, Wii and GameCube.

**Heavily developed using AI (local and cloud models).** Most of the code and
documentation here was written by AI under the author's direction. It is not
fully human-made, so read it and test it with that in mind.

* Cloud: Anthropic's Claude (Opus 5.5), used through Claude Code. The commits
  it wrote are marked `Co-Authored-By: Claude`.
* Local: Qwen 3.8 Flash-Next and Qwen 3.8 27B, run on the author's own PC.

> **What has actually been tested:** the app starts on macOS (Apple Silicon, and the Intel build under Rosetta), and
> whole injects of Wii, GameCube, N64, DS, MSX and TurboGrafx games run through to an installable package, using a
> stand-in base.
> **Not tested on real hardware:** no inject made with this app has been booted on a Wii U yet. Reports are welcome.

| What | Status |
|---|---|
| App starts, first-run download | Tested on macOS 26 (Apple Silicon); Intel build tested under Rosetta only |
| Wii, GameCube, N64, DS, MSX, TurboGrafx injects | Run through to an installable package with a stand-in base; never booted on a console |
| GBA and NES / SNES injects | Tested against synthetic files only, never against a real base |
| Downloading a base from inside the app | Untested (needs real keys) |
| Config files (`.uwuvci`) shared with the Windows app | Tested: files written here open with the Windows app's serializer |
| Wii video mode patching, NES/SNES Pixel Perfect, c2w patcher, Nintendont and N64 INI editors | Not available (see below) |

**No keys, base games or ROMs are included, and none are provided.** You need your own Wii U Common Key, the Title
Key of the base game you use (or a base dumped from your own Wii U), and your own ROMs, exactly as on Windows.

## Download and first start

Get the zip for your Mac from the [Releases](../../releases) page:

* `UWUVCI-macOS-AppleSilicon.zip` for Macs with an Apple chip (M1 or newer, A18 Pro)
* `UWUVCI-macOS-Intel.zip` for Intel Macs

Unzip it and drag `UWUVCI` into Applications. The app is not notarized by Apple, so macOS asks you to approve it the
first time:

1. Double-click UWUVCI. macOS says it can't be opened. Click **Done**.
2. Open **System Settings > Privacy & Security**, scroll down, and click **Open Anyway**.

Or, in Terminal: `xattr -dr com.apple.quarantine /Applications/UWUVCI.app`

On first start the app downloads a few small data files. After that, pick a console, choose a base game, choose your
ROM and press **Inject**. It works the same way as on Windows, so the
[UWUVCI guides](https://uwuvci-prime.github.io/UWUVCI-Resources/) apply.

Good to know:

* Wii and GameCube injects need about 15 GB of free disk space while they are being made.
* Finished injects go to `Documents/UWUVCI/InjectedGames`; settings, bases and logs live in
  `~/Library/Application Support/UWUVCI`.
* Wii cheat code (GCT) patching uses a tool that only exists for Intel Macs, so on Apple Silicon it needs Rosetta
  (`softwareupdate --install-rosetta`).

## Not available on macOS

These rely on closed Windows-only programs with no replacement yet: Wii video mode patching (NTSC ⇄ PAL), NES/SNES
Pixel Perfect mode, the cafe2wii (c2w) patcher, and the Nintendont and N64 INI editors. The app skips the first two
and tells you so.

## How it works

The Windows app shells out to about twenty Windows-only `.exe` tools. Here each of them is replaced by managed code
running inside the app, except Wiimms ISO Tools and SZS Tools, which exist as native macOS programs and are bundled.

| Windows tool | Here |
| --- | --- |
| `wit.exe`, `wstrt.exe` | `wit-mac` (universal), `wstrt-mac` (Intel only) |
| `nfs2iso2nfs.exe` | `nfs2iso2nfs.dll` in-process (`Tools/DiscTools.cs`) |
| `ConvertToISO.exe`, `ConvertToNKit.exe` | `NKit.dll` in-process (`Tools/DiscTools.cs`) |
| `wiiurpxtool.exe` | `Tools/RpxTool.cs` |
| `RetroInject.exe`, `GetExtTypePatcher.exe`, `N64Converter.exe`, `BuildPcePkg.exe`, `BuildTurboCdPcePkg.exe` | `Tools/RomTools.cs` |
| `psb.exe`, `MArchiveBatchTool.exe` | `GMWare.M2` in-process (`Tools/GbaTool.cs`) |
| `png2tga`, `jpg2tga`, `bmp2tga`, `tga2png`, `tga_verify` | `Tools/ImageTools.cs` (Skia) |
| `sox.exe` | macOS `afconvert` (`Tools/BootSoundTool.cs`) |
| `CNUSPACKER.exe` | the CNUSPACKER library, driven by `Engine/NusPacker.cs` |

| Path | What it is |
| --- | --- |
| `src/UWUVCI.Core` | The injector: engine, tool replacements, base/key handling, downloads. No UI. |
| `src/UWUVCI.App` | The app (Avalonia). |
| `src/UWUVCI.Cli` | Command line front end for the engine, with a self-test. Used for testing and by the build. |
| `src/UWUVCI.Shots` | Renders the app's windows to PNG without a screen, to check layouts. Not shipped. |
| `libs/` | Prebuilt libraries shared with the Windows app. |
| `native/` | `wit-mac` and `wstrt-mac`. |
| `packaging/` | The read-me that goes into the zip. |
| `tools/` | Scripts for syncing to a Mac and making a stand-in base for tests. |

## Building

On a Mac with the .NET 8 SDK and the Xcode command line tools:

```sh
./build-mac.sh          # dist/UWUVCI-macOS-AppleSilicon.zip and dist/UWUVCI-macOS-Intel.zip
```

The projects also build and run on Windows (`dotnet run --project src/UWUVCI.App`), which is handy for working on
the UI, but only a Mac can produce the signed `.app`.

## Testing

```sh
dotnet run --project src/UWUVCI.Cli -- setup       # downloads tool files and base lists
dotnet run --project src/UWUVCI.Cli -- selftest    # checks every tool replacement, needs no game files
tools/make-fake-base.sh /tmp/base wii              # a stand-in base, to run whole injects without a real one
dotnet run --project src/UWUVCI.Cli -- inject --console WII --base /tmp/base --rom game.wbfs --out /tmp/out --ckey <32 hex>
```

Set `UWUVCI_HOME` to keep test data out of `~/Library/Application Support/UWUVCI`.

What the checks so far cover, beyond the table at the top:

- The self-test passes on macOS and Windows.
- `RpxTool` and the NES/SNES injection produce byte-identical output to the original tools compiled from source.
- NKit runs, but the only GameCube test image was a modified one that NKit rejects; the injector then falls back to
  the untrimmed image. Trimming a clean image is untested.
- The Force Classic Controller patch has not been tried on a game it applies to.

Open items are listed in [TODO.md](TODO.md).

## Credits and licence

This is a port of **UWUVCI AIO** by NicoAICP, Morilli and ZestyTS
([stuff-by-3-random-dudes/UWUVCI-AIO-WPF](https://github.com/stuff-by-3-random-dudes/UWUVCI-AIO-WPF)), and like it
is licensed under the [GNU General Public License v3.0](LICENSE). The engine in `src/UWUVCI.Core` is adapted from
that project's source; the files under `src/UWUVCI.Core/Legacy` are copied from it almost unchanged. The macOS app,
the tool replacements and the build scripts were written for this port in 2026.

It also contains or builds on:

- **Wiimms ISO Tools** (`native/wit-mac`) and **Wiimms SZS Tools** (`native/wstrt-mac`) by Wiimm. Source and
  licence: <https://wit.wiimm.de> and <https://szs.wiimm.de>. The binaries are the ones shipped with
  [UWUVCI-V3-Helper](https://github.com/ZestyTS/UWUVCI-V3-Helper).
- **wiiurpxtool** by CBH (GPL-3.0), ported to C# in `Tools/RpxTool.cs`: <https://github.com/0CBH0/wiiurpxtool>
- **GetExtType Patcher** by FIX94 (MIT), ported in `Tools/RomTools.cs`: <https://github.com/FIX94/GetExtTypePatcher>
- **Retroinject_C** by Morilli, ported in `Tools/RomTools.cs`: <https://github.com/Morilli/Retroinject_C>
- **nfs2iso2nfs**, **GameBaseClassLibrary** and **WiiUDownloaderLibrary** (`libs/`), prebuilt by the UWUVCI authors.
- **CNUSPACKER** and **CDecryptSharp** by NicoAICP, and **GMWare.M2** by cyanic (GPL-3.0-or-later), from NuGet.
- **Avalonia** (MIT) and **SkiaSharp** (MIT).
- Boot image frames and console icons from the UWUVCI project. Nintendo, Wii U and the console names and logos are
  trademarks of Nintendo; this project is not affiliated with or endorsed by Nintendo.

At first start the app downloads data files (among them Nintendont and NKit) from the UWUVCI tools repository.
