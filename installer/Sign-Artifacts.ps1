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
    $cert = $null

    if (-not [string]::IsNullOrWhiteSpace($PfxPath) -and (Test-Path $PfxPath)) {
        Write-Host "Using PFX certificate: $PfxPath"
        if ([string]::IsNullOrEmpty($PfxPassword)) {
            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath)
        }
        else {
            $sec = ConvertTo-SecureString -String $PfxPassword -AsPlainText -Force
            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $sec)
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
        $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $Thumbprint } |
            Select-Object -First 1
        if (-not $cert) {
            throw "Code signing certificate with thumbprint '$Thumbprint' not found."
        }
        Write-Host "Using certificate store thumbprint: $($cert.Thumbprint)"
    }
    else {
        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq "CN=AAFF5C79-617E-404B-893B-24B04B60D21C" -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1

        if (-not $cert) {
            Write-Host "Creating local self-signed Authenticode certificate (CN=AAFF5C79-617E-404B-893B-24B04B60D21C) in Cert:\CurrentUser\My..."
            $cert = New-SelfSignedCertificate `
                -Type CodeSigningCert `
                -Subject "CN=AAFF5C79-617E-404B-893B-24B04B60D21C" `
                -FriendlyName "ArcSubash Local Code Signing" `
                -CertStoreLocation "Cert:\CurrentUser\My" `
                -NotAfter (Get-Date).AddYears(5)
        }
        else {
            Write-Host "Using existing local self-signed certificate: $($cert.Thumbprint)"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($PublicCertOut)) {
        $certDir = Split-Path -Parent $PublicCertOut
        if (-not [string]::IsNullOrEmpty($certDir)) {
            New-Item -ItemType Directory -Force -Path $certDir | Out-Null
        }
        Export-Certificate -Cert $cert -FilePath $PublicCertOut -Force | Out-Null
        Write-Host "Exported public certificate (.cer) -> $PublicCertOut"
    }

    return $cert
}

function Find-SignToolExe {
    $cmd = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @()
    $candidates += Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue
    $candidates += Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue

    $best = $candidates | Sort-Object FullName -Descending | Select-Object -First 1
    if ($best) { return $best.FullName }
    throw "signtool.exe not found. Restore NuGet packages (Microsoft.Windows.SDK.BuildTools) or install Windows 10/11 SDK."
}

function Invoke-MsixSign {
    param(
        [string]$FilePath,
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert
    )

    $signTool = Find-SignToolExe
    $baseArgs = @("sign", "/fd", "SHA256")
    if (-not [string]::IsNullOrWhiteSpace($PfxPath) -and (Test-Path $PfxPath)) {
        $baseArgs += @("/f", $PfxPath)
        if (-not [string]::IsNullOrEmpty($PfxPassword)) {
            $baseArgs += @("/p", $PfxPassword)
        }
    }
    else {
        $inLocalMachine = [bool](Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $Cert.Thumbprint })
        if ($inLocalMachine) {
            $baseArgs += "/sm"
        }
        $baseArgs += @("/sha1", $Cert.Thumbprint)
    }

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $tsArgs = $baseArgs + @("/tr", "http://timestamp.digicert.com", "/td", "SHA256", $FilePath)
        $tsOut = & $signTool @tsArgs 2>&1
        $tsExit = $LASTEXITCODE
        if ($tsExit -ne 0) {
            Write-Warning "Timestamp server unreachable or rejected for $FilePath; signing MSIX without external timestamp"
            $noTsArgs = $baseArgs + @($FilePath)
            $out = & $signTool @noTsArgs 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "signtool.exe failed for $FilePath (exit $LASTEXITCODE): $($out -join [Environment]::NewLine)"
            }
        }
    }
    finally {
        $ErrorActionPreference = $prevEap
    }

    $sig = Get-AuthenticodeSignature -FilePath $FilePath
    if ($sig.Status -eq "NotSigned" -or $sig.Status -eq "HashMismatch") {
        throw "MSIX signature verification failed for $FilePath : $($sig.Status) ($($sig.StatusMessage))"
    }
    Write-Host "Signed $(Split-Path -Leaf $FilePath) | Thumbprint: $($Cert.Thumbprint) | Status: $($sig.Status)"
}

function Invoke-PeSignWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert
    )

    $useTimestamp = $true
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            if ($useTimestamp) {
                return Set-AuthenticodeSignature -FilePath $FilePath -Certificate $Cert -HashAlgorithm SHA256 -TimestampServer "http://timestamp.digicert.com"
            }

            return Set-AuthenticodeSignature -FilePath $FilePath -Certificate $Cert -HashAlgorithm SHA256
        }
        catch {
            $msg = $_.Exception.Message
            if ($msg -match "being used by another process" -or $_.Exception -is [System.IO.IOException]) {
                Start-Sleep -Milliseconds 750
                continue
            }

            if ($useTimestamp) {
                Write-Warning "Timestamp server unreachable; signing without external timestamp for $FilePath"
                $useTimestamp = $false
                Start-Sleep -Milliseconds 500
                continue
            }

            throw
        }
    }

    return Set-AuthenticodeSignature -FilePath $FilePath -Certificate $Cert -HashAlgorithm SHA256
}

$cert = Resolve-SigningCertificate

foreach ($path in $TargetPaths) {
    if (-not (Test-Path $path)) {
        throw "Cannot sign missing file: $path"
    }

    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    if ($ext -in @(".msix", ".appx", ".msixbundle", ".appxbundle")) {
        Invoke-MsixSign -FilePath $path -Cert $cert
        continue
    }

    $sig = Invoke-PeSignWithRetry -FilePath $path -Cert $cert

    if ($sig.Status -eq "NotSigned" -or $sig.Status -eq "HashMismatch") {
        throw "Authenticode signing failed for $path : $($sig.Status) ($($sig.StatusMessage))"
    }

    $verified = Get-AuthenticodeSignature -FilePath $path
    if (-not $verified.SignerCertificate -or $verified.Status -eq "NotSigned" -or $verified.Status -eq "HashMismatch") {
        throw "Authenticode verification failed for $path : $($verified.Status)"
    }

    Write-Host "Signed $(Split-Path -Leaf $path) | Thumbprint: $($verified.SignerCertificate.Thumbprint) | Status: $($verified.Status)"
}
