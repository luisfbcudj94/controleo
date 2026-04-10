param(
    [string]$KeystorePath = "",
    [string]$KeystorePassword = "",
    [string]$KeyAlias = "",
    [string]$KeyPassword = "",
    [string]$SigningInfoFile = "C:\Users\v-tgeethanat\Desktop\secrets\controleo-release-info.txt",
    [switch]$InstallOnDevice,
    [switch]$UseAab
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

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Fallo: $Description"
    }
}

function Resolve-BuildToolsPath([string]$SdkRoot) {
    $buildToolsRoot = Join-Path $SdkRoot "build-tools"
    if (-not (Test-Path $buildToolsRoot)) {
        throw "No se encontró build-tools en: $buildToolsRoot"
    }

    $latest = Get-ChildItem $buildToolsRoot -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if (-not $latest) {
        throw "No hay versiones de build-tools instaladas."
    }

    return $latest.FullName
}

function Load-SigningInfoFromFile([string]$Path) {
    $result = @{}
    if (-not (Test-Path $Path)) {
        return $result
    }

    foreach ($line in Get-Content $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $parts = $line.Split("=", 2)
        if ($parts.Count -ne 2) {
            continue
        }

        $name = $parts[0].Trim()
        $value = $parts[1].Trim()
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $result[$name] = $value
        }
    }

    return $result
}

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$MobileProject = Join-Path $RepoRoot "Controleo.Mobile\Controleo.Mobile.csproj"
$ArtifactsDir = Join-Path $RepoRoot "artifacts"

Ensure-File $MobileProject

$signingFileData = Load-SigningInfoFromFile -Path $SigningInfoFile

if ([string]::IsNullOrWhiteSpace($KeystorePath) -and $signingFileData.ContainsKey("KeystorePath")) {
    $KeystorePath = $signingFileData["KeystorePath"]
}
if ([string]::IsNullOrWhiteSpace($KeyAlias) -and $signingFileData.ContainsKey("Alias")) {
    $KeyAlias = $signingFileData["Alias"]
}
if ([string]::IsNullOrWhiteSpace($KeystorePassword) -and $signingFileData.ContainsKey("StorePassword")) {
    $KeystorePassword = $signingFileData["StorePassword"]
}
if ([string]::IsNullOrWhiteSpace($KeyPassword) -and $signingFileData.ContainsKey("KeyPassword")) {
    $KeyPassword = $signingFileData["KeyPassword"]
}

if ([string]::IsNullOrWhiteSpace($KeystorePath) -or [string]::IsNullOrWhiteSpace($KeystorePassword) -or [string]::IsNullOrWhiteSpace($KeyAlias) -or [string]::IsNullOrWhiteSpace($KeyPassword)) {
    throw "Faltan datos de firma. Proporciona parámetros o completa el archivo de signing info."
}

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

$buildToolsPath = Resolve-BuildToolsPath -SdkRoot $env:ANDROID_SDK_ROOT
$zipalign = Join-Path $buildToolsPath "zipalign.exe"
$apksigner = Join-Path $buildToolsPath "apksigner.bat"

Ensure-File $zipalign
Ensure-File $apksigner

if (-not (Test-Path $ArtifactsDir)) {
    New-Item -Path $ArtifactsDir -ItemType Directory | Out-Null
}

$env:Path += ";$env:JAVA_HOME\bin;$env:ANDROID_SDK_ROOT\platform-tools;$env:ANDROID_SDK_ROOT\emulator"

Set-Location (Join-Path $RepoRoot "Controleo.Mobile")

$packageFormat = if ($UseAab) { "aab" } else { "apk" }

Write-Step "Publicando release Android ($packageFormat) sin firma MAUI"
dotnet publish .\Controleo.Mobile.csproj `
    -f net9.0-android `
    -c Release `
    -p:AndroidSdkDirectory="$env:ANDROID_SDK_ROOT" `
    -p:JavaSdkDirectory="$env:JAVA_HOME" `
    -p:AndroidKeyStore=false `
    -p:AndroidPackageFormats=$packageFormat `
    --ignore-failed-sources

$publishDir = Join-Path (Get-Location) "bin\Release\net9.0-android\publish"
if (-not (Test-Path $publishDir)) {
    throw "No se encontró carpeta publish: $publishDir"
}

if ($UseAab) {
    $aab = Get-ChildItem $publishDir -Filter *.aab | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $aab) {
        throw "No se generó ningún AAB en $publishDir"
    }

    $ts = Get-Date -Format "yyyyMMdd-HHmmss"
    $outAab = Join-Path $ArtifactsDir ("controleo-mobile-release-{0}.aab" -f $ts)
    Copy-Item $aab.FullName $outAab -Force
    Write-Step "AAB generado en artifacts"
    Get-Item $outAab | Select-Object FullName, Length, LastWriteTime | Format-List
    return
}

$unsignedApk = Get-ChildItem $publishDir -Filter *.apk |
    Where-Object {
        $_.Name -notmatch "-Signed\.apk$" -and
        $_.Name -notmatch "-aligned\.apk$" -and
        $_.Name -notmatch "-signed-apksigner\.apk$"
    } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $unsignedApk) {
    throw "No se encontró APK base para firmar en $publishDir"
}

$alignedApk = Join-Path $publishDir "com.companyname.controleo.mobile-aligned.apk"
$signedApk = Join-Path $publishDir "com.companyname.controleo.mobile-signed-apksigner.apk"

if (Test-Path $alignedApk) { Remove-Item $alignedApk -Force }
if (Test-Path $signedApk) { Remove-Item $signedApk -Force }

Write-Step "Alineando APK (zipalign)"
Invoke-External -Description "zipalign" -Command {
    & $zipalign -p -f 4 $unsignedApk.FullName $alignedApk
}

Write-Step "Firmando APK (apksigner)"
$signed = $false
try {
    Invoke-External -Description "apksigner con key password" -Command {
        & $apksigner sign `
            --ks $KeystorePath `
            --ks-key-alias $KeyAlias `
            --ks-pass "pass:$KeystorePassword" `
            --key-pass "pass:$KeyPassword" `
            --out $signedApk `
            $alignedApk
    }
    $signed = $true
}
catch {
    if (-not [string]::IsNullOrWhiteSpace($KeystorePassword) -and $KeystorePassword -ne $KeyPassword) {
        Write-Host "Reintentando firma usando store password como key password (keystore PKCS12)..." -ForegroundColor Yellow
        Invoke-External -Description "apksigner con store password" -Command {
            & $apksigner sign `
                --ks $KeystorePath `
                --ks-key-alias $KeyAlias `
                --ks-pass "pass:$KeystorePassword" `
                --key-pass "pass:$KeystorePassword" `
                --out $signedApk `
                $alignedApk
        }
        $signed = $true
    }
    else {
        throw
    }
}

if (-not $signed) {
    throw "No se pudo firmar el APK."
}

Write-Step "Verificando firma final"
Invoke-External -Description "apksigner verify" -Command {
    & $apksigner verify --verbose --print-certs $signedApk
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactApk = Join-Path $ArtifactsDir ("controleo-mobile-release-{0}-signed.apk" -f $timestamp)
Copy-Item $signedApk $artifactApk -Force

Write-Step "APK firmado generado"
Get-Item $artifactApk | Select-Object FullName, Length, LastWriteTime | Format-List

if ($InstallOnDevice) {
    $adb = Join-Path $env:ANDROID_SDK_ROOT "platform-tools\adb.exe"
    Ensure-File $adb

    Write-Step "Instalando APK en dispositivo/emulador conectado"
    & $adb install -r $artifactApk
}
