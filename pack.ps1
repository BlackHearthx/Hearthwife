# Pack Thunderstore zip (Hearthwife)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist\thunderstore"
$dll = Join-Path $root "bin\Release\Hearthwife.dll"
$manifest = Get-Content (Join-Path $root "manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number
$zip = Join-Path $root "dist\blackhearthx-Hearthwife-$version.zip"

if (-not (Test-Path $dll)) {
  throw "Build the project first: dotnet build -c Release"
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dist "docs\tutorial") | Out-Null

Copy-Item (Join-Path $root "icon.png") (Join-Path $dist "icon.png") -Force
Copy-Item (Join-Path $root "README.md") (Join-Path $dist "README.md") -Force
Copy-Item (Join-Path $root "CHANGELOG.md") (Join-Path $dist "CHANGELOG.md") -Force
Copy-Item (Join-Path $root "manifest.json") (Join-Path $dist "manifest.json") -Force
Copy-Item $dll (Join-Path $dist "Hearthwife.dll") -Force

Copy-Item (Join-Path $root "docs\tutorial\*") (Join-Path $dist "docs\tutorial") -Force

$locSrc = Join-Path $root "Translations"
if (Test-Path $locSrc) {
  Copy-Item $locSrc (Join-Path $dist "Translations") -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $root "dist") | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist "*") -DestinationPath $zip -Force
Write-Host "Ready: $zip"
Get-Item $zip | Format-List FullName, Length, LastWriteTime
