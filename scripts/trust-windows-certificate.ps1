<#
.SYNOPSIS
  Trusts the Tropicast self-signed signing certificate on a customer machine so its MSIX installs.

.DESCRIPTION
  Windows only installs an MSIX whose signing certificate is trusted machine-wide. This script:
    1. takes the public certificate from -Certificate (.cer), or reads it from the -Package signature;
    2. checks it: no private key, code-signing usage, not expired and, with -ExpectedThumbprint,
       the exact certificate the publisher announced;
    3. asks for confirmation (skip with -Force), then adds it to LocalMachine\TrustedPeople.
  It restarts itself elevated (UAC prompt) when needed. The certificate is never added to Trusted
  Root, so it can sign packages but cannot vouch for websites or other certificates.

  To also install the app in one step, use scripts/install-windows-msix.ps1.

.EXAMPLE
  .\trust-windows-certificate.ps1 -Certificate .\tropicast.cer -ExpectedThumbprint 0123456789ABCDEF0123456789ABCDEF01234567

.EXAMPLE
  .\trust-windows-certificate.ps1 -Package .\Tropicast-Station-<version>-win-x64.msix

.EXAMPLE
  .\trust-windows-certificate.ps1 -Certificate .\tropicast.cer -Remove
#>
[CmdletBinding(DefaultParameterSetName = 'Certificate')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Certificate')][string]$Certificate,
    [Parameter(Mandatory, ParameterSetName = 'Package')][string]$Package,
    # Thumbprint given by the publisher through another channel; the script refuses any other certificate.
    [string]$ExpectedThumbprint,
    # Remove the certificate from LocalMachine\TrustedPeople instead of adding it.
    [switch]$Remove,
    # Do not ask for confirmation.
    [switch]$Force,
    # Internal: set when the script restarted itself elevated.
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Resolve paths now: the elevated process starts in another working directory.
if ($Certificate) {
    if (-not (Test-Path -LiteralPath $Certificate)) { throw "Certificate not found: $Certificate" }
    $Certificate = (Resolve-Path -LiteralPath $Certificate).Path
}
if ($Package) {
    if (-not (Test-Path -LiteralPath $Package)) { throw "Package not found: $Package" }
    $Package = (Resolve-Path -LiteralPath $Package).Path
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    if ($Elevated) { throw 'Elevation failed: run this script from an elevated (Run as administrator) PowerShell.' }
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Elevated')
    if ($Certificate) { $arguments += '-Certificate', "`"$Certificate`"" }
    if ($Package) { $arguments += '-Package', "`"$Package`"" }
    if ($ExpectedThumbprint) { $arguments += '-ExpectedThumbprint', "`"$ExpectedThumbprint`"" }
    if ($Remove) { $arguments += '-Remove' }
    if ($Force) { $arguments += '-Force' }
    Write-Output 'Administrator rights are needed to write LocalMachine\TrustedPeople. Accept the Windows prompt.'
    $child = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $child.ExitCode
}

$exitCode = 0
try {
    if ($Certificate) {
        $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2 $Certificate
    } else {
        $signature = Get-AuthenticodeSignature -FilePath $Package
        if (-not $signature.SignerCertificate) {
            throw "$Package has no readable signature. Ask the publisher for the .cer file and use -Certificate."
        }
        $cert = $signature.SignerCertificate
    }

    Write-Output "Subject:    $($cert.Subject)"
    Write-Output "Thumbprint: $($cert.Thumbprint)"
    Write-Output "Valid:      $($cert.NotBefore.ToString('yyyy-MM-dd')) to $($cert.NotAfter.ToString('yyyy-MM-dd'))"

    if ($ExpectedThumbprint) {
        $expected = ($ExpectedThumbprint -replace '[\s:]', '').ToUpperInvariant()
        if ($cert.Thumbprint -ne $expected) {
            throw "Thumbprint mismatch: expected $expected, got $($cert.Thumbprint). Do not trust this certificate."
        }
        Write-Output 'Thumbprint matches the expected value.'
    }

    $store = New-Object Security.Cryptography.X509Certificates.X509Store 'TrustedPeople', 'LocalMachine'
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        $existing = $store.Certificates.Find('FindByThumbprint', $cert.Thumbprint, $false)
        if ($Remove) {
            if ($existing.Count -eq 0) { Write-Output 'The certificate is not trusted on this machine.'; return }
            $store.Remove($cert)
            Write-Output 'Removed the certificate from LocalMachine\TrustedPeople.'
            return
        }
        if ($existing.Count -gt 0) { Write-Output 'The certificate is already trusted.'; return }

        if ($cert.HasPrivateKey) { throw 'This file contains a private key. Use the public .cer file, never the .pfx.' }
        $codeSigning = $cert.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
            ForEach-Object { $_.EnhancedKeyUsages } | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }
        if (-not $codeSigning) { throw 'This is not a code-signing certificate. Refusing to trust it.' }
        if ($cert.NotAfter -lt (Get-Date)) { throw "The certificate expired on $($cert.NotAfter.ToString('yyyy-MM-dd')). Ask the publisher for a new one." }

        if (-not $Force -and -not $ExpectedThumbprint) {
            Write-Warning 'No -ExpectedThumbprint given. Compare the thumbprint above with the one the publisher sent you.'
        }
        if (-not $Force) {
            $answer = Read-Host 'Trust this certificate for app packages on this machine? [y/N]'
            if ($answer -notmatch '^(y|yes)$') { Write-Output 'Cancelled. Nothing changed.'; return }
        }
        $store.Add($cert)
        Write-Output 'Added the certificate to LocalMachine\TrustedPeople. You can now install the Tropicast MSIX.'
    } finally {
        $store.Close()
    }
} catch {
    if (-not $Elevated) { throw }
    # The elevated window would close before the error is visible.
    Write-Host "ERROR: $_" -ForegroundColor Red
    $exitCode = 1
} finally {
    if ($Elevated) { Read-Host 'Press Enter to close this window' | Out-Null }
}
exit $exitCode
