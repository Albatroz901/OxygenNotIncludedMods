# Smart Connect

Oxygen Not Included mod: connecting pipes, conveyor rails or wires to an existing line no longer
rebuilds the existing tiles in the new material or type. Full description: `WORKSHOP_DESCRIPTION.txt`.

## Build

Requirements: .NET SDK 8 or later, Oxygen Not Included installed.

1. Open a terminal in this folder.
2. `dotnet build -c Release`
   - Restores PLib and ILRepack from NuGet (internet needed the first time).
   - Merges PLib into `SmartConnect.dll` (`ILRepack.targets`).
   - Deploys `SmartConnect.dll`, `mod.yaml`, `mod_info.yaml`, `LICENSE.txt` and
     `THIRD-PARTY-NOTICES.txt` to `Documents\Klei\OxygenNotIncluded\mods\Local\SmartConnect`.
3. Game installed elsewhere:
   `dotnet build -c Release -p:ONIManaged="X:\...\OxygenNotIncluded_Data\Managed"`

## How it works

Patches on `BaseUtilityBuildTool`, shared by liquid and gas pipes, conveyor rails, power and
automation wires:

- `BuildPath`: a prefix computes the protected tiles; a transpiler replaces the
  `IsValidBuildLocation` call that opens the replacement block with
  `ReplaceRules.IsValidReplaceLocation`, which returns false for protected tiles.
- `VisUpdater` (preview coroutine): a postfix on its `MoveNext` tints the existing tiles.

Rule: an existing tile in the drag is rebuilt only if one of its drag neighbours is also an existing
tile it was already connected to before the drag. Otherwise it keeps its material and type and only
gets the new connection. A single click on one tile still rebuilds it (vanilla).

Check: `Player.log` should contain `[SmartConnect] loaded` and **not**
`[SmartConnect] BuildPath: replacement check not found` (that would mean a game update changed
`BuildPath`: the mod then does nothing and the game keeps its vanilla behaviour).
Options are stored by PLib in `config.json` inside the mod folder and apply immediately.

## Workshop

Add a `preview.png` (512x512 recommended, under 1 MB) to the mod folder, then use Klei's mod
uploader (Steam → Library → Tools → Oxygen Not Included Uploader). Page text: `WORKSHOP_DESCRIPTION.txt`.

## License

Public domain (The Unlicense, see `LICENSE.txt`): copy, modify and republish freely.
The compiled DLL also contains PLib by Peter Han, under the MIT License (`THIRD-PARTY-NOTICES.txt`).

Written by Claude, an AI by Anthropic. Source: https://github.com/Albatroz901
