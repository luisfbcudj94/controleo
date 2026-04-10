param(
    [string]$AvdName = "Medium_Phone_API_36.1",
    [switch]$UseCloudApi,
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

function Wait-ApiReady([string]$Url, [string]$Method = "Get", [int]$TimeoutSeconds = 40) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        try {
            $null = Invoke-RestMethod -Uri $Url -Method $Method -TimeoutSec 3
            return
        }
        catch {
            Start-Sleep -Milliseconds 800
        }
    }

    throw "La API no respondió en '$Url' dentro de $TimeoutSeconds segundos."
}

function Stop-ListenerOnPort([int]$Port) {
    $connections = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if (-not $connections) {
        return
    }

    $pids = $connections | Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($processId in $pids) {
        try {
            $proc = Get-Process -Id $processId -ErrorAction Stop
            Write-Step "Deteniendo proceso en puerto ${Port}: $($proc.ProcessName) (PID $processId)"
            Stop-Process -Id $processId -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "No se pudo detener el proceso PID $processId en puerto ${Port}: $($_.Exception.Message)"
        }
    }

    Start-Sleep -Milliseconds 600
}

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ApiProject = Join-Path $RepoRoot "Controleo.Backend\Controleo.Api\Controleo.Api.csproj"
$MobileProject = Join-Path $RepoRoot "Controleo.Mobile\Controleo.Mobile.csproj"

if (-not (Test-Path $MobileProject)) { throw "No existe: $MobileProject" }

if (-not $UseCloudApi -and -not (Test-Path $ApiProject)) { throw "No existe: $ApiProject" }

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
Ensure-Command az

$adbExe = Join-Path $env:ANDROID_SDK_ROOT "platform-tools\adb.exe"
$emulatorExe = Join-Path $env:ANDROID_SDK_ROOT "emulator\emulator.exe"

if (-not $UseCloudApi) {
    Write-Step "Iniciando API (.NET)"
    Stop-ListenerOnPort -Port 5051

    $cosmosEndpoint = az cosmosdb show --resource-group "projects" --name "controleocosmos262c4" --query documentEndpoint -o tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($cosmosEndpoint)) {
        throw "No se pudo obtener COSMOS_DB_ENDPOINT desde Azure."
    }

    $cosmosKey = az cosmosdb keys list --resource-group "projects" --name "controleocosmos262c4" --query primaryMasterKey -o tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($cosmosKey)) {
        throw "No se pudo obtener COSMOS_DB_KEY desde Azure."
    }

    $localJwtSecret = if (-not [string]::IsNullOrWhiteSpace($env:LocalAuth__JwtSecret) -and $env:LocalAuth__JwtSecret.Trim().Length -ge 32) {
        $env:LocalAuth__JwtSecret.Trim()
    }
    else {
        "local-dev-secret-2026-controleo-super-long"
    }

    Start-Process powershell -ArgumentList @(
        "-NoExit",
        "-Command",
        "`$env:COSMOS_DB_ENDPOINT='$cosmosEndpoint'; `$env:COSMOS_DB_KEY='$cosmosKey'; `$env:LocalAuth__JwtSecret='$localJwtSecret'; cd '$RepoRoot\Controleo.Backend\Controleo.Api'; Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; func.cmd start --dotnet-isolated --port 5051"
    ) | Out-Null

    Write-Step "Esperando API en http://localhost:5051/api/catalogs (OPTIONS)"
    Wait-ApiReady -Url "http://localhost:5051/api/catalogs" -Method "Options" -TimeoutSeconds 45
}
else {
    Write-Step "Usando API desplegada en Cloud Run (no se inicia API local)"

    # Configuración local opcional para futuro desarrollo:
    # Start-Process powershell -ArgumentList @(
    #     "-NoExit",
    #     "-Command",
    #     "cd '$RepoRoot'; dotnet run --project '$ApiProject' --launch-profile http"
    # ) | Out-Null
    # Wait-ApiReady -Url "http://localhost:5051/api/catalogs" -TimeoutSeconds 45
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
    -p:EmbedAssembliesIntoApk=true `
    -p:AndroidFastDeploymentType=None `
    --ignore-failed-sources

Write-Step "Proceso finalizado"
