# UWUVCI-Mac — To-do list

The port is built and packaged; see README.md for how it is put together and what has been checked.

## Needs a real Wii U base (and a console)
- [ ] Make one inject per console from a real base and boot it on a Wii U. Start with Wii or GameCube, then GBA and
      SNES/NES (those two paths have only been tested against synthetic files).
- [ ] Download a base from inside the app with real keys.
- [ ] Trim a clean GameCube ISO with NKit (the only test image so far was a modified one, which NKit rejects).
- [ ] Try "Force Classic Controller" on a Wii game it applies to.

## Could be added later
- [ ] Wii video mode patching (NTSC ⇄ PAL): needs a replacement for the closed `wii-vmc.exe`.
- [ ] NES/SNES Pixel Perfect mode: needs a replacement for the closed `ChangeAspectRatio.exe`.
- [ ] c2w patcher: FIX94's source is MIT and portable; only the UI around the Ancast key is missing.
- [ ] Nintendont config and N64 INI editors.
- [ ] An Apple Silicon build of `wstrt` (Wii cheat code patching currently needs Rosetta).
- [ ] Developer ID signing and notarization, which would remove the "Open Anyway" step on first launch.
