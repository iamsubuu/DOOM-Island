param(
    [string]$ExtensionId = "amplhmapoeandecddkchambamhafdcgp",

    [string]$HostExePath = "$PSScriptRoot\..\bin\Debug\net10.0-windows10.0.19041.0\NOVAIsland.BrowserHost.exe"
)

$resolvedExe = (Resolve-Path $HostExePath).Path
$manifestDir = Join-Path $env:LOCALAPPDATA "DOOMIsland\NativeMessaging"
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null

$manifest = @{
    name            = "com.doomisland.host"
    description     = "DOOM Island Native Messaging Host for browser media detection and control"
    path            = $resolvedExe
    type            = "stdio"
    allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 4

$manifestPath = Join-Path $manifestDir "com.doomisland.host.json"
$manifest | Set-Content -Path $manifestPath -Encoding UTF8

$regPaths = @(
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.doomisland.host",
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.doomisland.host"
)

foreach ($reg in $regPaths) {
    New-Item -Path $reg -Force | Out-Null
    Set-ItemProperty -Path $reg -Name "(Default)" -Value $manifestPath
}

Write-Host "Registered com.doomisland.host -> $manifestPath (Exe: $resolvedExe)"
