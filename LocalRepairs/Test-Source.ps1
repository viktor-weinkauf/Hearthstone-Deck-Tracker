[CmdletBinding()]
param([string]$HdtPath = (Join-Path $env:LOCALAPPDATA 'HearthstoneDeckTracker\current'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
foreach ($name in @('HearthDb.dll','HearthMirror.dll')) {
    Copy-Item -LiteralPath (Join-Path $HdtPath $name) -Destination (Join-Path $repo 'lib')
}
& dotnet test (Join-Path $repo 'HearthWatcher.Test\HearthWatcher.Test.csproj') -c Release -p:Platform=x64 --nologo --filter FullyQualifiedName~RewindStartingPointTests
if ($LASTEXITCODE -ne 0) { throw 'Source Rewind regression tests failed.' }
