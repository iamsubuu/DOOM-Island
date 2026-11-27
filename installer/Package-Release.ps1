param(
    [string]$DotnetExe = "C:\Program Files\dotnet\dotnet.exe",
    [ValidateSet("All", "Exe", "Msix")]
    [string]$Format = "All",
    [string]$Version = "",
    [switch]$StorePackage,
    [string]$PackageName = "",
    [string]$PackagePublisher = "",
    [string]$PublisherDisplayName = "",
    [string]$PfxPath = $env:DOOM_SIGN_PFX,
    [string]$PfxPassword = $env:DOOM_SIGN_PFX_PASSWORD,
    [string]$Thumbprint = $env:DOOM_SIGN_THUMBPRINT
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    $constantsPath = Join-Path $repoRoot "src\NOVAIsland.Core\Configuration\AppConstants.cs"
    $constantsRaw = Get-Content -Path $constantsPath -Raw
    if ($constantsRaw -notmatch 'AppVersion\s*=\s*"([^"]+)"') {
        throw "Could not parse AppVersion from $constantsPath"
    }
    $appVersion = $Matches[1]
}
else {
    $appVersion = $Version.Trim()
}
$msixVersion = if ($appVersion -match '^\d+\.\d+\.\d+\.\d+$') { $appVersion } else { "$appVersion.0" }

$publishRoot = Join-Path $repoRoot "artifacts\publish"
$appOut = Join-Path $publishRoot "app"
$hostStage = Join-Path $publishRoot "browser_host"
$msixOut = Join-Path $publishRoot "msix_stage"
$installerOut = Join-Path $repoRoot "artifacts\installer"
$signScript = Join-Path $PSScriptRoot "Sign-Artifacts.ps1"
$publicCertOut = Join-Path $installerOut "DOOMIsland-CodeSigning.cer"

function Find-MakeAppxExe {
    $cmd = Get-Command "makeappx.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @()
    $candidates += Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue
    $candidates += Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue

    $best = $candidates | Sort-Object FullName -Descending | Select-Object -First 1
    if ($best) { return $best.FullName }
    throw "makeappx.exe not found. Restore NuGet packages (Microsoft.Windows.SDK.BuildTools) or install Windows 10/11 SDK."
}

function Reset-StageDir {
    param([string]$Path)

    if (Test-Path $Path) {
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                if ($attempt -eq 5) { throw }
                Start-Sleep -Milliseconds 500
            }
        }
    }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

New-Item -ItemType Directory -Force -Path $installerOut | Out-Null
Reset-StageDir -Path $hostStage

if ($Format -in @("All", "Exe")) {
    Reset-StageDir -Path $appOut
}
if ($Format -in @("All", "Msix")) {
    Reset-StageDir -Path $msixOut
}

Write-Host "DOOM Island Release Packager v$appVersion (Format=$Format)"

Write-Host "1/7 Publishing & signing shared NOVAIsland.BrowserHost (Release, win-x64, self-contained)..."
& $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.BrowserHost\NOVAIsland.BrowserHost.csproj") `
    -c Release -r win-x64 -p:Version=$appVersion --self-contained true -o $hostStage
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$signedHostExe = Join-Path $hostStage "NOVAIsland.BrowserHost.exe"
& $signScript `
    -TargetPaths @($signedHostExe) `
    -PfxPath $PfxPath -PfxPassword $PfxPassword -Thumbprint $Thumbprint -PublicCertOut $publicCertOut

$certSubject = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($publicCertOut)).Subject

Write-Host "2/7 Packaging Browser Extension (MV3)..."
$extZip = Join-Path $installerOut "DOOMIsland-BrowserExtension-MV3.zip"
if (Test-Path $extZip) { Remove-Item $extZip -Force }
Compress-Archive -Path (Join-Path $repoRoot "src\NOVAIsland.BrowserExtension\*") -DestinationPath $extZip

if ($Format -in @("All", "Exe")) {
    Write-Host "3/7 Publishing & signing unpackaged NOVAIsland.App (WindowsPackageType=None, win-x64, self-contained)..."
    & $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.App\NOVAIsland.App.csproj") `
        -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:Version=$appVersion --disable-build-servers --self-contained true -o $appOut
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $appHostDest = Join-Path $appOut "BrowserHost"
    $appExtDest = Join-Path $appOut "BrowserExtension"
    New-Item -ItemType Directory -Force -Path $appHostDest, $appExtDest | Out-Null
    Copy-Item -Path (Join-Path $hostStage "*") -Destination $appHostDest -Recurse -Force
    Copy-Item -Path (Join-Path $repoRoot "src\NOVAIsland.BrowserExtension\*") -Destination $appExtDest -Recurse -Force

    & $signScript `
        -TargetPaths @((Join-Path $appOut "DOOMIsland.exe")) `
        -PfxPath $PfxPath -PfxPassword $PfxPassword -Thumbprint $Thumbprint -PublicCertOut $publicCertOut

    Write-Host "4/7 Packaging Portable Bundle (DOOMIsland-Win64-Portable-v$appVersion.zip)..."
    $portableStage = Join-Path $publishRoot "portable_stage"
    $portableAppDest = Join-Path $portableStage "app"
    New-Item -ItemType Directory -Force -Path $portableAppDest | Out-Null
    Copy-Item -Path (Join-Path $appOut "*") -Destination $portableAppDest -Recurse -Force
    Copy-Item -Path (Join-Path $PSScriptRoot "Install-DOOMIsland.ps1") -Destination $portableStage -Force
    Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-DOOMIsland.ps1") -Destination $portableStage -Force
    Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-DOOMIsland.ps1") -Destination $appOut -Force

    $bundleZip = Join-Path $installerOut "DOOMIsland-Win64-Portable-v$appVersion.zip"
    if (Test-Path $bundleZip) { Remove-Item $bundleZip -Force }
    Compress-Archive -Path (Join-Path $portableStage "*") -DestinationPath $bundleZip

    Write-Host "5/7 Building & signing standalone DOOMIsland-Setup-x64-v$appVersion.exe..."
    $payloadZip = Join-Path $publishRoot "DOOMIsland-AppPayload.zip"
    if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
    Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $payloadZip -CompressionLevel Optimal

    $setupBuildDir = Join-Path $publishRoot "setup_bin"
    & $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.Setup\NOVAIsland.Setup.csproj") `
        -c Release -r win-x64 -p:Version=$appVersion --self-contained true -o $setupBuildDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $setupExeSrc = Join-Path $setupBuildDir "DOOMIsland-Setup-x64-v$appVersion.exe"
    $setupExeDst = Join-Path $installerOut "DOOMIsland-Setup-x64-v$appVersion.exe"
    Copy-Item -Path $setupExeSrc -Destination $setupExeDst -Force

    & $signScript `
        -TargetPaths @($setupExeDst) `
        -PfxPath $PfxPath -PfxPassword $PfxPassword -Thumbprint $Thumbprint -PublicCertOut $publicCertOut

    $iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($iscc) {
        Write-Host "Optional: Building & signing Inno Setup EXE installer..."
        & $iscc.Source "/DAppVersion=$appVersion" (Join-Path $PSScriptRoot "DOOMIsland.iss")
        $innoExe = Join-Path $installerOut "DOOMIsland-Setup-Inno-x64-v$appVersion.exe"
        if (Test-Path $innoExe) {
            & $signScript -TargetPaths @($innoExe) -PfxPath $PfxPath -PfxPassword $PfxPassword -Thumbprint $Thumbprint
        }
    }
}

if ($Format -in @("All", "Msix")) {
    Write-Host "6/7 Publishing & staging packaged NOVAIsland.App (WindowsPackageType=MSIX, win-x64, self-contained)..."
    & $DotnetExe publish (Join-Path $repoRoot "src\NOVAIsland.App\NOVAIsland.App.csproj") `
        -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=MSIX -p:AppxPackageSigningEnabled=false -p:Version=$appVersion --disable-build-servers --self-contained true -o $msixOut
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $generatedManifest = Get-ChildItem (Join-Path $repoRoot "src\NOVAIsland.App\bin\x64\Release") -Filter "AppxManifest.xml" -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 -ExpandProperty FullName
    if (-not $generatedManifest) {
        $generatedManifest = Join-Path $repoRoot "src\NOVAIsland.App\Package.appxmanifest"
    }

    $stagedManifest = Join-Path $msixOut "AppxManifest.xml"
    [xml]$manifestXml = Get-Content -Path $generatedManifest -Raw
    if (-not [string]::IsNullOrWhiteSpace($PackageName)) {
        $manifestXml.Package.Identity.Name = $PackageName.Trim()
    }
    if (-not [string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
        $manifestXml.Package.Properties.PublisherDisplayName = $PublisherDisplayName.Trim()
    }
    if (-not [string]::IsNullOrWhiteSpace($PackagePublisher)) {
        $manifestXml.Package.Identity.Publisher = $PackagePublisher.Trim()
    }
    elseif (-not $StorePackage) {
        $manifestXml.Package.Identity.Publisher = $certSubject
    }
    $manifestXml.Package.Identity.Version = $msixVersion
    $manifestXml.Save($stagedManifest)

    $msixHostDest = Join-Path $msixOut "BrowserHost"
    $msixExtDest = Join-Path $msixOut "BrowserExtension"
    New-Item -ItemType Directory -Force -Path $msixHostDest, $msixExtDest | Out-Null
    Copy-Item -Path (Join-Path $hostStage "*") -Destination $msixHostDest -Recurse -Force
    Copy-Item -Path (Join-Path $repoRoot "src\NOVAIsland.BrowserExtension\*") -Destination $msixExtDest -Recurse -Force

    & $signScript `
        -TargetPaths @((Join-Path $msixOut "DOOMIsland.exe")) `
        -PfxPath $PfxPath -PfxPassword $PfxPassword -Thumbprint $Thumbprint -PublicCertOut $publicCertOut

    Write-Host "7/7 Packing MSIX package (DOOMIsland-x64-v$appVersion.msix)..."
    $makeAppx = Find-MakeAppxExe
    $msixDst = Join-Path $installerOut "DOOMIsland-x64-v$appVersion.msix"
    if (Test-Path $msixDst) { Remove-Item $msixDst -Force }
    & $makeAppx pack /o /d $msixOut /p $msixDst
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $effectivePublisher = $manifestXml.Package.Identity.Publisher
    if ($StorePackage -or ($effectivePublisher -ne $certSubject)) {
        Write-Host "Skipping local MSIX signing for Store submission (Publisher=$effectivePublisher; Microsoft Store re-signs on ingestion)."
    }
    else {
        & $signScript `
            -TargetPaths @($msixDst) `
            -PfxPath $PfxPath -PfxPassword $PfxPassword -Thumbprint $Thumbprint -PublicCertOut $publicCertOut
        Copy-Item -Path (Join-Path $PSScriptRoot "Install-DOOMIsland-MSIX.ps1") -Destination $installerOut -Force
    }
}

Get-ChildItem $installerOut | Select-Object Name, Length
