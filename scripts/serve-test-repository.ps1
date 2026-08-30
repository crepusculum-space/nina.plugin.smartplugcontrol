<#
.SYNOPSIS
Serves a local, single-plugin fake NINA plugin repository so an install-from-repository
can be tested end-to-end in a real NINA instance before submitting/updating a manifest
PR to isbeorn/nina.plugin.manifests.

.DESCRIPTION
NINA's Plugin Manager (see NINA.Plugin/PluginFetcher.cs in github.com/isbeorn/nina) queries
every repository URL configured in Options > Plugins with a plain "GET {url}/plugins/manifests"
and expects a JSON array of manifest objects - the same shape as the files in
isbeorn/nina.plugin.manifests. It does not care whether that URL is the real
nighttime-imaging.eu feed or something else, so this script stands up exactly that endpoint
on localhost, backed by a release zip already built by package-release.ps1.

Before serving, it also validates the zip's own structure - files must sit at the zip root
(NINA extracts an ARCHIVE installer directly into "<PluginsDir>\<Plugin Display Name>\" with
no extra subfolder of its own) - this is the exact bug that shipped in v0.0.0.1-v0.0.0.4, see
CHANGELOG.md v0.0.0.5.

.PARAMETER Version
The version whose zip to serve, e.g. "0.0.0.5". Must match an existing
artifacts\Crepusculum.NINA.SmartPlugControl-v<Version>.zip (built by package-release.ps1 first).

.PARAMETER Port
Local port to listen on. Default 8420.

.EXAMPLE
.\scripts\package-release.ps1 -Version 0.0.0.6
.\scripts\serve-test-repository.ps1 -Version 0.0.0.6
# Then in NINA: Options > Plugins > Add Repository > http://localhost:8420
# The plugin should show up as an available install/update there.
# Ctrl+C here to stop the server once done testing.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [int]$Port = 8420
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$zipName = "Crepusculum.NINA.SmartPlugControl-v$Version.zip"
$zipPath = Join-Path $repoRoot "artifacts\$zipName"
$templatePath = Join-Path $PSScriptRoot "manifest-template.json"

if (-not (Test-Path $zipPath)) {
    throw "Zip not found: $zipPath - run scripts\package-release.ps1 -Version $Version first."
}

# ---- Validate zip structure (files must be at the zip root, no wrapping folder) ----
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $topLevelDirs = $archive.Entries |
        Where-Object { $_.FullName -match '/' } |
        ForEach-Object { $_.FullName.Split('/')[0] } |
        Select-Object -Unique
    if ($topLevelDirs.Count -gt 0) {
        throw "Zip validation FAILED: found wrapping folder(s) [$($topLevelDirs -join ', ')] - files must sit at the zip root. This is the exact bug fixed in v0.0.0.5, see CHANGELOG.md."
    }
    $dllEntry = $archive.Entries | Where-Object { $_.Name -eq "Crepusculum.NINA.SmartPlugControl.dll" }
    if (-not $dllEntry) {
        throw "Zip validation FAILED: Crepusculum.NINA.SmartPlugControl.dll not found at the zip root."
    }
    Write-Host "Zip structure OK: $($archive.Entries.Count) file(s), all at root."
}
finally {
    $archive.Dispose()
}

# ---- Build the manifest JSON pointing at this local server ----
$buildNumber = ($Version -split '\.')[-1]
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
$installerUrl = "http://localhost:$Port/$zipName"

$manifestJson = Get-Content -Path $templatePath -Raw
$manifestJson = $manifestJson.Replace('__BUILD__', $buildNumber)
$manifestJson = $manifestJson.Replace('__INSTALLER_URL__', $installerUrl)
$manifestJson = $manifestJson.Replace('__CHECKSUM__', $hash)
$manifestArrayJson = "[$manifestJson]"

Write-Host "Serving manifest for version $Version, checksum $hash"

# ---- Minimal HTTP server: GET /plugins/manifests and GET /<zip> ----
$prefix = "http://localhost:$Port/"
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($prefix)
$listener.Start()

Write-Host ""
Write-Host "Test repository running at $prefix"
Write-Host "In NINA: Options > Plugins > Add Repository > $($prefix.TrimEnd('/'))"
Write-Host "Press Ctrl+C to stop."
Write-Host ""

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $request = $context.Request
        $response = $context.Response
        try {
            if ($request.HttpMethod -eq "GET" -and $request.Url.AbsolutePath -eq "/plugins/manifests") {
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($manifestArrayJson)
                $response.ContentType = "application/json"
                $response.ContentLength64 = $bytes.Length
                $response.OutputStream.Write($bytes, 0, $bytes.Length)
                Write-Host "$(Get-Date -Format 'HH:mm:ss')  GET /plugins/manifests -> 200"
            }
            elseif ($request.HttpMethod -eq "GET" -and $request.Url.AbsolutePath -eq "/$zipName") {
                $bytes = [System.IO.File]::ReadAllBytes($zipPath)
                $response.ContentType = "application/octet-stream"
                $response.ContentLength64 = $bytes.Length
                $response.OutputStream.Write($bytes, 0, $bytes.Length)
                Write-Host "$(Get-Date -Format 'HH:mm:ss')  GET /$zipName -> 200 ($($bytes.Length) bytes)"
            }
            else {
                $response.StatusCode = 404
                Write-Host "$(Get-Date -Format 'HH:mm:ss')  GET $($request.Url.AbsolutePath) -> 404"
            }
        }
        finally {
            $response.OutputStream.Close()
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
