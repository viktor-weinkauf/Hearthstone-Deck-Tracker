[CmdletBinding()]
param([string]$ArenaHelpersRoot = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))))
$ErrorActionPreference = 'Stop'
$ArenaHelpersRoot = (Resolve-Path -LiteralPath $ArenaHelpersRoot).Path
$hdtDir = Join-Path $env:LOCALAPPDATA 'HearthstoneDeckTracker\current'
$hdtExe = Join-Path $hdtDir 'HearthstoneDeckTracker.exe'
$project = Join-Path $ArenaHelpersRoot 'ArenasmithBridge\ArenasmithBridge.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw 'This deployment uses the existing Arena Comparison Bridge. Supply its ArenaHelpersRoot.' }
$projectText = Get-Content -LiteralPath $project -Raw
if (-not $projectText.Contains('Forks/Hearthstone-Deck-Tracker/LocalRepairs/RewindReplayRepair.cs')) {
    throw 'Bridge project does not reference the maintained repair source; integrate the source before deployment.'
}
& (Join-Path $PSScriptRoot 'Test-Installed.ps1') -HdtPath $hdtDir
$manifestPath = Join-Path $PSScriptRoot '.artifacts\installed\rewind-compatibility.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$stage = Join-Path $ArenaHelpersRoot 'Builds\maintained-replay-fix'
& dotnet build $project -c Release --nologo -v quiet -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Bridge build failed; installed files unchanged.' }
if ((Get-FileHash -LiteralPath $hdtExe).Hash -ne $manifest.hdt_sha256 -or
    (Get-FileHash -LiteralPath (Join-Path $hdtDir 'HearthWatcher.dll')).Hash -ne $manifest.hearthwatcher_sha256) {
    throw 'HDT changed after compatibility testing; rerun deployment.'
}
Copy-Item -LiteralPath $manifestPath -Destination $stage
# Never interrupt a match, including Constructed games not shown by the Arena overlay.
if (Get-Process -Name Hearthstone -ErrorAction SilentlyContinue) {
    $mirrorAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $hdtDir 'HearthMirror.dll'))
    $reader = [Activator]::CreateInstance($mirrorAssembly.GetType('HearthMirror.Reflection'))
    $scene = $reader.GetSceneMgrState()
    if (-not $scene -or $scene.Transitioning -or ($scene.Mode -eq 4 -and -not $reader.IsGameOver())) {
        throw 'A match is active or the game state is uncertain. Built files are ready; rerun after returning to the menu.'
    }
}
$targets = @(
    (Join-Path $env:APPDATA 'HearthstoneDeckTracker\Plugins\ArenaComparisonBridge'),
    (Join-Path $hdtDir 'Plugins\ArenaComparisonBridge')
)
$files = @('ArenaComparisonBridge.dll','0Harmony.dll','rewind-compatibility.json')
$backup = Join-Path $ArenaHelpersRoot ('Backups\maintained-replay-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup | Out-Null
$running = @(Get-Process -Name HearthstoneDeckTracker -ErrorAction SilentlyContinue)
foreach ($process in $running) {
    if ($process.Path -ne $hdtExe) { throw 'An HDT process is running from a different installation; no files changed.' }
    if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(15000)) {
        throw 'HDT did not close normally; no files changed. Close it manually and rerun.'
    }
}
$copied = @()
try {
    for ($i = 0; $i -lt $targets.Count; $i++) {
        New-Item -ItemType Directory -Path $targets[$i] -Force | Out-Null
        $saved = Join-Path $backup $i.ToString()
        New-Item -ItemType Directory -Path $saved | Out-Null
        foreach ($file in $files) {
            $target = Join-Path $targets[$i] $file
            $previous = Join-Path $saved $file
            $existed = Test-Path -LiteralPath $target
            if ($existed) { Copy-Item -LiteralPath $target -Destination $previous }
            $copied += [pscustomobject]@{ target=$target; previous=$previous; existed=$existed }
            Copy-Item -LiteralPath (Join-Path $stage $file) -Destination $target
            if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath (Join-Path $stage $file)).Hash) {
                throw "Verification failed: $target"
            }
        }
    }
    $copied | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'files.json') -Encoding utf8
} catch {
    foreach ($item in $copied) {
        if ($item.existed) { Copy-Item -LiteralPath $item.previous -Destination $item.target }
        elseif (Test-Path -LiteralPath $item.target) { Remove-Item -LiteralPath $item.target }
    }
    throw
} finally {
    if ($running.Count) { Start-Process -FilePath $hdtExe -WorkingDirectory $hdtDir -WindowStyle Hidden | Out-Null }
}
Write-Output "Installed tested bridge into both HDT plugin locations. Backup: $backup"
Write-Output "Check $(Join-Path $ArenaHelpersRoot 'Cache\hdt-rewind-repair.json') for active:true after HDT loads."
