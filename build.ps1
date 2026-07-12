param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Sailwind"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$PluginDir = Join-Path $GameDir "BepInEx\plugins\Routier"
$DataDir = Join-Path $PluginDir "data"
$X64Dir = Join-Path $PluginDir "x64"

dotnet build (Join-Path $Root "Routier.csproj") -c Release

New-Item -ItemType Directory -Force -Path $PluginDir, $DataDir, $X64Dir | Out-Null

$ReleaseDir = Join-Path $Root "bin\Release\netstandard2.0"
$NugetRoot = ((dotnet nuget locals global-packages --list) -split ':', 2)[1].Trim()
$InteropCandidates = @(
    (Join-Path $ReleaseDir "x64\SQLite.Interop.dll"),
    (Join-Path $NugetRoot "stub.system.data.sqlite.core.netstandard\1.0.118\runtimes\win-x64\native\SQLite.Interop.dll")
)

Get-ChildItem $PluginDir -Filter "*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item -Force $_.FullName -ErrorAction SilentlyContinue
}
Get-ChildItem $X64Dir -Filter "*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item -Force $_.FullName -ErrorAction SilentlyContinue
}
Remove-Item (Join-Path $PluginDir "e_sqlite3.dll") -ErrorAction SilentlyContinue

Copy-Item -Force (Join-Path $ReleaseDir "Routier.dll") $PluginDir
Copy-Item -Force (Join-Path $ReleaseDir "System.Data.SQLite.dll") $PluginDir

$Interop = $InteropCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($Interop) {
    Copy-Item -Force $Interop $X64Dir
} else {
    Write-Warning "SQLite.Interop.dll not found"
}

python -c "import sys; sys.path.insert(0, r'$Root\web'); from pathlib import Path; from database import ensure_database; ensure_database(Path(r'$DataDir\routier.db'))"

Write-Host "Installed to $PluginDir"
Write-Host "Database: $DataDir\routier.db (empty until in-game snapshots)"
Write-Host "Web UI:   python web\server.py  (from mod folder)"
