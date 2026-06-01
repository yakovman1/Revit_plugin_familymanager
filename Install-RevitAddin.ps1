# Копирует FamilyMang в папку Revit Addins (2022) с полным путём в .addin
param(
    [string]$RevitYear = "2022",
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$projectDir = Join-Path $root "FamilyMang"
$binDir = Join-Path $projectDir "bin\$Configuration"
$dll = Join-Path $binDir "FamilyMang.dll"
$logoSrc = Join-Path $binDir "Assets\AtpTlpLogo.png"

if (-not (Test-Path $dll)) {
    Write-Error "Сначала соберите проект: $dll не найден."
}

$deployDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear\FamilyMang"
$assetsDir = Join-Path $deployDir "Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

Copy-Item -Force $dll (Join-Path $deployDir "FamilyMang.dll")
if (Test-Path $logoSrc) {
    Copy-Item -Force $logoSrc (Join-Path $assetsDir "AtpTlpLogo.png")
} else {
    Write-Warning "Логотип не найден: $logoSrc"
}

$dllDeploy = Join-Path $deployDir "FamilyMang.dll"
$addinPath = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear\FamilyMang.addin"

$addinXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>FamilyMang</Name>
    <Assembly>$dllDeploy</Assembly>
    <FullClassName>FamilyMang.App</FullClassName>
    <AddInId>03CB6832-D933-44CD-AD29-3933D5A934CE</AddInId>
    <VendorId>FamilyMang</VendorId>
    <VendorDescription>FamilyMang Plugin</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -Path $addinPath -Value $addinXml -Encoding UTF8

Write-Host "Установлено:"
Write-Host "  DLL:   $dllDeploy"
Write-Host "  Addin: $addinPath"
Write-Host "Перезапустите Revit."
