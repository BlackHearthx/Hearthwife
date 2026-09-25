# Pack Thunderstore zip (Hearthwife)
# Uses ZipArchive with forward-slash entry names so r2modman / Linux extractors keep Translations/ nested.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

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
if (-not (Test-Path $locSrc)) {
  throw "Translations folder missing at $locSrc - refuse to pack a broken localization package."
}
Copy-Item $locSrc (Join-Path $dist "Translations") -Recurse -Force

# Marker so install folders are obvious in Explorer / zip listings.
$marker = @(
  "Hearthwife Translations pack OK",
  "Language folders expected: 17",
  "Path after r2modman install:",
  "  BepInEx/plugins/BlackHearthx-Hearthwife/Translations/<Language>/hearthwife.json"
) -join "`r`n"
Set-Content -Path (Join-Path $dist "Translations\README_INSTALL.txt") -Value $marker -Encoding UTF8
Set-Content -Path (Join-Path $dist "TRANSLATIONS_SHIPPED.txt") -Value $marker -Encoding UTF8

$langDirs = @(Get-ChildItem (Join-Path $dist "Translations") -Directory -ErrorAction SilentlyContinue)
$langCount = $langDirs.Count
if ($langCount -lt 17) {
  throw "Expected at least 17 language folders under Translations/, found $langCount"
}
foreach ($d in $langDirs) {
  $jf = Join-Path $d.FullName "hearthwife.json"
  if (-not (Test-Path $jf)) {
    throw "Missing hearthwife.json in $($d.Name)"
  }
}

New-Item -ItemType Directory -Force -Path (Join-Path $root "dist") | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }

# Zip with Unix-style paths (forward slashes). Compress-Archive uses backslashes and can break extractors.
$zipArchive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
  Get-ChildItem $dist -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($dist.Length).TrimStart([char]'\', [char]'/').Replace([char]'\', [char]'/')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
      $zipArchive,
      $_.FullName,
      $rel,
      [System.IO.Compression.CompressionLevel]::Optimal)
  }
}
finally {
  $zipArchive.Dispose()
}

# Verify zip contents
$check = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
  $names = @($check.Entries | ForEach-Object { $_.FullName })
  $tr = @($names | Where-Object { $_ -like 'Translations/*/hearthwife.json' })
  if ($tr.Count -lt 17) {
    $joined = $names -join "`n"
    throw "Pack verify failed: zip has $($tr.Count) Translations/*/hearthwife.json entries (need 17+). Entries:`n$joined"
  }
  if ($names -contains 'hearthwife.json') {
    throw "Pack verify failed: loose hearthwife.json at zip root (should live under Translations/{Lang}/)."
  }
  Write-Host "Verified $($tr.Count) translation files in zip (forward-slash paths)."
}
finally {
  $check.Dispose()
}

Write-Host "Ready: $zip"
Get-Item $zip | Format-List FullName, Length, LastWriteTime
