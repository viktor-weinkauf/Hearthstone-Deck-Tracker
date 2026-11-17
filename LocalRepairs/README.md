# Maintained replay repairs

This branch contains a small source patch for lethal Rewind recording and an
optional runtime repair for the official Windows tracker. The public upstream
source was v1.59.8 when this branch was created; the installed official tracker
was 1.60.3.9031. Do not downgrade the live tracker to the public source build.

## Source patch

The reader receives the earliest active rewind timestamp from LogWatcherManager.
When a reroll has already ended the game, it rereads from the preceding
Gameplay.Start instead of skipping to STATE=COMPLETE. Normal startup and a newer
match retain their normal starting point. The original one-argument Start API
is retained for binary compatibility.

Run `./LocalRepairs/Test-Source.ps1` from PowerShell to build HearthWatcher and run
the six regression cases. It uses HearthDb/HearthMirror from the installed HDT
as build references in the ignored lib directory; it does not modify HDT.

## Installed tracker repair

`RewindReplayRepair.cs` is the canonical source compiled into the existing Arena
Comparison Bridge. It does not replace the official HDT EXE or HearthWatcher DLL.
The hook affects only HDT's reader, only during active Rewind reconstruction.

Run `./LocalRepairs/Test-Installed.ps1` after an HDT update. It runs 23 checks in
an isolated .NET Framework process against the installed reader, using synthetic
logs. No game interaction, replay upload or account data is involved. Only a
successful run writes `.artifacts/installed/rewind-compatibility.json`.

The runtime hook requires that manifest's exact HDT version, HDT EXE hash and
HearthWatcher hash. Unknown binaries stay unpatched. If upstream fixes the bug,
the reproduction test fails deliberately: review and retire the hook rather
than blindly reapplying it.

For this workstation's ArenaHelpers integration:

```powershell
./LocalRepairs/Deploy-Bridge.ps1 -ArenaHelpersRoot D:\Hearthstone\ArenaHelpers
```

This retests, builds the bridge, refuses to interrupt an active game, closes HDT
normally, backs up the previous plugin files, copies the new bridge/Harmony/
manifest into both plugin locations, verifies hashes and restarts HDT if it was
running. Copy failures restore the previous files. The live status is written
to ArenaHelpers/Cache/hdt-rewind-repair.json. No tracker settings are changed.

To roll back, close HDT normally and restore the files listed in a deployment's
Backups/maintained-replay-*/files.json to their recorded targets. Removing the
new approval manifest disables the hook without removing other bridge features.

## Upstream updates

Run `./Update-LocalFork.ps1`. It requires a clean checkout, fetches upstream,
creates a backup branch and rebases the local commits, then runs the source
regressions. Conflicts are aborted for manual review. Nothing is force-pushed,
published upstream, or installed automatically. Deployment is the separate
command above. The original upstream remains the `upstream` remote; your fork
is `origin`.

The Sludge/Announce Darkness display issue belongs to the separate Joust viewer
fork. These changes cannot change HSReplay.net's hosted service or its already
uploaded replays. Fixtures committed here are synthetic; real replays and
personal logs remain outside Git.
