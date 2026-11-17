[CmdletBinding()]
param([string]$UpstreamRef = 'upstream/master')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (git status --porcelain) { throw 'Commit or stash local work before updating.' }
    $branch = git branch --show-current
    if ($branch -ne 'fix/replay-reliability') { throw 'Switch to fix/replay-reliability before updating.' }
    git fetch upstream --tags
    if ($LASTEXITCODE -ne 0) { throw 'Upstream fetch failed.' }
    git rev-parse --verify $UpstreamRef | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unknown upstream ref.' }
    $backup = 'backup/replay-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
    git branch $backup
    if ($LASTEXITCODE -ne 0) { throw 'Could not preserve the previous branch.' }
    git rebase $UpstreamRef
    if ($LASTEXITCODE -ne 0) {
        git rebase --abort
        throw "Patch conflict. Rebase aborted; inspect upstream changes. Previous state: $backup"
    }
    & (Join-Path $PSScriptRoot 'LocalRepairs\Test-Source.ps1')
    Write-Output "Source patches verified. Previous state: $backup. Nothing deployed or pushed."
} finally { Pop-Location }
