param(
    [string]$AvdName = "Medium_Phone_API_36.1",
    [switch]$SkipApi,
    [switch]$SkipEmulator
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Ensure-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "No se encontró el comando '$Name' en PATH."
    }
}

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ApiProject = Join-Path $RepoRoot "Controleo.Api\Controleo.Api.csproj"
$MobileProject = Join-Path $RepoRoot "Controleo.Mobile\Controleo.Mobile.csproj"

if (-not (Test-Path $ApiProject)) { throw "No existe: $ApiProject" }
if (-not (Test-Path $MobileProject)) { throw "No existe: $MobileProject" }

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

Ensure-Command dotnet
Ensure-Command adb

$adbExe = Join-Path $env:ANDROID_SDK_ROOT "platform-tools\adb.exe"
$emulatorExe = Join-Path $env:ANDROID_SDK_ROOT "emulator\emulator.exe"

if (-not $SkipApi) {
    Write-Step "Iniciando API (.NET)"
    Start-Process powershell -ArgumentList @(
        "-NoExit",
        "-Command",
        "cd '$RepoRoot'; dotnet run --project '$ApiProject'"
    ) | Out-Null
    Start-Sleep -Seconds 2
}

if (-not $SkipEmulator) {
    if (-not (Test-Path $emulatorExe)) {
        throw "No se encontró emulator.exe en $emulatorExe"
    }

    Write-Step "Iniciando emulador '$AvdName'"
    Start-Process $emulatorExe -ArgumentList "-avd", $AvdName | Out-Null

    Write-Step "Esperando dispositivo Android"
    & $adbExe start-server | Out-Null
    & $adbExe wait-for-device
    & $adbExe devices
}

Write-Step "Compilando y ejecutando app MAUI Android"
Set-Location (Join-Path $RepoRoot "Controleo.Mobile")

dotnet build .\Controleo.Mobile.csproj `
    -f net9.0-android `
    -t:Run `
    -p:AndroidSdkDirectory="$env:ANDROID_SDK_ROOT" `
    -p:JavaSdkDirectory="$env:JAVA_HOME" `
    --ignore-failed-sources

Write-Step "Proceso finalizado"
