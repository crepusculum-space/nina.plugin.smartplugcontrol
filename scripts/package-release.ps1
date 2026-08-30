<#
.SYNOPSIS
Builds the plugin in Release config and packages the output into a release zip
NINA's plugin installer can extract directly.

.DESCRIPTION
NINA's plugin manager extracts the "ARCHIVE" installer zip directly into
"<PluginsDir>\<Plugin Display Name>\" (e.g. "...\Plugins\3.0.0\Smart Plug Control\").
It does NOT create an extra subfolder for the plugin - so the zip's files must sit
at the zip root, not inside a wrapping folder named after the assembly
(Crepusculum.NINA.SmartPlugControl). Zipping the raw bin\Release output folder
(or using Windows Explorer's "Send to > Compressed folder" on it) produces a zip
with that wrapping folder, which is the bug this script exists to avoid - see
CHANGELOG.md v0.0.0.5 and CLAUDE.md.

.PARAMETER Version
The plugin version (matches AssemblyInfo.cs's AssemblyVersion), e.g. "0.0.0.5".
Used only to name the output zip; does not modify any source file.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "SmartPlugControl\SmartPlugControl.csproj"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"

Write-Host "Building Release..."
& $dotnet build $csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$targetDir = Join-Path $repoRoot "SmartPlugControl\bin\Release\net8.0-windows"
if (-not (Test-Path $targetDir)) { throw "Build output not found at $targetDir" }

$stagingDir = Join-Path $repoRoot "artifacts\release-staging"
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir | Out-Null

# Only what NINA actually needs to load the plugin - exclude dev-only artifacts
# (.pdb debug symbols are fine to keep out of the public zip; deps/runtimeconfig
# json files are needed at runtime for a Class Library host).
$includePatterns = @("*.dll", "*.dll.config", "*.deps.json", "*.runtimeconfig.json", "LICENSE", "THIRD-PARTY-NOTICES.md")
foreach ($pattern in $includePatterns) {
    Get-ChildItem -Path $targetDir -Filter $pattern -File | Copy-Item -Destination $stagingDir
}

$outDir = Join-Path $repoRoot "artifacts"
$zipName = "Crepusculum.NINA.SmartPlugControl-v$Version.zip"
$zipPath = Join-Path $outDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Compress the CONTENTS of stagingDir (trailing \*) so files land at the zip root,
# not inside a "release-staging" wrapping folder.
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash

Write-Host ""
Write-Host "Package created: $zipPath"
Write-Host "SHA256: $hash"
Write-Host ""
Write-Host "Zip contents:"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$archive.Entries | ForEach-Object { Write-Host "  $($_.FullName)" }

# NINA extracts an ARCHIVE installer directly into "<PluginsDir>\<Plugin Display Name>\" with
# no extra subfolder of its own - a wrapping folder here silently breaks installs from the
# plugin repository (shipped undetected in v0.0.0.1-v0.0.0.4, see CHANGELOG.md v0.0.0.5). Fail
# loudly instead of shipping that again.
$topLevelDirs = $archive.Entries |
    Where-Object { $_.FullName -match '/' } |
    ForEach-Object { $_.FullName.Split('/')[0] } |
    Select-Object -Unique
$archive.Dispose()
if ($topLevelDirs.Count -gt 0) {
    throw "Zip structure INVALID: found wrapping folder(s) [$($topLevelDirs -join ', ')] - files must sit at the zip root, not nested. Fix before releasing."
}
Write-Host "Zip structure OK: files are at the root, no wrapping folder."
Write-Host ""
Write-Host "Next: .\scripts\serve-test-repository.ps1 -Version $Version, then in NINA add"
Write-Host "http://localhost:8420 as a plugin repository (Options > Plugins) to verify the"
Write-Host "install works end-to-end before submitting/updating the manifest PR."
