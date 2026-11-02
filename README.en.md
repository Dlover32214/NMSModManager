# All Mod's Sky 2.0.6

[简体中文](README.md) | **English**

> A Windows mod manager for *No Man's Sky*  
> .NET 8 + WPF · single-file release · Chinese UI · NMSModManager data compatibility

All Mod's Sky is a local mod manager for *No Man's Sky*. It does **not** copy mods into a separate library. Instead, it directly manages the game's real directories:

```text
<Game>\GAMEDATA\MODS
<Game>\GAMEDATA\MODS_disabled
```

Enabling a mod means keeping its folder or `.pak` file in `MODS`; disabling means moving it to the sibling `MODS_disabled`. Both operations are same-volume moves/renames, so they are fast, use no extra disk space, and cannot create a second out-of-sync copy of the same mod.

---

## Contents

- [1. Project Overview](#1-project-overview)
- [2. Core Concepts](#2-core-concepts)
- [3. Feature Reference](#3-feature-reference)
  - [3.1 Mod Library](#31-mod-library)
  - [3.2 Enable, Disable and Sync](#32-enable-disable-and-sync)
  - [3.3 External Mods and Adoption](#33-external-mods-and-adoption)
  - [3.4 Batch Selection and Drag Reordering](#34-batch-selection-and-drag-reordering)
  - [3.5 Conflict Detection and Load Order](#35-conflict-detection-and-load-order)
  - [3.6 Log Diagnostics](#36-log-diagnostics)
  - [3.7 AMUMSS Toolchain](#37-amumss-toolchain)
  - [3.8 Discover (Nexus / oogc)](#38-discover-nexus--oogc)
  - [3.9 Save Game Reader](#39-save-game-reader)
  - [3.10 Settings](#310-settings)
  - [3.11 UI and Interaction](#311-ui-and-interaction)
  - [3.12 About and Updates](#312-about-and-updates)
- [4. Data Directories and Compatibility](#4-data-directories-and-compatibility)
- [5. Requirements](#5-requirements)
- [6. Running the App](#6-running-the-app)
- [7. Build, Test and Publish](#7-build-test-and-publish)
- [8. Project Structure](#8-project-structure)
- [9. Architecture](#9-architecture)
- [10. FAQ](#10-faq)
- [11. Known Limitations and Trade-offs](#11-known-limitations-and-trade-offs)
- [12. License](#12-license)

---

## 1. Project Overview

### What problem does it solve?

Since *No Man's Sky* 5.50, the game reads loose `EXML` / `MBIN` directories directly from `GAMEDATA\MODS`; legacy `.pak` mods may still appear. Managing these files manually is error-prone:

- Enabling/disabling a mod requires manually moving folders around.
- It is hard to tell which folders were imported by a manager and which were placed manually.
- When several mods touch the same `EXML` path or replace the same `MBIN`, it is unclear which one wins.
- It is easy to forget `DISABLEMODS.TXT`.
- Installing AMUMSS, generating mods and collecting `CreatedMODS` output is a scattered workflow.
- Nexus / oogc downloads must be unpacked and dragged into `MODS` manually.

This project brings those workflows into one WPF desktop application.

### Main capabilities

| Capability | Description |
|---|---|
| Mod library | Import zip / rar / 7z / tar / gz / bz2 / xz / zst / cab / pak / folders; enable, disable, reorder and batch-manage |
| External mods | Detect `MODS` content not owned by the app; enable/disable directly or adopt it |
| Deployment sync | Reconcile the UI state with the game directory and maintain `DISABLEMODS.TXT` |
| Launch modes | Launch vanilla (disable all mods) or launch modded (restore the last enabled set); both sync before launching |
| Conflict detection | Scan `EXML` / `MBIN` path overlaps and predict the winner from load order |
| Load order | Read/write `GCMODSETTINGS.MXML` `ModPriority` with automatic backup |
| Log diagnostics | Extract errors from game logs and attribute them to mods where possible |
| AMUMSS | Detect AMUMSS, analyze repair plans, run `BUILDMOD.bat`, collect output |
| Discover | Browse Nexus (API key) and oogc.cc with paging, details, browser jump and random 10 (Nexus has no keyword search) |
| About | Project/release links, program/data directories, automatic latest GitHub Release check; downloads, replaces the exe and restarts |
| Save games | Read `.hg` save version, name, platform, mode and play time |
| Compatibility | Read/write legacy `NMSModManager` `settings.json` / `library.json` fields |
| Portability | AppData by default; `--data` can point at any writable directory |

### Technology

- C# / .NET 8
- WPF (Windows Presentation Foundation)
- MVVM with hand-written `ObservableObject` / `RelayCommand` / `AsyncRelayCommand`
- `System.Text.Json`
- No third-party NuGet dependencies
- Framework-dependent single-file publishing (requires the .NET 8 Desktop Runtime)

---

## 2. Core Concepts

### 2.1 The library is `MODS`

There is no separate "mod warehouse". The UI records are only an index; the real files always live in:

```text
<Game>\GAMEDATA\MODS
```

The record field `FolderName` (called `LibraryPath` / `DeployName` in the legacy format) is the directory name under `MODS`. Whether a mod was imported by this app or placed manually, the game sees the same single copy.

### 2.2 Enabled and disabled

- **Enabled:** the mod folder/file is in `GAMEDATA\MODS`.
- **Disabled:** the mod folder/file is in `GAMEDATA\MODS_disabled`.

Disabling is **not** deletion. The mod can be enabled again at any time. Files are only removed from disk when the user explicitly chooses "Permanently delete".

### 2.3 External mods

Any folder or `.pak` in `MODS` / `MODS_disabled` that is not present in this app's index is treated as an **external mod**. External mods can be:

- enabled/disabled directly (the app moves the files);
- **adopted** into `library.json`, after which they appear in the main library list.

Adoption does not move files; it only creates an index record.

When the library is empty, the external mod panel opens automatically so existing mods do not appear to be "lost".

### 2.4 List order = load priority

The order of the library list is the load order. Items higher in the list have higher priority; when mods conflict, the higher item overrides the lower one.

"Sync to Game" does not rewrite `GCMODSETTINGS.MXML`. Use "Apply Order" explicitly to write the current list order into the game's load-order configuration.

### 2.5 Loose 5.50+ mods vs. legacy `.pak`

- 5.50+: mods are usually folders containing `EXML` / `MBIN` files.
- Legacy `.pak`: still supported as a compatibility import; the `.pak` is placed in `MODS` and recorded.
- A record is either a folder mod or a pak mod, distinguished by whether `PakFiles` is empty.

### 2.6 DISABLEMODS.TXT

*No Man's Sky* uses `GAMEDATA\PCBANKS\DISABLEMODS.TXT` to control mod loading. During sync:

- if at least one mod is enabled: remove/do not keep `DISABLEMODS.TXT`;
- if every mod is disabled and the setting allows it: create `DISABLEMODS.TXT`.

The setting `CreateDisableModsFile` controls automatic creation.

---

## 3. Feature Reference

### 3.1 Mod Library

The library is the main page.

#### Import

Supported formats:

- `.zip`
- `.rar` / `.7z` / `.cab` (7-Zip or Bandizip)
- `.tar` / `.tar.gz` / `.tgz` / `.tar.bz2` / `.tbz2` / `.tar.xz` / `.txz` / `.tar.zst` / `.tzst` (Windows tar, 7-Zip or Bandizip)
- `.pak`
- normal mod folders

Ways to import:

1. Click **Import Mods** and select multiple files.
2. Click **Add Folder**.
3. Drag files/folders anywhere onto the window.

The import flow:

1. Extract/copy into a temporary directory.
2. Detect the content root inside archives, avoiding an extra nested folder.
3. Choose a non-conflicting destination folder name.
4. Move the content into `GAMEDATA\MODS`.
5. Write `library.json`; the mod is enabled by default.
6. Run a sync once.

Extraction note: Windows `tar.exe` is preferred for tar-family archives; `.rar` / `.7z` / `.cab` try 7-Zip, Bandizip (`bz.exe`) and Windows tar in that order. If no tool is available, install 7-Zip or Bandizip.

#### Search and filters

- Search box: filter by mod name, version, source or folder name.
- Filters: All / Enabled / Disabled / Issues.
- Top counters: total mods, enabled mods, selected mods.
- "Issues" is based on conflict/health scan results.

#### Row actions

Each mod row provides:

- enable/disable toggle;
- open the mod folder;
- remove/delete menu;
- whole-row drag reordering in normal mode;
- status, size, version and format information.

#### Two meanings of "delete"

The delete action distinguishes:

- **Remove from library:** disable the mod first (files go to `MODS_disabled`), then remove the index record. Files are kept.
- **Permanently delete…:** after a second confirmation, delete the corresponding folder/files in both `MODS` and `MODS_disabled`. This cannot be undone by the app.

Batch delete is also permanent and shows the selected mod count and total size before confirming.

#### Other library actions

- Open the `MODS` folder.
- Refresh game state.
- Sync to game.
- Apply order.
- External mod panel.
- Batch action bar.

### 3.2 Enable, Disable and Sync

#### Row toggle

Toggling a row:

1. updates the record and saves `library.json`;
2. runs deployment sync;
3. moves the folder/`.pak` between `MODS` and `MODS_disabled`;
4. updates `DISABLEMODS.TXT`;
5. refreshes external mods and UI state.

#### "Sync to Game"

Use this to reconcile the index with disk state after:

- manually modifying `MODS`;
- copying mods from elsewhere;
- an abnormal exit;
- batch operations.

The sync report includes:

- how many mods were moved/synced;
- failures;
- missing entries (recorded but not found on disk).

#### Important

The game scans `MODS` only at startup. Restart *No Man's Sky* after enabling or disabling mods.

#### Launch Vanilla / Modded

The sidebar has two launch buttons:

- **Launch Modded**: if *Launch Vanilla* was used before, restore the enabled set saved at that time; otherwise use the current state. Then sync and launch.
- **Launch Vanilla**: record the currently enabled mods, disable all managed mods, sync (creating `DISABLEMODS.TXT` when needed), then launch without mods.

The enabled IDs for vanilla mode are stored in `vanilla-launch.json` in the data directory. The next *Launch Modded* restores them and deletes the file. Closing the manager does not lose the state; if syncing fails, the game is not launched, and a failed vanilla launch restores the previous enabled state.

### 3.3 External Mods and Adoption

The external mod panel lists:

- folders in `MODS` not owned by this app;
- folders in `MODS_disabled` not owned by this app;
- `.pak` files in the root.

Each external mod offers:

- current state (enabled/disabled);
- enable/disable;
- adopt.

Adoption creates a `ModRecord` in `library.json` without moving files. The mod can then be managed by the normal library toggles.

When the library is empty, this panel opens automatically.

### 3.4 Batch Selection and Drag Reordering

#### Batch selection

The toolbar has a **Batch Select** button:

- In batch mode, click anywhere on a row except interactive controls to select/deselect it.
- No `Ctrl` key is needed.
- Toggles, buttons and scrollbars do not accidentally toggle selection.
- The batch bar stays visible even with zero selected.
- Exit with **Exit Batch**, **Clear Selection**, or `Esc`.

Batch actions:

- enable selected;
- disable selected;
- move to top/bottom;
- permanently delete selected.

#### Drag reordering

- In normal mode, press and drag any non-button part of a row to reorder it.
- The dragged row lifts and gets a shadow.
- After dropping, all affected rows animate smoothly from their old positions to their new positions instead of snapping.
- The order is written back to `library.json`.
- Click **Apply Order** when the game should read the new order.

### 3.5 Conflict Detection and Load Order

#### Conflict detection

**Tools → Conflict Detection** scans the library's `EXML` / `MBIN` files and groups them by "the same vanilla path is touched by multiple mods":

- **MBIN replacement conflict:** multiple mods replace the same `MBIN` path; only the last one loaded wins. This is a hard conflict.
- **EXML same-property conflict:** multiple `EXML` patches change the same property of the same path; the last one loaded wins.
- **EXML same file, different properties:** path overlap only; the patches may coexist.
- **EXML patch + MBIN replacement:** allowed; the patch is applied on top of the replacement.

Results include:

- conflict group;
- involved mods;
- enabled state;
- load order;
- predicted winner.

Higher in the list means higher priority.

#### Load order

The game stores load order in:

```text
<Game>\Binaries\SETTINGS\GCMODSETTINGS.MXML
```

The app can:

- read existing `ModPriority` values;
- write the current list order into `ModPriority`;
- back up the file as `GCMODSETTINGS.MXML.bak` before writing;
- report that the file is missing and suggest entering the game once if needed.

### 3.6 Log Diagnostics

**Tools → Log Diagnostics** scans game logs and extracts lines containing:

```text
error / failed / failure / exception / unable to / could not /
cannot / missing / corrupt / invalid / crash / assert
```

It then:

- groups results by log file;
- tries to attribute lines to mod names;
- shows "suspect mods";
- displays the matching raw log lines.

To limit memory use, it reads about the last 4 MB of each log, at most 20,000 lines per file, and keeps at most 4,000 errors.

### 3.7 AMUMSS Toolchain

AMUMSS is a *No Man's Sky* mod scripting/compilation toolchain. This app does not bundle it; it detects and invokes an existing installation.

#### Detection

Specify the AMUMSS root in Settings, or let the app search automatically. A valid directory usually contains:

- `BUILDMOD.bat`
- `MBINCompiler.exe`

#### Analyze / repair

**Tools → AMUMSS** can:

- inspect the current mod folder structure;
- determine which lua scripts are needed;
- generate a repair/rebuild plan;
- copy the relevant lua files into AMUMSS's `ModScript` directory.

#### Rebuild

The rebuild flow:

1. Run `BUILDMOD.bat` in its own console so interactive prompts can be answered.
2. Wait for compilation.
3. Collect output from AMUMSS's `CreatedMODS`.
4. Move the original mod folder to a `.bak_yyyyMMdd_HHmmss` backup.
5. Replace the original folder with the new output.
6. Attempt to restore the original on failure.

#### Open locations

The Tools page also opens:

- the AMUMSS root;
- `ModScript`;
- `CreatedMODS`;
- the log directory.

### 3.8 Discover (Nexus / oogc)

#### Nexus Mods

- Requires a personal Nexus API key.
- Supports categories and sorting (trending/latest/recently updated, etc.).
- Category chips wrap automatically with the window width.
- Local paging (fixed page size).
- Cards show cover, title, category, version and author.
- Detail view shows the summary and file list.
- Can open the web page and copy the link; the app does not download mods directly.
- **Random 10** picks 10 mods from a local pool merged from three Nexus sorts (Nexus has no official random endpoint).

Without an API key:

- the Nexus list is unavailable;
- the app still offers a browser entry to the Nexus website;
- mods can be downloaded manually and dragged into the app.

#### oogc.cc

- No API key required.
- Supports category, sort, cost and keyword search.
- Server-side paging.
- Detail view and open-original-page; download in the browser.
- Cover images load asynchronously.

#### Loading experience

- Initial loads show skeleton cards matching the real card size (image area plus two text lines).
- Real cards replace skeletons in place to avoid layout jumps.
- Covers load in the background and do not block card rendering.
- After 25 seconds or on failure, skeletons are replaced by an explicit error and a Retry action.
- Pagination supports previous/next.

### 3.9 Save Game Reader

**Settings → Saves** reads *No Man's Sky* `.hg` save metadata.

An `.hg` file consists of repeated "16-byte block header + LZ4 block" records:

```text
magic(0xFEEDA1E5) / compressed length / decompressed length / reserved
```

The app decompresses only enough to read the header fields; it does not expand the whole tens-of-MB save.

Displayed fields:

- save file;
- save version;
- save name;
- platform;
- game mode;
- play time;
- modified time;
- file size.

It also tries to infer the game version from `backup.*.zip` filenames (for example, `...560.zip` → 5.60).

### 3.10 Settings

Settings include:

- game directory (detect/browse);
- game version (manual or automatic);
- AMUMSS directory;
- Nexus API key;
- dark/light theme;
- whether to create `DISABLEMODS.TXT` automatically;
- save metadata reader;
- open `MODS` / data directory / logs;
- restart as administrator (only if that environment truly requires it).

The API key is saved to `settings.json` in the data directory. If it cannot be saved, check whether the data directory is writable and whether another process is holding `settings.json`.

### 3.11 UI and Interaction

- Left navigation: Library / Discover / Tools / Settings / About.
- Bottom-left: Launch Modded / Launch Vanilla buttons, current-mode hint, game status card, dark-mode switch.
- Top status bar: `MODS` path, counts, version.
- Dark and light themes.
- Global toasts with scale/fade, glow and an outer shadow.
- Busy overlay for long operations.
- Skeleton loading on Discover.
- 64×64 animated logo in the top-left:
  - holographic planet;
  - a glowing satellite with a trail, orbiting once every 20 seconds;
  - the satellite passes behind the badge on the far side and reappears on the near side;
  - subtitle `A NMS MOD MANAGER`.
- Keyboard shortcuts:
  - `Ctrl+O` import
  - `Ctrl+1` Library
  - `Ctrl+2` Discover
  - `Ctrl+3` Tools
  - `Ctrl+4` Settings
  - `Ctrl+5` About
  - `F5` refresh
  - `Esc` exit batch / dismiss toast

### 3.12 About and Updates

A new **About** page appears below Settings in the left navigation:

- shows the current version and links to the project/release pages;
- shows the program directory (exe location) and data directory (settings/library), with open buttons;
- automatically checks the latest GitHub Release when the page opens;
- shows latest version, publication time and release notes;
- if a new version exists, prompts the user to click **Download update**;
- after download, verifies SHA256 when the release provides a digest, replaces the current exe and restarts;
- asks for confirmation before installing; a background PowerShell helper waits for the app to exit and performs the replacement;
- if the release has no exe/zip asset, directs the user to the release page;
- on check failure, shows the error and keeps the project/release links available.

The check uses the public GitHub API for:

```text
https://github.com/Dlover32214/NMSModManager
```

---

## 4. Data Directories and Compatibility

### 4.1 Data directory priority

The app searches for a writable data directory in this order:

1. `--data <directory>` command-line argument;
2. `ALLMODSSKY_DATA` environment variable;
3. `%APPDATA%\NMSModManager` (legacy-compatible directory);
4. `%APPDATA%\AllModsSky`;
5. `%LOCALAPPDATA%\NMSModManager`;
6. `%LOCALAPPDATA%\AllModsSky`;
7. `exe directory\NMSModManager.data`;
8. `exe directory\data`;
9. `%TEMP%\AllModsSky`.

If the legacy directory already contains `settings.json` / `library.json`, it is preferred so existing configuration is not lost.

Manual override:

```powershell
AllModsSky.exe --data "D:\NMSData"
```

### 4.2 Data files

```text
<DataDir>\
  settings.json       settings
  library.json        mod index
  app.log             application log
  crash.log           unhandled exception log (if any)
  cache\import\       temporary import files
  cache\download\     download cache
```

### 4.3 library.json

Modern fields:

```json
{
  "SchemaVersion": 2,
  "Mods": [
    {
      "Id": "unique id",
      "Name": "Mod name",
      "Source": "Local import",
      "Version": "7.01",
      "FolderName": "folder under MODS",
      "PakFiles": [],
      "Enabled": true,
      "Size": 12345,
      "ImportedAt": "2026-10-31T12:00:00+08:00",
      "Note": ""
    }
  ]
}
```

For legacy NMSModManager compatibility, the app also writes:

```json
{
  "LibraryPath": "folder under MODS",
  "DeployName": "folder under MODS (empty for pak mods)"
}
```

The legacy app only needs `LibraryPath` / `DeployName` / `PakFiles` / `Enabled` and similar fields to read it.

### 4.4 settings.json

```json
{
  "GamePath": "E:\\SteamLibrary\\steamapps\\common\\No Man's Sky",
  "GameVersion": "7.10",
  "AmumssPath": "",
  "NexusApiKey": "",
  "Theme": "Dark",
  "CreateDisableModsFile": true,
  "LibraryPath": "legacy library path (compatibility field)",
  "BackupOnDeploy": true
}
```

`LibraryPath` and `BackupOnDeploy` are legacy fields retained for old readers. The new app does not depend on them for core functionality, but keeps writing them for compatibility.

### 4.5 Legacy compatibility details

- Legacy `/Date(1730000000000)/` timestamps are parsed into normal times.
- Legacy `LibraryPath` / `DeployName` map to the new `FolderName`.
- Modern `ImportedAt` is written as ISO 8601; the legacy `JavaScriptSerializer` can deserialize it too.
- A separate legacy library directory is no longer required: both apps use the game's `GAMEDATA\MODS` as the real library.
- Legacy `LibraryPath` and `BackupOnDeploy` settings are preserved.

---

## 5. Requirements

| Item | Requirement |
|---|---|
| OS | Windows 10 / Windows 11, x64 |
| Runtime | .NET 8 Desktop Runtime (x64) |
| Game | *No Man's Sky* with `GAMEDATA\PCBANKS` or `Binaries\NMS.exe` |
| Optional | 7-Zip or Bandizip (`.rar` / `.7z` / `.cab`; tar-family archives can use Windows tar) |
| Optional | AMUMSS (needed for the AMUMSS toolchain) |
| Network | Required for Nexus / oogc discovery |
| Privileges | Normal user; running as administrator is not recommended |

The release is **framework-dependent single-file**:

- the exe itself is about 1.14 MiB;
- the runtime comes from the installed .NET 8 Desktop Runtime;
- the target machine must have that runtime;
- framework-dependent single-file bundles cannot use compression, and WPF is not safe to trim, so the size will not return to the old .NET Framework 600 KB build.

---

## 6. Running the App

### Normal launch

1. Extract the release package.
2. Double-click `AllModsSky.exe` in File Explorer.
3. Set the game directory on first run (Steam libraries are usually detected automatically).
4. If `MODS` already contains mods, the external mod panel opens automatically when the library is empty; click **Adopt** to take them over.

### Do not run as administrator

The application manifest uses normal-user privileges (`asInvoker`). When running elevated:

- Windows UIPI blocks drag-and-drop from File Explorer to the elevated process;
- dragging mods in appears to do nothing;
- administrator rights are unnecessary when the normal user can already write the game's `MODS`.

If you hit permission issues, check directory ACLs first rather than elevating.

### Release files

- `release\AllModsSky.exe`
- `release\AllModsSky-2.0.6-win-x64.zip`
- `release\README.txt` (quick-start guide)
- `README.en.md` (this full guide)

---

## 7. Build, Test and Publish

### Environment

- .NET SDK 8
- Windows 10/11
- PowerShell (Windows PowerShell 5.1 or PowerShell 7)

### Build

From the project root:

```powershell
dotnet build AllModsSky.slnx -c Release
```

### Run

```powershell
dotnet run --project src\AllModsSky.App\AllModsSky.App.csproj -c Release
```

### Self-test

The repository includes a console self-test program with no third-party test framework:

```powershell
$env:ALLMODSSKY_DATA = "$PWD\.tmp\selftest"
dotnet run --project tests\AllModsSky.SelfTest\AllModsSky.SelfTest.csproj -c Release
```

Current coverage includes:

- import/extraction/content-root detection;
- enable/disable and file movement;
- external mod detection/adoption;
- `DISABLEMODS.TXT`;
- conflict scanning;
- load-order read/write and backup;
- AMUMSS analysis/collection;
- legacy data migration;
- other core paths.

Expected result:

```text
Result: 111 passed, 0 failed
```

### Publish a single file

```powershell
dotnet publish src\AllModsSky.App\AllModsSky.App.csproj `
  -c Release -r win-x64 --no-self-contained `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=false `
  -p:DebugType=none `
  -p:PublishReadyToRun=false `
  -o release\publish
```

Then copy `release\publish\AllModsSky.exe` to `release\`.

`EnableCompressionInSingleFile` must be `false`: framework-dependent single-file publishing does not support compression and otherwise fails with `NETSDK1176`.

---

## 8. Project Structure

```text
NMSModManager\
├─ AllModsSky.slnx                 solution
├─ README.md                       Chinese README (default)
├─ README.en.md                    English README
├─ src\
│  ├─ AllModsSky.Core\             UI-independent core logic
│  │  ├─ Models.cs                 mod/settings/game-info models
│  │  ├─ Infrastructure.cs         AppPaths / JsonStore / FileOps / logging
│  │  ├─ ModLibrary.cs             library.json load/save/normalize
│  │  ├─ Importer.cs               zip/rar/7z/tar/gz/bz2/xz/zst/cab/pak import
│  │  ├─ DeploymentService.cs      MODS / MODS_disabled synchronization
│  │  ├─ VanillaLaunchStore.cs     vanilla launch state save/restore
│  │  ├─ ConflictAnalyzer.cs       EXML/MBIN conflict scan
│  │  ├─ LoadOrderService.cs       GCMODSETTINGS.MXML
│  │  ├─ LogDiagnostics.cs         game log diagnostics
│  │  ├─ AmumssService.cs          AMUMSS detect/analyze/rebuild/collect
│  │  ├─ NexusClient.cs            Nexus API
│  │  ├─ OogcClient.cs             oogc.cc
│  │  ├─ SaveGameReader.cs         .hg save reader (LZ4 blocks)
│  │  ├─ UpdateService.cs          GitHub Release check/download/self-update
│  │  ├─ LegacyDataMigrator.cs     legacy NMSModManager compatibility
│  │  └─ Resources\mapping.json    embedded mapping data
│  └─ AllModsSky.App\              WPF UI
│     ├─ MainWindow.xaml(.cs)      shell, sidebar, toast, animated logo
│     ├─ ViewModels\               page view models
│     ├─ Views\                    Library / Discover / Tools / Settings / About
│     ├─ Themes\                   Dark / Light / Styles
│     ├─ Services\                 dialog / shell UI / theme services
│     ├─ Controls\                 control helpers
│     └─ app.ico / app.manifest
├─ tests\
│  └─ AllModsSky.SelfTest\         console self-test (111 checks)
├─ tools\                          helper scripts (not required at runtime)
├─ release\                        final release artifacts
├─ dist\                           historical builds/temporary publish output
└─ .tmp\                           local temporary directory
```

---

## 9. Architecture

### 9.1 Layers

```text
WPF Views
   ↓ binding
ViewModels (App)
   ↓ calls
Workspace / Services (App + Core)
   ↓
Core: Import / Deployment / Conflict / LoadOrder / AMUMSS / Nexus / Oogc / Saves
   ↓
File system + game directories
```

- **Core** does not reference WPF, making it reusable and easy to self-test.
- **App** handles UI, dialogs, themes and MVVM orchestration.
- **SelfTest** calls Core directly and covers file operations and parsing.

### 9.2 Key services

| Service | Responsibility |
|---|---|
| `AppPaths` | Data directory resolution, fallbacks, log paths |
| `JsonStore` | Indented JSON, atomic writes, `.bak` backup |
| `ModLibrary` | Index load/save/normalize/reorder |
| `Importer` | Archive/folder/pak import |
| `DeploymentService` | Enable/disable moves, external mods, `DISABLEMODS.TXT` |
| `ConflictAnalyzer` | EXML/MBIN path and property conflicts |
| `LoadOrderService` | Read/write `GCMODSETTINGS.MXML` |
| `LogDiagnostics` | Error extraction and mod attribution |
| `AmumssService` | AMUMSS detection, rebuild, collection |
| `NexusClient` / `OogcClient` | Network listing/details |
| `SaveGameReader` | `.hg` header field extraction |
| `LegacyDataMigrator` | Legacy data reading/migration |

### 9.3 Design principles

- **One real location:** mods live only in the game's `MODS`/`MODS_disabled`; no second library copy.
- **Index can be lost, files cannot:** if `library.json` disappears, the external mod panel still finds files on disk.
- **Destructive actions need confirmation:** permanent delete and apply-order are confirmed.
- **Back up before writing:** load order and AMUMSS originals are backed up.
- **Diagnosable:** `app.log` / `crash.log` record core operations and exceptions.
- **Legacy data first:** read old data when possible instead of forcing a migration.

---

## 10. FAQ

### Dragging files does nothing

- Make sure the app is not running as administrator (UIPI blocks Explorer-to-elevated drag/drop).
- Make sure the file is zip / rar / 7z / tar / gz / bz2 / xz / zst / cab / pak / a folder.
- Do not double-click the exe inside a zip; extract it first.
- Alternatively use **Import Mods** / **Add Folder**.

### The library is empty, but the game already has mods

This is expected on first run or after switching machines: the index has no records, but files exist on disk. When the library is empty, the external mod panel opens automatically; click **Adopt**.

### The status bar says "MODS is not writable"

- The exact `UnauthorizedAccessException` or other error is written to `app.log`.
- Prefer launching the exe normally from File Explorer.
- Check the game directory ACL, antivirus, or controlled-folder access.
- Normal user permissions are usually enough; do not immediately elevate.

### Enabling/disabling does nothing in-game

The files moved, but the game only scans `MODS` at startup. Fully exit and restart the game.

### RAR / 7z / tar import fails

The app does not bundle compression libraries: `.rar` / `.7z` / `.cab` need 7-Zip or Bandizip (`bz.exe`); tar-family archives use Windows `tar.exe` first. Install one of those tools or convert the archive to zip.

### Nexus list is empty / API key is invalid

- Generate a personal API key in the Nexus account settings.
- Enter it under **Settings → Nexus Mods** and save.
- Without a key, use the browser entry and download manually.

### AMUMSS is not detected

Point Settings at the AMUMSS directory containing `BUILDMOD.bat` or `MBINCompiler.exe`. AMUMSS is not bundled.

### Can I still use legacy NMSModManager data?

Yes. The app prefers `%APPDATA%\NMSModManager` and understands legacy `LibraryPath` / `DeployName` / `PakFiles` / `/Date(...)` / `BackupOnDeploy` fields.

### Why is it 1.14 MiB instead of 600 KB?

The old 600 KB build was a .NET Framework 4.8 app using the runtime included with Windows. This version is .NET 8 WPF, published framework-dependent and single-file; the runtime comes from the installed .NET 8 Desktop Runtime. WPF is not safe to trim, so around 1 MB is the practical floor for this codebase.

### Can I move the data directory to another drive?

Yes:

```powershell
AllModsSky.exe --data "D:\NMSData"
```

Or set the `ALLMODSSKY_DATA` environment variable.

### Is the UI available in English?

The application UI is currently Chinese. This repository provides English and Chinese documentation; `README.md` is the Chinese default and `README.en.md` is the English version.

---

## 11. Known Limitations and Trade-offs

1. **Requires the .NET 8 Desktop Runtime**  
   The release is not self-contained; the target machine must have the runtime.

2. **The game must be restarted after changing mods**  
   The app only moves files; the game does not hot-reload `MODS`.

3. **RAR/7z depends on external extractors**  
   To keep the app free of third-party NuGet dependencies, `.rar`/`.7z`/`.cab` are delegated to 7-Zip or Bandizip; tar-family archives use Windows tar.

4. **Nexus requires an API key and is limited by the network/API**  
   Without a key, only browser-based manual download is available.

5. **AMUMSS is not bundled**  
   Users must install it and point the app at it.

6. **No cloud sync; updates are opt-in**  
   Data stays local; the About page checks for versions automatically, but downloading, replacing and restarting happen only after you click **Download update**.

7. **Legacy compatibility is field-level**  
   The new app still uses the game's `MODS` as the only real library. If the legacy app used a separate library root, the new app does not create a second copy.

8. **Single-file is not self-contained**  
   The small size is paid for by requiring the system .NET 8 runtime; a fully offline self-contained build would be much larger.

9. **The UI is Chinese-only**  
   There is no multi-language UI switch yet; only the README is bilingual.

10. **It does not modify system settings**  
    The app only manages game mod directories and its own data directory. It does not change the system terminal, Start menu, etc.

---

## 12. License

MIT License (as declared by the project).

---

## Appendix: Quick Start

```text
1. Install the .NET 8 Desktop Runtime.
2. Extract the release and double-click AllModsSky.exe (do not run as administrator).
3. Set the game directory (usually detected automatically).
4. Drag in a zip/folder or click "Import Mods".
5. Use the toggles to enable/disable; click "Sync to Game" when needed.
6. Restart No Man's Sky.
```

If the library is empty but `MODS` already has content: open **External Mods** → **Adopt** → manage everything from the main list.
