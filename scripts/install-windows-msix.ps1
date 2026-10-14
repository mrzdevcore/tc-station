<#
.SYNOPSIS
  Trusts the signing certificate of a Tropicast MSIX and installs it. For packages signed with a
  self-signed or private-CA certificate (for example release pre-releases).

.DESCRIPTION
  Windows only installs an MSIX whose signing certificate is trusted on the machine. This script:
    1. takes the certificate from -Certificate, or reads it from the MSIX signature;
    2. adds it to LocalMachine\TrustedPeople (needs an elevated PowerShell);
    3. installs the package with Add-AppxPackage.

  Only trust a certificate you created or received from a source you trust. Remove it afterwards
  with -Remove.

.EXAMPLE
  .\install-windows-msix.ps1 -Package .\Tropicast-Station-<version>-win-x64.msix

.EXAMPLE
  .\install-windows-msix.ps1 -Package .\Tropicast-Station-<version>-win-x64.msix -Certificate .\tropicast.cer

.EXAMPLE
  .\install-windows-msix.ps1 -Package .\Tropicast-Station-<version>-win-x64.msix -Remove
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Package,
    [string]$Certificate,
    # Uninstall the app and remove the certificate from LocalMachine\TrustedPeople.
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Cmdlets and the Cert: drive only: Constrained Language Mode (AppLocker/WDAC managed machines)
# blocks .NET method calls such as X509Store.Add.

# S-1-16-12288 is the High Mandatory Level group: present only in an elevated process.
if (-not ((whoami /groups) -match 'S-1-16-12288')) {
    throw 'Run this script from an elevated (Run as administrator) PowerShell: it writes to LocalMachine\TrustedPeople.'
}

if (-not (Test-Path -LiteralPath $Package)) { throw "Package not found: $Package" }
$Package = (Resolve-Path -LiteralPath $Package).Path

if ($Certificate) {
    if (-not (Test-Path -LiteralPath $Certificate)) { throw "Certificate not found: $Certificate" }
    $Certificate = (Resolve-Path -LiteralPath $Certificate).Path
    $cert = Get-PfxCertificate -FilePath $Certificate
} else {
    $signature = Get-AuthenticodeSignature -FilePath $Package
    if (-not $signature.SignerCertificate) {
        throw "$Package has no readable signature. Pass the public certificate with -Certificate <file.cer>."
    }
    $cert = $signature.SignerCertificate
}
Write-Output "Certificate: $($cert.Subject) [$($cert.Thumbprint)]"

$trustedPath = "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)"
if ($Remove) {
    $app = Get-AppxPackage Tropicast.Station
    if ($app) { Remove-AppxPackage $app.PackageFullName; Write-Output 'Removed Tropicast Station.' }
    if (Test-Path -LiteralPath $trustedPath) { Remove-Item -LiteralPath $trustedPath }
    Write-Output 'Removed the certificate from LocalMachine\TrustedPeople.'
    return
}
if (Test-Path -LiteralPath $trustedPath) {
    Write-Output 'The certificate is already trusted.'
} else {
    # Import-Certificate reads a file: write the signer certificate to a temporary .cer if needed.
    $cerFile = $Certificate
    if (-not $cerFile) {
        $cerFile = Join-Path $env:TEMP "tropicast-$((New-Guid).Guid).cer"
        if ($PSVersionTable.PSVersion.Major -ge 6) { Set-Content -LiteralPath $cerFile -Value $cert.RawData -AsByteStream }
        else { Set-Content -LiteralPath $cerFile -Value $cert.RawData -Encoding Byte }
    }
    try {
        Import-Certificate -FilePath $cerFile -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    } finally {
        if (-not $Certificate) { Remove-Item -LiteralPath $cerFile -Force }
    }
    Write-Output 'Added the certificate to LocalMachine\TrustedPeople.'
}

Add-AppxPackage -Path $Package
$installed = Get-AppxPackage Tropicast.Station
if (-not $installed) { throw 'The MSIX did not install.' }
Write-Output "Installed Tropicast Station $($installed.Version). Start it from the Start menu."
