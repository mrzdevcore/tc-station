<#
.SYNOPSIS
  Creates the self-signed MSIX signing certificate and hands its secrets to the release workflow
  without printing them.

.DESCRIPTION
  Automates the Windows section of docs/signing-secrets.md:
    1. creates a self-signed code-signing certificate in Cert:\CurrentUser\My;
    2. generates a random PFX password (or uses -Password) and exports the PFX;
    3. delivers WINDOWS_PFX_BASE64, WINDOWS_PFX_PASSWORD and WINDOWS_MSIX_PUBLISHER, either
       straight to GitHub with `gh secret set` (-Repo) or one at a time through the clipboard;
    4. exports the public certificate (.cer) for customers (scripts/trust-windows-certificate.ps1)
       and prints its thumbprint;
    5. deletes the local PFX unless -KeepPfx.

  The PFX base64 and password are never written to the console, to a log or to a file other than
  the PFX itself. The private key stays exportable in Cert:\CurrentUser\My; back the PFX up to a
  password manager (-KeepPfx) if you need it on another machine.

  A self-signed certificate is for pre-releases only: customers must trust it explicitly.

.EXAMPLE
  .\new-windows-signing-cert.ps1 -Repo tropicast/tc-station

.EXAMPLE
  .\new-windows-signing-cert.ps1 -Subject 'CN=Tropicast, O=Tropicast, C=FR' -KeepPfx

.EXAMPLE
  .\new-windows-signing-cert.ps1 -Password (Read-Host -AsSecureString 'PFX password')
#>
[CmdletBinding()]
param(
    # Becomes the MSIX publisher: it must equal the certificate subject character for character.
    [string]$Subject = 'CN=Tropicast',
    [string]$FriendlyName = 'Tropicast MSIX signing',
    [ValidateRange(1, 10)][int]$ValidYears = 3,
    # Where tropicast.cer (and tropicast.pfx with -KeepPfx) are written.
    [string]$OutputDir = (Get-Location).Path,
    # Set the secrets in this GitHub repository with the gh CLI. Without it, use the clipboard.
    [string]$Repo,
    # PFX password. Default: 32 random bytes, never shown.
    [Security.SecureString]$Password,
    # Keep tropicast.pfx in -OutputDir (store it in a password manager, then delete it).
    [switch]$KeepPfx
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSEdition -eq 'Core' -and -not $IsWindows) { throw 'This script needs Windows (New-SelfSignedCertificate).' }
if ($Repo -and -not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh CLI not found. Install it or omit -Repo to use the clipboard.' }
if ($Repo) {
    gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'gh is not authenticated. Run: gh auth login' }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$pfxPath = Join-Path $OutputDir 'tropicast.pfx'
$cerPath = Join-Path $OutputDir 'tropicast.cer'

# The script avoids .NET method calls where it can: Constrained Language Mode (AppLocker/WDAC
# managed machines) only allows them on core types.

function ConvertFrom-SecureStringPlain([Security.SecureString]$secure) {
    if ($PSVersionTable.PSVersion.Major -ge 7) { return ConvertFrom-SecureString -SecureString $secure -AsPlainText }
    # Windows PowerShell 5.1 fallback: needs Full Language mode.
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

# Same output as [Convert]::ToBase64String, which Constrained Language Mode blocks.
function ConvertTo-Base64([byte[]]$bytes) {
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'
    $chars = for ($i = 0; $i -lt $bytes.Length; $i += 3) {
        $n = [int]$bytes[$i] -shl 16
        if ($i + 1 -lt $bytes.Length) { $n = $n -bor ([int]$bytes[$i + 1] -shl 8) }
        if ($i + 2 -lt $bytes.Length) { $n = $n -bor [int]$bytes[$i + 2] }
        $alphabet[($n -shr 18) -band 63]
        $alphabet[($n -shr 12) -band 63]
        if ($i + 1 -lt $bytes.Length) { $alphabet[($n -shr 6) -band 63] } else { '=' }
        if ($i + 2 -lt $bytes.Length) { $alphabet[$n -band 63] } else { '=' }
    }
    -join $chars
}

function Send-Secret([string]$name, [string]$value) {
    if ($Repo) {
        # gh reads the value from stdin when --body is absent, so it never appears in the process list.
        # PowerShell appends a newline to piped strings; gh trims trailing CR/LF from stdin.
        $value | & gh secret set $name --repo $Repo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "gh secret set $name failed." }
        Write-Output "Set $name in $Repo."
    } else {
        Set-Clipboard -Value $value
        Read-Host "$name is on the clipboard. Paste it into GitHub (Settings > Secrets and variables > Actions), then press Enter" | Out-Null
        # Clear the clipboard so the next application cannot read the secret.
        Set-Clipboard -Value ' '
    }
}

# 1. Certificate (EKU 1.3.6.1.5.5.7.3.3 = code signing; empty basic constraints = end entity).
$cert = New-SelfSignedCertificate -Type Custom -Subject $Subject `
    -KeyUsage DigitalSignature -KeyExportPolicy Exportable -FriendlyName $FriendlyName `
    -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears($ValidYears) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
Write-Output "Created $($cert.Subject) [$($cert.Thumbprint)], valid until $($cert.NotAfter.ToString('yyyy-MM-dd'))."

# A failed run must not leave a half-delivered certificate behind: a re-run would create a second
# one with the same subject.
$completed = $false
$pfxBase64 = $null
$plainPassword = $null
try {
    # 2. Password and PFX.
    $generatedPassword = -not $Password
    if ($generatedPassword) {
        $bytes = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $Password = ConvertTo-SecureString -String ([Convert]::ToBase64String($bytes)) -AsPlainText -Force
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $Password | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

    # 3. Secrets. The publisher is read back from the certificate so it matches exactly.
    $pfxBytes = if ($PSVersionTable.PSVersion.Major -ge 6) { Get-Content -LiteralPath $pfxPath -AsByteStream -Raw }
                else { Get-Content -LiteralPath $pfxPath -Encoding Byte -Raw }
    $pfxBase64 = ConvertTo-Base64 $pfxBytes
    $plainPassword = ConvertFrom-SecureStringPlain $Password
    Send-Secret 'WINDOWS_PFX_BASE64' $pfxBase64
    Send-Secret 'WINDOWS_PFX_PASSWORD' $plainPassword
    Send-Secret 'WINDOWS_MSIX_PUBLISHER' $cert.Subject
    $completed = $true
} finally {
    $pfxBase64 = $null
    $plainPassword = $null
    # 5. The PFX holds the private key: keep it only on request, and never after a failure.
    if ((-not $KeepPfx -or -not $completed) -and (Test-Path -LiteralPath $pfxPath)) { Remove-Item -LiteralPath $pfxPath -Force }
    if (-not $completed) {
        Write-Warning "Run failed: removing certificate $($cert.Thumbprint), its private key and $cerPath."
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey -ErrorAction Continue
        if (Test-Path -LiteralPath $cerPath) { Remove-Item -LiteralPath $cerPath -Force }
        Write-Warning 'GitHub secrets already set by this run point to the removed certificate. Re-run the script to replace them.'
    }
}

# 4. What to give customers.
Write-Output ''
Write-Output "Public certificate for customers: $cerPath"
Write-Output "Thumbprint (send it separately so customers can check it): $($cert.Thumbprint)"
Write-Output "Customers run: .\trust-windows-certificate.ps1 -Certificate .\tropicast.cer -ExpectedThumbprint $($cert.Thumbprint)"
if ($KeepPfx) {
    Write-Output "PFX kept at $pfxPath. Store it and its password in a password manager, then delete it."
    if ($generatedPassword -and -not $Repo) { Write-Output 'The password was only on the clipboard. Save it now if you did not.' }
} else {
    Write-Output 'Local PFX deleted. The private key remains exportable in Cert:\CurrentUser\My.'
}
if ($Repo) { gh secret list --repo $Repo }
