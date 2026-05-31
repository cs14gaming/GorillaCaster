# Build the obfuscated DLL and zip a ready-to-upload Thunderstore package into dist/.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$ver = ([regex]'"version_number"\s*:\s*"([^"]+)"').Match((Get-Content "$root\manifest.json" -Raw)).Groups[1].Value
Write-Host "Packaging GorillaCaster v$ver for Thunderstore..." -ForegroundColor Cyan

dotnet build -c Release -v minimal
obfuscar.console obfuscar.xml

$staging = Join-Path $root "dist\staging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

# Thunderstore requires manifest.json, icon.png and README.md at the ZIP ROOT.
Copy-Item "$root\manifest.json" $staging
Copy-Item "$root\icon.png"      $staging
Copy-Item "$root\README.md"     $staging
if (Test-Path "$root\LICENSE") { Copy-Item "$root\LICENSE" "$staging\LICENSE.txt" }
Copy-Item "$root\bin\Obf\GorillaCaster.dll" $staging   # obfuscated plugin

$zip = Join-Path $root "dist\GorillaCaster-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$staging\*" -DestinationPath $zip
Remove-Item $staging -Recurse -Force

Write-Host "Thunderstore package -> $zip" -ForegroundColor Green
Write-Host "Upload it at https://thunderstore.io/c/gorilla-tag/create/" -ForegroundColor Green
