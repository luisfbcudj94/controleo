param(
    [string]$Subscription = "1abed9cd-15d3-45b3-b22a-bb7ed63d30bb",
    [string]$ResourceGroup = "projects",
    [string]$FunctionAppName = "controleo-api",
    [string]$StaticWebAppName = "controleo-web",
    [string]$CosmosAccountName = "controleocosmos262c4",
    [switch]$SaveJson
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Get-AzJson([string]$Command) {
    $result = Invoke-Expression $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Falló comando: $Command"
    }

    if ([string]::IsNullOrWhiteSpace($result)) {
        return $null
    }

    return $result | ConvertFrom-Json
}

function Try-GetMetricTotal(
    [string]$ResourceId,
    [string]$MetricName,
    [string]$Aggregation,
    [string]$StartTime,
    [string]$EndTime,
    [string]$Interval = "PT1H"
) {
    try {
        $json = az monitor metrics list `
            --resource $ResourceId `
            --metric $MetricName `
            --aggregation $Aggregation `
            --interval $Interval `
            --start-time $StartTime `
            --end-time $EndTime `
            -o json | ConvertFrom-Json

        if (-not $json.value -or $json.value.Count -eq 0) {
            return $null
        }

        $points = @($json.value[0].timeseries[0].data)
        if ($Aggregation -eq "Total") {
            return [math]::Round(($points | Where-Object { $_.total -ne $null } | Measure-Object -Property total -Sum).Sum, 2)
        }

        if ($Aggregation -eq "Average") {
            $avgPoints = $points | Where-Object { $_.average -ne $null } | Select-Object -ExpandProperty average
            if (-not $avgPoints -or $avgPoints.Count -eq 0) {
                return $null
            }

            return [math]::Round((($avgPoints | Measure-Object -Average).Average), 2)
        }

        return $null
    }
    catch {
        return $null
    }
}

function Get-RiskLabel([string]$Service, [hashtable]$Facts) {
    switch ($Service) {
        "StaticWebApp" {
            if ($Facts.Sku -eq "Free") { return "LOW" }
            return "HIGH"
        }
        "Function" {
            if ($Facts.PlanSku -eq "FC1" -and $Facts.AlwaysReadyCount -eq 0 -and $Facts.MaxInstances -le 1) { return "LOW" }
            if ($Facts.PlanSku -eq "FC1") { return "MEDIUM" }
            return "HIGH"
        }
        "Cosmos" {
            if ($Facts.FreeTier -and $Facts.Throughput -le 1000) { return "LOW" }
            if ($Facts.FreeTier) { return "MEDIUM" }
            return "HIGH"
        }
        default { return "UNKNOWN" }
    }
}

Write-Step "Seleccionando suscripción"
az account set --subscription $Subscription | Out-Null

Write-Step "Leyendo estado de recursos"
$static = Get-AzJson "az staticwebapp show -g '$ResourceGroup' -n '$StaticWebAppName' -o json"
$function = Get-AzJson "az functionapp show -g '$ResourceGroup' -n '$FunctionAppName' -o json"
$scale = Get-AzJson "az functionapp scale config show -g '$ResourceGroup' -n '$FunctionAppName' -o json"
$cosmos = Get-AzJson "az cosmosdb show -g '$ResourceGroup' -n '$CosmosAccountName' -o json"
$dbThroughput = Get-AzJson "az cosmosdb sql database throughput show -g '$ResourceGroup' -a '$CosmosAccountName' -n 'controleo' -o json"
$resources = Get-AzJson "az resource list -g '$ResourceGroup' -o json"

$functionPlanSku = ($resources | Where-Object { $_.type -eq 'Microsoft.Web/serverfarms' } | Select-Object -First 1).sku.name
if ([string]::IsNullOrWhiteSpace($functionPlanSku)) {
    $functionPlanSku = "UNKNOWN"
}

Write-Step "Consultando métricas últimas 24h"
$nowUtc = (Get-Date).ToUniversalTime()
$startUtc = $nowUtc.AddDays(-1)
$startTime = $startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
$endTime = $nowUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")

$functionMetricDefs = @()
try {
    $functionMetricDefs = az monitor metrics list-definitions --resource $function.id -o json | ConvertFrom-Json
}
catch {
    $functionMetricDefs = @()
}

$functionMetricNames = @($functionMetricDefs | ForEach-Object { $_.name.value })

$functionRequests24h = $null
if ($functionMetricNames -contains "Requests") {
    $functionRequests24h = Try-GetMetricTotal -ResourceId $function.id -MetricName "Requests" -Aggregation "Total" -StartTime $startTime -EndTime $endTime
}

$functionDataOut24h = $null
if ($functionMetricNames -contains "BytesSent") {
    $functionDataOut24h = Try-GetMetricTotal -ResourceId $function.id -MetricName "BytesSent" -Aggregation "Total" -StartTime $startTime -EndTime $endTime
}

$functionAvgMemory24h = $null
if ($functionMetricNames -contains "AverageMemoryWorkingSet") {
    $functionAvgMemory24h = Try-GetMetricTotal -ResourceId $function.id -MetricName "AverageMemoryWorkingSet" -Aggregation "Average" -StartTime $startTime -EndTime $endTime
}

$functionAvgInstances24h = $null
if ($functionMetricNames -contains "InstanceCount") {
    $functionAvgInstances24h = Try-GetMetricTotal -ResourceId $function.id -MetricName "InstanceCount" -Aggregation "Average" -StartTime $startTime -EndTime $endTime
}

$cosmosRu24h = Try-GetMetricTotal -ResourceId $cosmos.id -MetricName "TotalRequestUnits" -Aggregation "Total" -StartTime $startTime -EndTime $endTime
$cosmosDataUsage = Try-GetMetricTotal -ResourceId $cosmos.id -MetricName "DataUsage" -Aggregation "Average" -StartTime $startTime -EndTime $endTime

Write-Step "Consultando costo acumulado del mes (si el tenant lo permite)"
$monthStart = Get-Date -Day 1 -Hour 0 -Minute 0 -Second 0
$monthEnd = Get-Date
$costMtd = $null
try {
    $usage = az consumption usage list `
        --start-date $monthStart.ToString("yyyy-MM-dd") `
        --end-date $monthEnd.ToString("yyyy-MM-dd") `
        --top 1000 `
        --query "[?contains(instanceName, '$FunctionAppName') || contains(instanceName, '$CosmosAccountName') || contains(instanceName, '$StaticWebAppName') || contains(instanceName, '$ResourceGroup')].pretaxCost" `
        -o json | ConvertFrom-Json

    if ($usage) {
        $costMtd = [math]::Round((($usage | Measure-Object -Sum).Sum), 4)
    }
}
catch {
    $costMtd = $null
}

$staticFacts = @{
    Sku = $static.sku.name
}
$functionFacts = @{
    PlanSku = $functionPlanSku
    AlwaysReadyCount = @($scale.alwaysReady).Count
    MaxInstances = $scale.maximumInstanceCount
}
$cosmosFacts = @{
    FreeTier = [bool]$cosmos.enableFreeTier
    Throughput = [int]$dbThroughput.resource.throughput
}

$staticRisk = Get-RiskLabel -Service "StaticWebApp" -Facts $staticFacts
$functionRisk = Get-RiskLabel -Service "Function" -Facts $functionFacts
$cosmosRisk = Get-RiskLabel -Service "Cosmos" -Facts $cosmosFacts

$report = [ordered]@{
    generatedAtUtc = $nowUtc.ToString("o")
    subscription = $Subscription
    resourceGroup = $ResourceGroup
    staticWebApp = [ordered]@{
        name = $static.name
        sku = $static.sku.name
        defaultHostname = $static.defaultHostname
        risk = $staticRisk
    }
    functionApp = [ordered]@{
        name = $function.name
        kind = $function.kind
        planSku = $functionPlanSku
        maxInstances = $scale.maximumInstanceCount
        instanceMemoryMB = $scale.instanceMemoryMB
        alwaysReadyCount = @($scale.alwaysReady).Count
        availableMetrics = $functionMetricNames
        requestsLast24h = $functionRequests24h
        bytesSentLast24h = $functionDataOut24h
        averageMemoryWorkingSetLast24h = $functionAvgMemory24h
        averageInstanceCountLast24h = $functionAvgInstances24h
        risk = $functionRisk
    }
    cosmosDb = [ordered]@{
        name = $cosmos.name
        freeTier = [bool]$cosmos.enableFreeTier
        location = $cosmos.location
        database = "controleo"
        sharedThroughputRu = [int]$dbThroughput.resource.throughput
        totalRequestUnitsLast24h = $cosmosRu24h
        averageDataUsageMetricLast24h = $cosmosDataUsage
        risk = $cosmosRisk
    }
    monthToDateEstimatedCostUsd = $costMtd
}

Write-Host "`n================ FREE-TIER REPORT ================" -ForegroundColor Yellow
Write-Host ("Static Web App: {0} | SKU={1} | Risk={2}" -f $report.staticWebApp.name, $report.staticWebApp.sku, $report.staticWebApp.risk)
Write-Host ("Function App: {0} | SKU={1} | maxInstances={2} | alwaysReady={3} | req24h={4} | avgInstances24h={5} | avgMem24h={6} | Risk={7}" -f $report.functionApp.name, $report.functionApp.planSku, $report.functionApp.maxInstances, $report.functionApp.alwaysReadyCount, $report.functionApp.requestsLast24h, $report.functionApp.averageInstanceCountLast24h, $report.functionApp.averageMemoryWorkingSetLast24h, $report.functionApp.risk)
Write-Host ("Cosmos DB: {0} | freeTier={1} | RU={2} | RU24h={3} | Risk={4}" -f $report.cosmosDb.name, $report.cosmosDb.freeTier, $report.cosmosDb.sharedThroughputRu, $report.cosmosDb.totalRequestUnitsLast24h, $report.cosmosDb.risk)
if ($null -ne $report.monthToDateEstimatedCostUsd) {
    Write-Host ("MTD estimated cost (USD): {0}" -f $report.monthToDateEstimatedCostUsd)
}
else {
    Write-Host "MTD estimated cost (USD): no disponible (sin permisos/soporte del tenant)."
}
Write-Host "==================================================`n" -ForegroundColor Yellow

if ($SaveJson) {
    $reportDir = "C:\Users\v-tgeethanat\Desktop\controleo\artifacts"
    if (-not (Test-Path $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir | Out-Null
    }

    $path = Join-Path $reportDir ("free-tier-report-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
    $report | ConvertTo-Json -Depth 10 | Set-Content -Path $path -Encoding UTF8
    Write-Host "Reporte JSON guardado en: $path" -ForegroundColor Green
}
