param(
    [string]$MsixPath = "",
    [string]$CertPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($MsixPath)) {
    $found = Get-ChildItem -Path $PSScriptRoot -Filter "DOOMIsland-x64-v*.msix" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $found) {
        throw "No DOOMIsland MSIX package found in $PSScriptRoot"
    }
    $MsixPath = $found.FullName
}

if ([string]::IsNullOrWhiteSpace($CertPath)) {
    $CertPath = Join-Path $PSScriptRoot "DOOMIsland-CodeSigning.cer"
}

if (-not (Test-Path $MsixPath)) { throw "MSIX not found: $MsixPath" }

if (Test-Path $CertPath) {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertPath)
    $trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople, Cert:\LocalMachine\Root -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $cert.Thumbprint } |
        Select-Object -First 1

    if (-not $trusted) {
        $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)

        if ($isAdmin) {
            Import-Certificate -FilePath $CertPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
            Write-Host "Trusted MSIX certificate $($cert.Thumbprint) in Cert:\LocalMachine\TrustedPeople."
        }
        else {
            Write-Host "Requesting elevation once to trust $CertPath in LocalMachine\TrustedPeople..."
            $cmd = "Import-Certificate -FilePath '$CertPath' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null"
            $proc = Start-Process powershell -Verb RunAs -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $cmd) -Wait -PassThru
            if ($proc.ExitCode -ne 0) {
                throw "Failed to import MSIX signing certificate (exit $($proc.ExitCode))."
            }
        }
    }
}

Write-Host "Installing $(Split-Path -Leaf $MsixPath)..."
Add-AppxPackage -Path $MsixPath
Write-Host "DOOM Island MSIX installed successfully."
