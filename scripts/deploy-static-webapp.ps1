param(
    [string]$Subscription = "1abed9cd-15d3-45b3-b22a-bb7ed63d30bb",
    [string]$ResourceGroup = "projects",
    [string]$StaticWebAppName = "controleo-web",
    [string]$Location = "centralus",
    [string]$WebProjectPath = "C:\Users\v-tgeethanat\Desktop\controleo\Controleo.Web",
    [string]$OutputFolder = "dist\controleo.web\browser",
    [switch]$EnsureCreated
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
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
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "No se encontró el comando '$Name'."
    }
}

Write-Host "==> Validando herramientas" -ForegroundColor Cyan
Require-Command -Name "az"
Require-Command -Name "npm"
Require-Command -Name "npx"

Invoke-Step -Description "Seleccionando suscripción" -Command {
    az account set --subscription $Subscription
}

if ($EnsureCreated) {
    $existing = az staticwebapp show --resource-group $ResourceGroup --name $StaticWebAppName --query name -o tsv 2>$null

    if ([string]::IsNullOrWhiteSpace($existing)) {
        Invoke-Step -Description "Creando Static Web App Free" -Command {
            az staticwebapp create --resource-group $ResourceGroup --name $StaticWebAppName --location $Location --sku Free --output none
        }
    }
}

Invoke-Step -Description "Build Angular producción (usa endpoint Azure)" -Command {
    npm run build --prefix $WebProjectPath -- --configuration production
}

$fullOutputPath = Join-Path $WebProjectPath $OutputFolder
if (-not (Test-Path $fullOutputPath)) {
    throw "No existe el folder de salida: $fullOutputPath"
}

$deploymentToken = az staticwebapp secrets list --resource-group $ResourceGroup --name $StaticWebAppName --query "properties.apiKey" -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($deploymentToken)) {
    throw "No se pudo obtener el deployment token de Static Web App."
}

Invoke-Step -Description "Publicando en Static Web App" -Command {
    npx --yes @azure/static-web-apps-cli deploy $fullOutputPath --deployment-token $deploymentToken --env production
}

$url = az staticwebapp show --resource-group $ResourceGroup --name $StaticWebAppName --query defaultHostname -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($url)) {
    throw "No se pudo obtener el hostname del Static Web App."
}

Write-Host ""
Write-Host "Deploy frontend completado." -ForegroundColor Green
Write-Host "URL: https://$url" -ForegroundColor Green
