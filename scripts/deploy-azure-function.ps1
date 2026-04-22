param(
    [string]$Subscription = "Azure subscription 1",
    [string]$ResourceGroup = "projects",
    [string]$FunctionAppName = "controleo-api",
    [string]$ProjectPath = ".\Controleo.Backend\Controleo.Api\Controleo.Api.csproj",
    [string]$Configuration = "Release",
    [string]$CosmosAccountName = "controleocosmos262c4",
    [string]$CosmosDatabaseName = "controleo",
    [string]$TenantId = "",
    [string]$ExpectedUser = "",
    [string]$KeyVaultName = "",
    [string]$OpenAiSecretName = "openai-api-key",
    [string]$OpenAiApiKey = "",
    [switch]$EnableLlm,
    [string]$LlmModel = "gpt-4.1-nano",
    [int]$LlmMaxTokens = 260,
    [double]$LlmTemperature = 0.2,
    [int]$LlmRequestTimeoutSeconds = 20,
    [decimal]$AiMonthlyUsdCap = 2.0,
    [int]$AiDailyRequestCap = 50,
    [int]$AiPerUserMonthlyRequestCap = 20,
    [decimal]$AiEstimatedUsdPerRequest = 0.003,
    [int]$AiRecommendationCacheDays = 10,
    [int]$AiMaterialScoreDelta = 7,
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

function To-InvariantString {
    param([Parameter(Mandatory = $true)] $Value)
    return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
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

$functionLocation = az functionapp show --resource-group $ResourceGroup --name $FunctionAppName --query location -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($functionLocation)) {
    $functionLocation = az group show --name $ResourceGroup --query location -o tsv
}

if (-not [string]::IsNullOrWhiteSpace($OpenAiApiKey) -and [string]::IsNullOrWhiteSpace($KeyVaultName)) {
    throw "Si envías OpenAiApiKey debes especificar también KeyVaultName para almacenarla de forma segura."
}

$llmApiKeySetting = ""
if (-not [string]::IsNullOrWhiteSpace($KeyVaultName)) {
    $vaultId = az keyvault list --resource-group $ResourceGroup --query "[?name=='$KeyVaultName'].id | [0]" -o tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($vaultId)) {
        Invoke-AzCommand -Description "Creando Key Vault $KeyVaultName" -Command {
            az keyvault create `
                --name $KeyVaultName `
                --resource-group $ResourceGroup `
                --location $functionLocation `
                --enable-rbac-authorization true `
                --output none
        }

        $vaultId = az keyvault list --resource-group $ResourceGroup --query "[?name=='$KeyVaultName'].id | [0]" -o tsv
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($vaultId)) {
            throw "No se pudo resolver el id del Key Vault '$KeyVaultName'."
        }
    }

    Invoke-AzCommand -Description "Habilitando Managed Identity en Function App" -Command {
        az functionapp identity assign --resource-group $ResourceGroup --name $FunctionAppName --output none
    }

    $principalId = az functionapp identity show --resource-group $ResourceGroup --name $FunctionAppName --query principalId -o tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($principalId)) {
        throw "No se pudo obtener principalId de la Managed Identity de '$FunctionAppName'."
    }

    $roleCount = az role assignment list `
        --assignee $principalId `
        --scope $vaultId `
        --query "[?roleDefinitionName=='Key Vault Secrets User'] | length(@)" `
        -o tsv

    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo consultar asignaciones RBAC para Key Vault."
    }

    if ([int]$roleCount -eq 0) {
        Invoke-AzCommand -Description "Asignando rol Key Vault Secrets User a la Function App" -Command {
            az role assignment create `
                --assignee $principalId `
                --role "Key Vault Secrets User" `
                --scope $vaultId `
                --output none
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($OpenAiApiKey)) {
        Invoke-AzCommand -Description "Guardando OpenAI key en Key Vault" -Command {
            az keyvault secret set `
                --vault-name $KeyVaultName `
                --name $OpenAiSecretName `
                --value $OpenAiApiKey `
                --output none
        }

        $secretUri = "https://$KeyVaultName.vault.azure.net/secrets/$OpenAiSecretName"
        $llmApiKeySetting = "LlmProvider__ApiKey=@Microsoft.KeyVault(SecretUri=$secretUri)"
    }
    else {
        if ($EnableLlm.IsPresent) {
            $existingSecret = $null
            try {
                $existingSecret = az keyvault secret show --vault-name $KeyVaultName --name $OpenAiSecretName --query id -o tsv
            }
            catch {
                $existingSecret = ""
            }

            if ([string]::IsNullOrWhiteSpace($existingSecret)) {
                throw "No existe el secreto '$OpenAiSecretName' en Key Vault y la IA está habilitada. Debes enviar OpenAiApiKey o crear el secreto antes del deploy."
            }

            $secretUri = "https://$KeyVaultName.vault.azure.net/secrets/$OpenAiSecretName"
            $llmApiKeySetting = "LlmProvider__ApiKey=@Microsoft.KeyVault(SecretUri=$secretUri)"
        }
        else {
            Write-Warning "No se recibió OpenAI key. Se provisiona Key Vault/MI y se deja IA deshabilitada hasta cargar el secreto."
        }
    }
}

$settings = @()
$settings += "COSMOS_DB_ENDPOINT=$cosmosEndpoint"
$settings += "COSMOS_DB_KEY=$cosmosKey"
$settings += "CosmosStorage__DatabaseName=$CosmosDatabaseName"
$settings += "CosmosStorage__ExpensesContainerName=expenses"
$settings += "CosmosStorage__BudgetsContainerName=budgets"
$settings += "CosmosStorage__RecurringExpensesContainerName=recurring_expenses"
$settings += "CosmosStorage__SettingsContainerName=app_settings"
$settings += "CosmosStorage__CatalogDocumentId=catalogs"
$settings += "LlmProvider__Enabled=$($EnableLlm.IsPresent.ToString().ToLowerInvariant())"
$settings += "LlmProvider__Model=$LlmModel"
$settings += "LlmProvider__MaxTokens=$LlmMaxTokens"
$settings += "LlmProvider__Temperature=$(To-InvariantString $LlmTemperature)"
$settings += "LlmProvider__RequestTimeoutSeconds=$LlmRequestTimeoutSeconds"
$settings += "AiBudget__MonthlyUsdCap=$(To-InvariantString $AiMonthlyUsdCap)"
$settings += "AiBudget__DailyRequestCap=$AiDailyRequestCap"
$settings += "AiBudget__PerUserMonthlyRequestCap=$AiPerUserMonthlyRequestCap"
$settings += "AiBudget__EstimatedUsdPerRequest=$(To-InvariantString $AiEstimatedUsdPerRequest)"
$settings += "AiBudget__RecommendationCacheDays=$AiRecommendationCacheDays"
$settings += "AiBudget__MaterialScoreDelta=$AiMaterialScoreDelta"

if (-not [string]::IsNullOrWhiteSpace($llmApiKeySetting)) {
    $settings += $llmApiKeySetting
}

Invoke-AzCommand -Description "Configurando app settings de runtime y IA" -Command {
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
if (-not [string]::IsNullOrWhiteSpace($KeyVaultName)) {
    Write-Host "Key Vault configurado: $KeyVaultName (secreto: $OpenAiSecretName)" -ForegroundColor Green
}
Write-Host "" 
Write-Host "Ejemplo de prueba:" -ForegroundColor Yellow
Write-Host "  https://$defaultHostName/api/catalogs" -ForegroundColor Yellow
