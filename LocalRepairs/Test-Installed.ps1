[CmdletBinding()]
param(
    [string]$HdtPath = (Join-Path $env:LOCALAPPDATA 'HearthstoneDeckTracker\current'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '.artifacts\installed')
)
$ErrorActionPreference = 'Stop'
$HdtPath = (Resolve-Path -LiteralPath $HdtPath).Path
$hdtExe = Join-Path $HdtPath 'HearthstoneDeckTracker.exe'
$watcherDll = Join-Path $HdtPath 'HearthWatcher.dll'
$before = @((Get-FileHash -LiteralPath $hdtExe).Hash, (Get-FileHash -LiteralPath $watcherDll).Hash)
$version = [Reflection.AssemblyName]::GetAssemblyName($hdtExe).Version.ToString()
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$fixture = Join-Path $OutputDirectory 'synthetic-fixture'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
# Synthetic reproduction; never copy real matches or account data into the fork.
$power = @(
    'D 16:13:00.0000000 Header',
    'D 16:13:35.2547857 GameState.DebugPrintPower() - CREATE_GAME',
    'D 16:13:35.3000000 GameState.DebugPrintPower() - Player EntityID=2 PlayerID=1',
    'D 16:27:40.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=TURN value=20',
    'D 16:27:47.8860000 GameState.DebugPrintPower() - RESET_GAME',
    'D 16:27:48.3844140 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE'
)
[IO.File]::WriteAllText((Join-Path $fixture 'Power.log'), ($power -join "`r`n") + "`r`n")
[IO.File]::WriteAllText((Join-Path $fixture 'LoadingScreen.log'), "D 16:13:00.0000000 Header`r`nD 16:13:35.2547857 Gameplay.Start`r`n")
$project = Join-Path $PSScriptRoot 'Tests\InstalledRewindTests.csproj'
& dotnet build $project -c Release --nologo -v quiet "-p:HdtPath=$HdtPath"
if ($LASTEXITCODE -ne 0) { throw 'Compatibility test build failed; no approval written.' }
$runner = Join-Path $PSScriptRoot 'Tests\bin\Release\net472\InstalledRewindTests.exe'
& $runner $fixture $HdtPath | Tee-Object -FilePath (Join-Path $OutputDirectory 'test-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed or upstream behavior changed; review before enabling the fix.' }
$after = @((Get-FileHash -LiteralPath $hdtExe).Hash, (Get-FileHash -LiteralPath $watcherDll).Hash)
if ($before[0] -ne $after[0] -or $before[1] -ne $after[1]) { throw 'HDT updated during testing; run again.' }
$manifest = [ordered]@{
    schema_version = 1
    hdt_version = $version
    hdt_sha256 = $after[0].ToLowerInvariant()
    hearthwatcher_sha256 = $after[1].ToLowerInvariant()
    repair_source_sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'RewindReplayRepair.cs')).Hash.ToLowerInvariant()
    tested_utc = [DateTime]::UtcNow.ToString('o')
    checks = 23
    fixture = 'synthetic lethal Rewind plus ordinary startup and compatibility controls'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'rewind-compatibility.json') -Encoding utf8
Write-Output "Compatibility verified for HDT $version. Manifest: $(Join-Path $OutputDirectory 'rewind-compatibility.json')"
