param(
    [string]$DotnetExe = "C:\Program Files\dotnet\dotnet.exe",
    [string]$PfxPath = $env:DOOM_SIGN_PFX,
    [string]$PfxPassword = $env:DOOM_SIGN_PFX_PASSWORD,
    [string]$Thumbprint = $env:DOOM_SIGN_THUMBPRINT
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repoRoot "artifacts\publish"
$appOut = Join-Path $publishRoot "app"
$hostOut = Join-Path $appOut "BrowserHost"
$extOut = Join-Path $appOut "BrowserExtension"
$installerOut = Join-Path $repoRoot "artifacts\installer"
$signScript = Join-Path $PSScriptRoot "Sign-Artifacts.ps1"
$publicCertOut = Join-Path $installerOut "DOOMIsland-CodeSigning.cer"

if (Test-Path $publishRoot) { Remove-Item -Path $publishRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $appOut, $hostOut, $extOut, $installerOut | Out-Null

Write-Host "1/6 Publishing NOVAIsland.App (Release, win-x64, self-contained)..."
& $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.App\NOVAIsland.App.csproj") `
    -c Release -r win-x64 -p:Platform=x64 --self-contained true -o $appOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "2/6 Publishing NOVAIsland.BrowserHost (Release, win-x64, self-contained)..."
& $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.BrowserHost\NOVAIsland.BrowserHost.csproj") `
    -c Release -r win-x64 --self-contained true -o $hostOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "3/6 Signing published application executables..."
& $signScript `
    -TargetPaths @((Join-Path $appOut "DOOMIsland.exe"), (Join-Path $hostOut "NOVAIsland.BrowserHost.exe")) `
    -PfxPath $PfxPath -PfxPassword $PfxPassword -Thumbprint $Thumbprint -PublicCertOut $publicCertOut

Write-Host "4/6 Packaging Browser Extension (MV3) & Portable Bundle..."
Copy-Item -Path (Join-Path $repoRoot "src\NOVAIsland.BrowserExtension\*") -Destination $extOut -Recurse -Force
$extZip = Join-Path $installerOut "DOOMIsland-BrowserExtension-MV3.zip"
if (Test-Path $extZip) { Remove-Item $extZip -Force }
Compress-Archive -Path (Join-Path $extOut "*") -DestinationPath $extZip

Copy-Item -Path (Join-Path $PSScriptRoot "Install-DOOMIsland.ps1") -Destination $publishRoot -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-DOOMIsland.ps1") -Destination $publishRoot -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-DOOMIsland.ps1") -Destination $appOut -Force

$bundleZip = Join-Path $installerOut "DOOMIsland-Win64-Portable-v2.4.0.zip"
if (Test-Path $bundleZip) { Remove-Item $bundleZip -Force }
Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $bundleZip

Write-Host "5/6 Building embedded payload & publishing standalone DOOMIsland-Setup-x64-v2.4.0.exe..."
$payloadZip = Join-Path $publishRoot "DOOMIsland-AppPayload.zip"
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $payloadZip -CompressionLevel Optimal

$setupBuildDir = Join-Path $publishRoot "setup_bin"
& $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.Setup\NOVAIsland.Setup.csproj") `
    -c Release -r win-x64 --self-contained true -o $setupBuildDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setupExeSrc = Join-Path $setupBuildDir "DOOMIsland-Setup-x64-v2.4.0.exe"
$setupExeDst = Join-Path $installerOut "DOOMIsland-Setup-x64-v2.4.0.exe"
Copy-Item -Path $setupExeSrc -Destination $setupExeDst -Force

Write-Host "6/6 Signing DOOMIsland-Setup-x64-v2.4.0.exe..."
& $signScript `
    -TargetPaths @($setupExeDst) `
    -PfxPath $PfxPath -PfxPassword $PfxPassword -Thumbprint $Thumbprint -PublicCertOut $publicCertOut

$iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if ($iscc) {
    Write-Host "Optional: Building & signing Inno Setup EXE installer..."
    & $iscc.Source (Join-Path $PSScriptRoot "DOOMIsland.iss")
    $innoExe = Join-Path $installerOut "DOOMIsland-Setup-Inno-x64-v2.4.0.exe"
    if (Test-Path $innoExe) {
        & $signScript -TargetPaths @($innoExe) -PfxPath $PfxPath -PfxPassword $PfxPassword -Thumbprint $Thumbprint
    }
}

Get-ChildItem $installerOut | Select-Object Name, Length
