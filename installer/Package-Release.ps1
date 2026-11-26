param(
    [string]$DotnetExe = "C:\Program Files\dotnet\dotnet.exe"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repoRoot "artifacts\publish"
$appOut = Join-Path $publishRoot "app"
$hostOut = Join-Path $appOut "BrowserHost"
$extOut = Join-Path $appOut "BrowserExtension"
$installerOut = Join-Path $repoRoot "artifacts\installer"

if (Test-Path $publishRoot) { Remove-Item -Path $publishRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $appOut, $hostOut, $extOut, $installerOut | Out-Null

Write-Host "1/5 Publishing NOVAIsland.App (Release, win-x64, self-contained)..."
& $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.App\NOVAIsland.App.csproj") `
    -c Release -r win-x64 -p:Platform=x64 --self-contained true -o $appOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "2/5 Publishing NOVAIsland.BrowserHost (Release, win-x64, self-contained)..."
& $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.BrowserHost\NOVAIsland.BrowserHost.csproj") `
    -c Release -r win-x64 --self-contained true -o $hostOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "3/5 Packaging Browser Extension (MV3)..."
Copy-Item -Path (Join-Path $repoRoot "src\NOVAIsland.BrowserExtension\*") -Destination $extOut -Recurse -Force
$extZip = Join-Path $installerOut "DOOMIsland-BrowserExtension-MV3.zip"
if (Test-Path $extZip) { Remove-Item $extZip -Force }
Compress-Archive -Path (Join-Path $extOut "*") -DestinationPath $extZip

Copy-Item -Path (Join-Path $PSScriptRoot "Install-DOOMIsland.ps1") -Destination $publishRoot -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-DOOMIsland.ps1") -Destination $publishRoot -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-DOOMIsland.ps1") -Destination $appOut -Force

$bundleZip = Join-Path $installerOut "DOOMIsland-Win64-Portable-v1.0.0.zip"
if (Test-Path $bundleZip) { Remove-Item $bundleZip -Force }
Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $bundleZip

Write-Host "4/5 Building embedded payload for Setup EXE..."
$payloadZip = Join-Path $publishRoot "DOOMIsland-AppPayload.zip"
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $payloadZip -CompressionLevel Optimal

Write-Host "5/5 Publishing standalone DOOMIsland-Setup-x64-v1.0.0.exe..."
$setupBuildDir = Join-Path $publishRoot "setup_bin"
& $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.Setup\NOVAIsland.Setup.csproj") `
    -c Release -r win-x64 --self-contained true -o $setupBuildDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setupExeSrc = Join-Path $setupBuildDir "DOOMIsland-Setup-x64-v1.0.0.exe"
$setupExeDst = Join-Path $installerOut "DOOMIsland-Setup-x64-v1.0.0.exe"
Copy-Item -Path $setupExeSrc -Destination $setupExeDst -Force

$iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if ($iscc) {
    Write-Host "Optional: Building Inno Setup EXE installer..."
    & $iscc.Source (Join-Path $PSScriptRoot "DOOMIsland.iss")
}

Get-ChildItem $installerOut | Select-Object Name, Length
