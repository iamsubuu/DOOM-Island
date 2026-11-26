param(
    [Parameter(Mandatory = $true)]
    [string[]]$TargetPaths,

    [string]$PfxPath = $env:DOOM_SIGN_PFX,
    [string]$PfxPassword = $env:DOOM_SIGN_PFX_PASSWORD,
    [string]$Thumbprint = $env:DOOM_SIGN_THUMBPRINT,
    [string]$PublicCertOut = ""
)

$ErrorActionPreference = "Stop"

function Resolve-SigningCertificate {
    if (-not [string]::IsNullOrWhiteSpace($PfxPath) -and (Test-Path $PfxPath)) {
        Write-Host "Using PFX certificate: $PfxPath"
        if ([string]::IsNullOrEmpty($PfxPassword)) {
            return New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath)
        }
        $sec = ConvertTo-SecureString -String $PfxPassword -AsPlainText -Force
        return New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $sec)
    }

    if (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
        $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $Thumbprint } |
            Select-Object -First 1
        if ($cert) {
            Write-Host "Using certificate store thumbprint: $($cert.Thumbprint)"
            return $cert
        }
        throw "Code signing certificate with thumbprint '$Thumbprint' not found."
    }

    $existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq "CN=DOOM Island" -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $existing) {
        Write-Host "Creating local self-signed Authenticode certificate (CN=DOOM Island) in Cert:\CurrentUser\My..."
        $existing = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject "CN=DOOM Island" `
            -FriendlyName "DOOM Island Local Code Signing" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -NotAfter (Get-Date).AddYears(5)
    }
    else {
        Write-Host "Using existing local self-signed certificate: $($existing.Thumbprint)"
    }

    if (-not [string]::IsNullOrWhiteSpace($PublicCertOut)) {
        $certDir = Split-Path -Parent $PublicCertOut
        if (-not [string]::IsNullOrEmpty($certDir)) {
            New-Item -ItemType Directory -Force -Path $certDir | Out-Null
        }
        Export-Certificate -Cert $existing -FilePath $PublicCertOut -Force | Out-Null
        Write-Host "Exported public certificate (.cer) -> $PublicCertOut"
    }

    return $existing
}

$cert = Resolve-SigningCertificate

foreach ($path in $TargetPaths) {
    if (-not (Test-Path $path)) {
        throw "Cannot sign missing file: $path"
    }

    $sig = $null
    try {
        $sig = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256 -TimestampServer "http://timestamp.digicert.com"
    }
    catch {
        Write-Warning "Timestamp server unreachable; signing without external timestamp for $path"
        $sig = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256
    }

    if ($sig.Status -eq "NotSigned" -or $sig.Status -eq "HashMismatch") {
        throw "Authenticode signing failed for $path : $($sig.Status) ($($sig.StatusMessage))"
    }

    $verified = Get-AuthenticodeSignature -FilePath $path
    if (-not $verified.SignerCertificate -or $verified.Status -eq "NotSigned" -or $verified.Status -eq "HashMismatch") {
        throw "Authenticode verification failed for $path : $($verified.Status)"
    }

    Write-Host "Signed $(Split-Path -Leaf $path) | Thumbprint: $($verified.SignerCertificate.Thumbprint) | Status: $($verified.Status)"
}
