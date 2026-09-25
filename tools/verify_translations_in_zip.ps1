# Verify Hearthwife Thunderstore/GitHub zip contains Translations/
# Usage: powershell -File tools\verify_translations_in_zip.ps1 [path-to.zip]
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = if ($args.Count -ge 1) { $args[0] } else {
  $dist = Join-Path (Split-Path $PSScriptRoot -Parent) "dist"
  $latest = Get-ChildItem $dist -Filter "blackhearthx-Hearthwife-*.zip" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike "_verify*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
  if ($latest) { $latest.FullName } else { Join-Path $dist "blackhearthx-Hearthwife-1.0.3.zip" }
}

if (-not (Test-Path $zip)) {
  throw "Zip not found: $zip"
}

Write-Host "Checking: $zip"
$a = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $zip))
try {
  $names = @($a.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
  $tr = @($names | Where-Object { $_ -like 'Translations/*/hearthwife.json' })
  Write-Host "Total zip entries: $($names.Count)"
  Write-Host "Translations/*/hearthwife.json: $($tr.Count) (need 17)"
  $tr | Sort-Object | ForEach-Object { Write-Host "  OK $_" }

  if ($tr.Count -lt 17) {
    throw "FAIL: expected 17 language files, found $($tr.Count)"
  }
  if ($names -contains 'hearthwife.json') {
    throw "FAIL: loose hearthwife.json at zip root"
  }

  # Extract to temp and confirm real folders on disk
  $tmp = Join-Path $env:TEMP ("hearthwife_tr_verify_" + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $tmp | Out-Null
  try {
    [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $zip), $tmp)
    $dir = Join-Path $tmp "Translations"
    if (-not (Test-Path $dir)) { throw "FAIL: Translations folder missing after extract" }
    $langs = @(Get-ChildItem $dir -Directory)
    Write-Host "After extract: $($langs.Count) language folders under Translations/"
    $langs | ForEach-Object { Write-Host "  DIR $($_.Name)" }
    if ($langs.Count -lt 17) { throw "FAIL: extracted folder count $($langs.Count)" }

    $dll = Join-Path $tmp "Hearthwife.dll"
    $asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dll))
    $emb = @($asm.GetManifestResourceNames() | Where-Object { $_ -like '*Translations*hearthwife.json' })
    Write-Host "Embedded in DLL: $($emb.Count) (need 17)"
    if ($emb.Count -lt 17) { throw "FAIL: DLL missing embedded translations" }
  }
  finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
  }

  Write-Host ""
  Write-Host "PASS: package ships Translations/ (17 langs) + embedded DLL resources."
}
finally {
  $a.Dispose()
}
