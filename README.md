# MV-Plugins

A collection of standalone plugins for [**Mapset Verifier**](https://github.com/Naxess/MapsetVerifier), providing advanced snapshot translation and comprehensive Ranking Criteria skin verification.

---

## Included Plugins

### 1. Robust Snapshots (`MapsetVerifier.Plugin.CustomSnapshots.dll`)
Replaces Mapset Verifier's default `BeatmapDiffer` snapshot comparison with robust alignment algorithms while seamlessly powering MV 2.2's new interactive in-game visualizers:
- **DTW & RANSAC Alignment**: Accurately aligns timing changes during map edits even with multi-second section-wide time-shifts (e.g. MP3 offset adjustments, removing base MV's hardcoded 1000 ms limit).
- **Transformation / Flip Detection**: Detects pattern flips (Ctrl+H horizontal flip, Ctrl+J vertical flip, dual flip, and Catch flip) and reports them cleanly as flipped patterns rather than 100% deletions and additions.
- **Unstacked Position Tracking**: Uses raw unstacked coordinates for hit object matching to prevent stacking-cascade ghost diffs.
- **Sub-Snap Jitter Filtering**: Distinguishes between global section shifts and minor local timing adjustments or snap divisor roundings.
- **Break Shift Grouping**: Automatically groups uniform break time changes with section timing shifts.

### 2. Skin Checks (`MapsetVerifier.Plugin.SkinChecks.dll`)
Enforces the official [osu! Ranking Criteria (Skinning)](https://osu.ppy.sh/wiki/en/Rules/Ranking_criteria) and [Skin Set List](https://osu.ppy.sh/wiki/en/Rules/Ranking_criteria/Skin_set_list) across all modes (**osu!standard**, **osu!taiko**, **osu!catch**, and **osu!mania**):
- **Complete Skin Sets**: Verifies that when any gameplay element of a set is skinned in a beatmap folder, all required elements in that set are present (e.g., `cursor.png` + `cursortrail.png`, `hitcircle.png` + `hitcircleoverlay` + `approachcircle.png`).
- **Grouped Digit Sets**: Ensures digits are fully skinned together (e.g., `default-0` through `default-9`, `score-0` through `score-9`).
- **Catch Hitbox Dimensions**: Enforces strict pixel dimensions for osu!catch hitboxes:
  - Fruits: $128 \times 128$ px ($256 \times 256$ @2x)
  - Drops: $82 \times 103$ px ($164 \times 206$ @2x)
  - Catcher: $306 \times 320$ px ($612 \times 640$ @2x)
- **Catch Rules**: Checks that custom catchers declare `Version: 2` or higher in `skin.ini`, and verifies fruit sprites are greyscale for combo tintability.
- **Comboburst Limits**: Checks that comboburst assets do not exceed the $768$ px maximum height.
- **Element Visibility**: Ensures gameplay elements are not completely transparent or 0-alpha (with exemption for `cursormiddle.png` and `fruit-drop-empty.png`).
- **Mode Mismatches**: Warns when skin assets for a different mode (e.g., Taiko or Mania assets) are bundled into a set without matching difficulties.
- **File Integrity**: Flags 0-byte or corrupted image files.

---

## Build Guide

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Compilation
Build the entire plugin solution using the .NET CLI:
```bash
dotnet restore MV-Plugins.sln
dotnet build MV-Plugins.sln -c Release --no-restore
```

This compiles both plugin DLLs into their respective output folders:
- `bin/Release/net10.0/MapsetVerifier.Plugin.CustomSnapshots.dll`
- `MapsetVerifier.Plugin.SkinChecks/bin/Release/net10.0/MapsetVerifier.Plugin.SkinChecks.dll`

### Building Against a Custom Host Version
To deploy directly to your local Mapset Verifier installation:
```powershell
dotnet build MV-Plugins.sln -c Release -p:DeployPlugin=true
```

> **Note**: Automatic deployment to `%APPDATA%` is opt-in via `-p:DeployPlugin=true` to prevent accidental overwrites during local development.

---

## Installation & Usage

1. Download the latest compiled DLLs from the [Releases](https://github.com/hikizisa/MV-Plugins/releases) page (or compile locally).
2. Copy the DLL files you wish to use into your Mapset Verifier Custom Checks folder:
   - **Windows**: `%APPDATA%\Mapset Verifier Externals\CustomChecks\`
   - **Linux**: `~/.local/share/Mapset Verifier Externals/CustomChecks/`
3. Restart Mapset Verifier. The application logs will display:
   ```text
   [CustomSnapshotsPlugin] Initializing robust snapshot differ hook...
   [DetourHelper] Successfully applied x64 detour at 0x... -> 0x...
   [CustomSnapshotsPlugin] Hooked BeatmapDiffer.Diff successfully. Robust RANSAC/DTW alignment active.
   [SkinChecksPlugin] MapsetVerifier Skin Checks Plugin loaded successfully.
   ```
4. Mapset Verifier will now automatically use the enhanced snapshot differ and run the skin verification checks!

---

## Running Tests

Run the full automated test suite (25 tests) with:
```bash
dotnet test MV-Plugins.sln
```
