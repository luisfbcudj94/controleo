param(
    [Parameter(Mandatory = $true)]
    [string]$KeystorePath,

    [Parameter(Mandatory = $true)]
    [string]$KeystorePassword,

    [Parameter(Mandatory = $true)]
    [string]$KeyAlias,

    [Parameter(Mandatory = $true)]
    [string]$KeyPassword
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Ensure-File([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "No existe el archivo: $Path"
    }
}

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$MobileProject = Join-Path $RepoRoot "Controleo.Mobile\Controleo.Mobile.csproj"

Ensure-File $MobileProject
Ensure-File $KeystorePath

if (-not $env:JAVA_HOME) {
    $studioJbr = "C:\Program Files\Android\Android Studio\jbr"
    if (Test-Path $studioJbr) {
        $env:JAVA_HOME = $studioJbr
    }
}

if (-not $env:ANDROID_SDK_ROOT) {
    $defaultSdk = Join-Path $env:LOCALAPPDATA "Android\Sdk"
    if (Test-Path $defaultSdk) {
        $env:ANDROID_SDK_ROOT = $defaultSdk
    }
}

if (-not $env:JAVA_HOME -or -not (Test-Path $env:JAVA_HOME)) {
    throw "JAVA_HOME no está configurado o no existe."
}

if (-not $env:ANDROID_SDK_ROOT -or -not (Test-Path $env:ANDROID_SDK_ROOT)) {
    throw "ANDROID_SDK_ROOT no está configurado o no existe."
}

$env:Path += ";$env:JAVA_HOME\bin;$env:ANDROID_SDK_ROOT\platform-tools;$env:ANDROID_SDK_ROOT\emulator"

Write-Step "Generando APK Release firmado"
Set-Location (Join-Path $RepoRoot "Controleo.Mobile")

dotnet publish .\Controleo.Mobile.csproj `
    -f net9.0-android `
    -c Release `
    -p:AndroidSdkDirectory="$env:ANDROID_SDK_ROOT" `
    -p:JavaSdkDirectory="$env:JAVA_HOME" `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore="$KeystorePath" `
    -p:AndroidSigningStorePass="$KeystorePassword" `
    -p:AndroidSigningKeyAlias="$KeyAlias" `
    -p:AndroidSigningKeyPass="$KeyPassword" `
    -p:AndroidPackageFormat=apk `
    --ignore-failed-sources

$outputFolder = Join-Path (Get-Location) "bin\Release\net9.0-android\publish"
Write-Step "APK generado en: $outputFolder"
Get-ChildItem $outputFolder -Filter *.apk | Select-Object FullName, Length, LastWriteTime | Format-List
