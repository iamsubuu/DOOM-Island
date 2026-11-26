param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\DOOMIsland",
    [string]$ExtensionId = "amplhmapoeandecddkchambamhafdcgp"
)

$ErrorActionPreference = "Stop"
$sourceRoot = $PSScriptRoot

Write-Host "Installing DOOM Island to $InstallDir..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $sourceRoot "app\*") -Destination $InstallDir -Recurse -Force
Copy-Item -Path (Join-Path $sourceRoot "Uninstall-DOOMIsland.ps1") -Destination $InstallDir -Force

# Create Start Menu shortcut
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenu "DOOM Island.lnk"
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut($shortcutPath)
$sc.TargetPath = Join-Path $InstallDir "DOOMIsland.exe"
$sc.WorkingDirectory = $InstallDir
$sc.Description = "DOOM Island — Native Windows Dynamic Island"
$sc.Save()

# Register Native Messaging Host if ExtensionId provided
$hostExe = Join-Path $InstallDir "BrowserHost\NOVAIsland.BrowserHost.exe"
$manifestDir = Join-Path $env:LOCALAPPDATA "DOOMIsland\NativeMessaging"
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null

$originEntry = if ([string]::IsNullOrWhiteSpace($ExtensionId)) {
    "chrome-extension://REPLACE_WITH_YOUR_EXTENSION_ID/"
} else {
    "chrome-extension://$ExtensionId/"
}

$manifest = @{
    name            = "com.doomisland.host"
    description     = "DOOM Island Native Messaging Host for browser media detection and control"
    path            = $hostExe
    type            = "stdio"
    allowed_origins = @($originEntry)
} | ConvertTo-Json -Depth 4

$manifestPath = Join-Path $manifestDir "com.doomisland.host.json"
$manifest | Set-Content -Path $manifestPath -Encoding UTF8

foreach ($reg in @(
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.doomisland.host",
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.doomisland.host"
)) {
    New-Item -Path $reg -Force | Out-Null
    Set-ItemProperty -Path $reg -Name "(Default)" -Value $manifestPath
}

Write-Host "DOOM Island installed successfully."
Write-Host "Start Menu shortcut: $shortcutPath"
Write-Host "Browser Extension folder: $(Join-Path $InstallDir 'BrowserExtension')"
