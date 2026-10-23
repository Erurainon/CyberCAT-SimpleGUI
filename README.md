# CyberCAT-SimpleGUI

A simplified offshoot of SirBitesalot's CyberCAT.

## Features

**Feature**                                                                     | **Stability**
------------------------------------------------------------------------------- | -----------
Save and load presets for your character's appearance.                          | Stable
Edit the quantity, flags, & mod tree of items in your inventory.                | Stable
Edit quest facts.                                                               | Stable
Quick actions - dedicated controls for changing money & making items legendary. | Semi-Stable

## Game Version Support

**Game Version**    | **Support** | **CyberCAT-SimpleGUI Version**
--------------------| ------------|-----------------------------
Update 2.5          | Experimental | >=v0.29a
Update 2.3          | Full        | >=v0.28a
Patch 2.11          | Full        | >=v0.27d
Patch 1.5X          | Full        | >=v0.20a
Patch 1.3           | Partial     | >=v0.10a_r1
Patch 1.23          | Full        | >=v0.10a

Update 2.5 support uses WolvenKit's `update_2.5` branch (commit `95074158e`). Saving always runs a serialize → re-parse check and **refuses to write** if that validation fails.

## Usage

1. Run **CP2077SaveEditor.exe**
2. Click **"Load Save"**
3. Make changes to your save.
    - Double-click items in your inventory to edit them.
    - Double-click nodes in an item's mod tree to edit them
4. Click **"Save Changes"**

## Headless round-trip harness (Linux / CI)

The GUI is Windows-only. On any OS with the .NET 10 SDK you can validate parse/serialize without the WinForms UI:

```bash
dotnet build SaveRoundTripHarness/SaveRoundTripHarness.csproj -c Release -p:Platform=x64
dotnet SaveRoundTripHarness/bin/x64/Release/net10.0/SaveRoundTripHarness.dll /path/to/sav.dat
```

Optional: `--write out.dat` writes the re-serialized bytes only after validation succeeds.

**Sample needed for full 2.5 verification:** a real Update 2.5 `sav.dat` (plus `metadata.9.json` / `screenshot.png` from the same save folder). No 2.5 sample was available when this fork was prepared, so 2.5 round-trips are untested until one is supplied.

## Building

Requires **.NET 10 SDK**. On non-Windows hosts, WinForms projects need Windows targeting packs:

```bash
dotnet build CP2077SaveEditor.sln -c Release -p:Platform=x64 -p:EnableWindowsTargeting=true
```

Initialize the WolvenKit submodule before building:

```bash
git submodule update --init --recursive
```

### Regenerating item / vehicle / fact lists

`ResourceGenerator` reads `tweakdb_ep1.bin` from a local game install (set `_gameDir` in `ResourceGenerator/Program.cs`). Ship lists under `CP2077SaveEditor/Resources/` are still from the 2.3 update unless regenerated against a 2.5 tweakDB. Missing new TweakDB names only affects dropdown/helper lists; unknown items/facts remain editable by hash/id.

## Todo

- Refactor, refactor, refactor.
- Eat some of the spaghetti.
- Improve the functionality of the appearance tab.
- Move contribution section from readme to github wiki
- Regenerate Vehicles / Facts / ItemClasses / ModifierGroups from Update 2.5 tweakDB when a game install is available

## Credits

[CyberCAT by SirBitesalot and other contributors](https://github.com/WolvenKit/CyberCAT)  
[WolvenKit by the WolvenKit team](https://github.com/WolvenKit/WolvenKit)  
Original SimpleGUI authors: Deweh / DeweySalt, Papayu, and contributors

## Contribution

### Visual Studio / VS Code

**Task** | **Info**
-------- | ----------------------------------
Requires | .NET 10 SDK, Windows workload for GUI
Setup    | `git submodule update --init --recursive`
Build    | Build solution (x64), or the harness project alone on Linux
Debug    | GUI: Windows only
