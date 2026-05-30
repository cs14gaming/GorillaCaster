# Build -> obfuscate -> deploy the obfuscated plugin into BepInEx/plugins.
$ErrorActionPreference = "Stop"
$plugins = "D:\Steam\steamapps\common\Gorilla Tag\BepInEx\plugins\GorillaCaster"

Write-Host "Building..." -ForegroundColor Cyan
dotnet build -c Release -v minimal

Write-Host "Obfuscating..." -ForegroundColor Cyan
obfuscar.console obfuscar.xml

Write-Host "Deploying obfuscated DLL..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item "bin\Obf\GorillaCaster.dll" "$plugins\GorillaCaster.dll" -Force

Write-Host "Done -> $plugins\GorillaCaster.dll" -ForegroundColor Green
