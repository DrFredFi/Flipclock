<#
.SYNOPSIS
    Publishes a self-contained Release build of Flipclock.

.DESCRIPTION
    Runs the tests, then publishes the app as a portable folder that runs without installing
    .NET or the Windows App SDK. An existing config.toml in the output folder is kept, so
    republishing over an installed copy keeps its settings.

.PARAMETER Output
    Destination folder. Defaults to the "publish" folder at the repository root.

.PARAMETER Platform
    x64 (default) or ARM64.

.PARAMETER Clean
    Empties the output folder before publishing (config.toml is preserved).

.PARAMETER SkipTests
    Publishes without running the tests first.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Output D:\Tools\Flipclock -Clean
    .\publish.ps1 -Platform ARM64 -Output .\publish\arm64
#>
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot 'publish'),
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',
    [switch]$Clean,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src/Flipclock.App/Flipclock.App.csproj'
$tests = Join-Path $PSScriptRoot 'tests/Flipclock.Core.Tests'
$runtime = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)

function Invoke-Step([string]$Title, [scriptblock]$Command) {
    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Title failed (exit code $LASTEXITCODE)." }
}

# A running copy locks Flipclock.exe and its DLLs; fail early with a clear message.
$exe = Join-Path $Output 'Flipclock.exe'
$running = Get-Process Flipclock -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) { throw "Flipclock is running from $Output. Close it and run the script again." }

if (-not $SkipTests) {
    Invoke-Step 'Running tests' { dotnet test $tests -c Release --nologo }
}

if ($Clean -and (Test-Path $Output)) {
    Write-Host "==> Cleaning $Output (keeping config.toml)" -ForegroundColor Cyan
    Get-ChildItem $Output -Force | Where-Object { $_.Name -ne 'config.toml' } | Remove-Item -Recurse -Force
}

# Build into a scratch folder instead of bin\, so a copy running from bin\ does not block publishing.
$buildDir = Join-Path ([System.IO.Path]::GetTempPath()) "Flipclock-publish\$runtime\"
Invoke-Step "Publishing $runtime to $Output" {
    dotnet publish $project -c Release -p:Platform=$Platform -r $runtime -p:OutDir=$buildDir -o $Output --nologo
}

Write-Host "Done: $exe" -ForegroundColor Green
