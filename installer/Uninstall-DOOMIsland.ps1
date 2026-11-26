param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\DOOMIsland",
    [switch]$RemoveUserData
)

$ErrorActionPreference = "Stop"

Get-Process -Name "NOVAIsland.App","NOVAIsland.BrowserHost" -ErrorAction SilentlyContinue | Stop-Process -Force

$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\DOOM Island.lnk"
Remove-Item -Path $shortcutPath -Force -ErrorAction SilentlyContinue
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "DOOM Island.lnk"
Remove-Item -Path $desktopShortcut -Force -ErrorAction SilentlyContinue

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "DOOMIsland" -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DOOMIsland" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.doomisland.host" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.doomisland.host" -Force -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
}

if ($RemoveUserData) {
    $dataDir = Join-Path $env:LOCALAPPDATA "DOOMIsland"
    if (Test-Path $dataDir) {
        Remove-Item -Path $dataDir -Recurse -Force
    }
}

Write-Host "DOOM Island uninstalled cleanly."
