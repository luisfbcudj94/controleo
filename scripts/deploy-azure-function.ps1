param(
    [string]$Subscription = "Azure subscription 1",
    [string]$ResourceGroup = "projects",
    [string]$FunctionAppName = "controleo-api",
    [string]$ProjectPath = ".\Controleo.Api\Controleo.Api.csproj",
    [string]$Configuration = "Release",
    [string]$CosmosAccountName = "controleocosmos262c4",
    [string]$CosmosDatabaseName = "controleo",
    [string]$TenantId = "",
    [string]$ExpectedUser = "",
    [switch]$UseDeviceCode,
    [switch]$ForceFreshLogin,
    [switch]$SkipLogin
)

$ErrorActionPreference = "Stop"

function Invoke-AzCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Falló: $Description"
    }
}

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "No se encontró el comando '$Name'. Instálalo y vuelve a ejecutar."
    }
}

Write-Host "==> Validando herramientas..." -ForegroundColor Cyan
Require-Command -Name "az"
Require-Command -Name "dotnet"
Require-Command -Name "func"

if (-not $SkipLogin) {
    if ($ForceFreshLogin) {
        Invoke-AzCommand -Description "Cerrando sesiones previas de Azure CLI" -Command { az logout --output none }
    }

    if (-not [string]::IsNullOrWhiteSpace($TenantId) -and $UseDeviceCode) {
        Invoke-AzCommand -Description "Iniciando sesión en Azure (tenant + device code)" -Command { az login --tenant $TenantId --use-device-code --output none }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($TenantId)) {
        Invoke-AzCommand -Description "Iniciando sesión en Azure (tenant específico)" -Command { az login --tenant $TenantId --output none }
    }
    elseif ($UseDeviceCode) {
        Invoke-AzCommand -Description "Iniciando sesión en Azure (device code)" -Command { az login --use-device-code --output none }
    }
    else {
        Invoke-AzCommand -Description "Iniciando sesión en Azure" -Command { az login --output none }
    }
}

Invoke-AzCommand -Description "Seleccionando suscripción: $Subscription" -Command { az account set --subscription $Subscription }

$activeUser = az account show --query user.name -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($activeUser)) {
    throw "No se pudo identificar el usuario activo de Azure CLI."
}

$activeTenant = az account show --query tenantId -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($activeTenant)) {
    throw "No se pudo identificar el tenant activo de Azure CLI."
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedUser) -and -not [string]::Equals($activeUser, $ExpectedUser, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Usuario activo '$activeUser' no coincide con el esperado '$ExpectedUser'."
}

if (-not [string]::IsNullOrWhiteSpace($TenantId) -and -not [string]::Equals($activeTenant, $TenantId, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Tenant activo '$activeTenant' no coincide con el tenant solicitado '$TenantId'."
}

Write-Host "==> Contexto activo: usuario=$activeUser tenant=$activeTenant" -ForegroundColor Cyan

$projectFullPath = Resolve-Path $ProjectPath
$projectDirectory = Split-Path -Parent $projectFullPath
$cosmosEndpoint = az cosmosdb show --resource-group $ResourceGroup --name $CosmosAccountName --query documentEndpoint -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($cosmosEndpoint)) {
    throw "No se pudo obtener el endpoint de Cosmos DB para '$CosmosAccountName'."
}

$cosmosKey = az cosmosdb keys list --resource-group $ResourceGroup --name $CosmosAccountName --query primaryMasterKey -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($cosmosKey)) {
    throw "No se pudo obtener la llave primaria de Cosmos DB para '$CosmosAccountName'."
}

$settings = @()
$settings += "COSMOS_DB_ENDPOINT=$cosmosEndpoint"
$settings += "COSMOS_DB_KEY=$cosmosKey"
$settings += "CosmosStorage__DatabaseName=$CosmosDatabaseName"
$settings += "CosmosStorage__ExpensesContainerName=expenses"
$settings += "CosmosStorage__BudgetsContainerName=budgets"
$settings += "CosmosStorage__SettingsContainerName=app_settings"
$settings += "CosmosStorage__CatalogDocumentId=catalogs"

Invoke-AzCommand -Description "Configurando app settings mínimas" -Command {
    az functionapp config appsettings set `
        --resource-group $ResourceGroup `
        --name $FunctionAppName `
        --settings $settings `
        --output none
}

Invoke-AzCommand -Description "Desplegando Function App (Flex compatible)" -Command {
    Push-Location $projectDirectory
    try {
        func azure functionapp publish $FunctionAppName --subscription $Subscription --dotnet-isolated --nozip
    }
    finally {
        Pop-Location
    }
}

$defaultHostName = az functionapp show --resource-group $ResourceGroup --name $FunctionAppName --query defaultHostName -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($defaultHostName)) {
    $defaultHostName = "$FunctionAppName.azurewebsites.net"
}

Write-Host "" 
Write-Host "Deploy completado." -ForegroundColor Green
Write-Host "URL base: https://$defaultHostName/api" -ForegroundColor Green
Write-Host "" 
Write-Host "Ejemplo de prueba:" -ForegroundColor Yellow
Write-Host "  https://$defaultHostName/api/catalogs" -ForegroundColor Yellow
