using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Controleo.Api.Models;
using Controleo.Api.Options;
using Controleo.Api.Services;
using Controleo.Api.Services.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Controleo.Api.Functions;

public sealed class ExpenseApiFunctions
{
    private readonly IExpenseStorageService _expenseStorageService;
    private readonly IAccessTokenValidator _accessTokenValidator;
    private readonly LocalAuthOptions _authOptions;
    private readonly IHostEnvironment _hostEnvironment;

    public ExpenseApiFunctions(
        IExpenseStorageService expenseStorageService,
        IAccessTokenValidator accessTokenValidator,
        IOptions<LocalAuthOptions> authOptions,
        IHostEnvironment hostEnvironment)
    {
        _expenseStorageService = expenseStorageService;
        _accessTokenValidator = accessTokenValidator;
        _authOptions = authOptions.Value;
        _hostEnvironment = hostEnvironment;
    }

    [Function("OptionsPreflight")]
    public HttpResponseData OptionsPreflight(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "{*path}")] HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.NoContent);
        AddCorsHeaders(response);
        return response;
    }

    [Function("GetCatalogs")]
    public async Task<HttpResponseData> GetCatalogsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "catalogs")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var catalog = await _expenseStorageService.GetCatalogsAsync(auth.User!.UserId, cancellationToken);
        return await JsonAsync(request, HttpStatusCode.OK, catalog, cancellationToken);
    }

    [Function("UpdateCatalogs")]
    public async Task<HttpResponseData> UpdateCatalogsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "catalogs")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var payload = await ReadBodyAsync<UpdateCatalogsRequest>(request, cancellationToken);
        if (payload is null || payload.MovementTypes is null || payload.PaymentMethods is null)
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Debes enviar secciones y medios de pago."), cancellationToken);
        }

        var result = await _expenseStorageService.UpdateCatalogsAsync(auth.User!.UserId, payload, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    [Function("GetExpenses")]
    public async Task<HttpResponseData> GetExpensesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var query = ParseQuery(request);
        var month = query.TryGetValue("month", out var monthValue) ? monthValue : null;

        if (!TryResolveMonth(month, out var monthKey))
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Mes inválido. Usa formato yyyy-MM."), cancellationToken);
        }

        var data = await _expenseStorageService.GetExpensesAsync(auth.User!.UserId, monthKey, cancellationToken);
        return await JsonAsync(request, HttpStatusCode.OK, data, cancellationToken);
    }

    [Function("GetExpensesPaged")]
    public async Task<HttpResponseData> GetExpensesPagedAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/paged")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var query = ParseQuery(request);
        var month = query.TryGetValue("month", out var monthValue) ? monthValue : null;

        if (!TryResolveMonth(month, out var monthKey))
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Mes inválido. Usa formato yyyy-MM."), cancellationToken);
        }

        var pageNumber = query.TryGetValue("pageNumber", out var pageNumberRaw) && int.TryParse(pageNumberRaw, out var parsedPageNumber)
            ? parsedPageNumber
            : 1;

        var pageSize = query.TryGetValue("pageSize", out var pageSizeRaw) && int.TryParse(pageSizeRaw, out var parsedPageSize)
            ? parsedPageSize
            : 5;

        var movementType = query.TryGetValue("movementType", out var movementTypeRaw)
            ? movementTypeRaw
            : null;

        var searchTerm = query.TryGetValue("searchTerm", out var searchTermRaw)
            ? searchTermRaw
            : null;

        var resolvedPageNumber = pageNumber < 1 ? 1 : pageNumber;
        var resolvedPageSize = NormalizePageSize(pageSize);

        var data = await _expenseStorageService.GetExpensesPageAsync(auth.User!.UserId, monthKey, resolvedPageNumber, resolvedPageSize, movementType, searchTerm, cancellationToken);
        return await JsonAsync(request, HttpStatusCode.OK, data, cancellationToken);
    }

    [Function("GetExpenseMonths")]
    public async Task<HttpResponseData> GetAvailableMonthsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/months")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var data = await _expenseStorageService.GetAvailableMonthKeysAsync(auth.User!.UserId, cancellationToken);
        return await JsonAsync(request, HttpStatusCode.OK, data, cancellationToken);
    }

    [Function("SaveExpense")]
    public async Task<HttpResponseData> SaveExpenseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var payload = await ReadExpenseEntryRequestAsync(request, cancellationToken);
        if (payload is null)
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), cancellationToken);
        }

        var validationErrors = ValidateRequest(payload);
        if (validationErrors.Count > 0)
        {
            return await ValidationProblemAsync(request, validationErrors, cancellationToken);
        }

        var result = await _expenseStorageService.SaveAsync(auth.User!.UserId, payload, cancellationToken);
        if (!result.IsSuccess)
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, result, cancellationToken);
        }

        var response = await JsonAsync(request, HttpStatusCode.Created, result, cancellationToken);
        response.Headers.Add("Location", $"/api/expenses/{result.RowNumber}");
        return response;
    }

    [Function("UpdateExpense")]
    public async Task<HttpResponseData> UpdateExpenseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/{id}")] HttpRequestData request,
        string id,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var payload = await ReadExpenseEntryRequestAsync(request, cancellationToken);
        if (payload is null)
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), cancellationToken);
        }

        var validationErrors = ValidateRequest(payload);
        if (validationErrors.Count > 0)
        {
            return await ValidationProblemAsync(request, validationErrors, cancellationToken);
        }

        var result = await _expenseStorageService.UpdateExpenseAsync(auth.User!.UserId, id, payload, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    [Function("DeleteExpense")]
    public async Task<HttpResponseData> DeleteExpenseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/{id}")] HttpRequestData request,
        string id,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var result = await _expenseStorageService.DeleteExpenseAsync(auth.User!.UserId, id, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    [Function("CountExpensesByMovementType")]
    public async Task<HttpResponseData> CountExpensesByMovementTypeAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/count-by-type/{movementType}")] HttpRequestData request,
        string movementType,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var decoded = Uri.UnescapeDataString(movementType);
        var count = await _expenseStorageService.CountExpensesByMovementTypeAsync(auth.User!.UserId, decoded, cancellationToken);
        return await JsonAsync(request, HttpStatusCode.OK, new { count }, cancellationToken);
    }

    [Function("DeleteAllByMovementType")]
    public async Task<HttpResponseData> DeleteAllByMovementTypeAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "movement-types/{movementType}")] HttpRequestData request,
        string movementType,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var decoded = Uri.UnescapeDataString(movementType);
        var result = await _expenseStorageService.DeleteAllByMovementTypeAsync(auth.User!.UserId, decoded, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    [Function("GetBudgets")]
    public async Task<HttpResponseData> GetBudgetsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budgets")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var data = await _expenseStorageService.GetBudgetsAsync(auth.User!.UserId, cancellationToken);
        return await JsonAsync(request, HttpStatusCode.OK, data, cancellationToken);
    }

    [Function("UpsertBudget")]
    public async Task<HttpResponseData> UpsertBudgetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "budgets/{movementType}")] HttpRequestData request,
        string movementType,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var payload = await ReadBodyAsync<BudgetUpsertRequest>(request, cancellationToken);
        if (payload is null)
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), cancellationToken);
        }

        var result = await _expenseStorageService.UpsertBudgetAsync(auth.User!.UserId, movementType, payload, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    [Function("DeleteBudget")]
    public async Task<HttpResponseData> DeleteBudgetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budgets/{movementType}")] HttpRequestData request,
        string movementType,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var result = await _expenseStorageService.DeleteBudgetAsync(auth.User!.UserId, movementType, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    [Function("GetDashboardByCategory")]
    public async Task<HttpResponseData> GetDashboardByCategoryAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/by-category")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var query = ParseQuery(request);
        var month = query.TryGetValue("month", out var monthValue) ? monthValue : null;

        if (!TryResolveMonth(month, out var monthKey))
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Mes inválido. Usa formato yyyy-MM."), cancellationToken);
        }

        var data = await _expenseStorageService.GetDashboardByCategoryAsync(auth.User!.UserId, monthKey, cancellationToken);
        return await JsonAsync(request, HttpStatusCode.OK, data, cancellationToken);
    }

    [Function("GetRecurringExpenses")]
    public async Task<HttpResponseData> GetRecurringExpensesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recurring-expenses")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var data = await _expenseStorageService.GetRecurringExpensesAsync(auth.User!.UserId, cancellationToken);
        return await JsonAsync(request, HttpStatusCode.OK, data, cancellationToken);
    }

    [Function("CreateRecurringExpense")]
    public async Task<HttpResponseData> CreateRecurringExpenseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recurring-expenses")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var payload = await ReadBodyAsync<RecurringExpenseUpsertRequest>(request, cancellationToken);
        if (payload is null)
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), cancellationToken);
        }

        var result = await _expenseStorageService.UpsertRecurringExpenseAsync(auth.User!.UserId, null, payload, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.Created : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    [Function("UpdateRecurringExpense")]
    public async Task<HttpResponseData> UpdateRecurringExpenseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "recurring-expenses/{id}")] HttpRequestData request,
        string id,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var payload = await ReadBodyAsync<RecurringExpenseUpsertRequest>(request, cancellationToken);
        if (payload is null)
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), cancellationToken);
        }

        var result = await _expenseStorageService.UpsertRecurringExpenseAsync(auth.User!.UserId, id, payload, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    [Function("DeleteRecurringExpense")]
    public async Task<HttpResponseData> DeleteRecurringExpenseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "recurring-expenses/{id}")] HttpRequestData request,
        string id,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var result = await _expenseStorageService.DeleteRecurringExpenseAsync(auth.User!.UserId, id, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    [Function("PurgeData")]
    public async Task<HttpResponseData> PurgeDataAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dev/purge-data")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!_hostEnvironment.IsDevelopment())
        {
            var notFound = request.CreateResponse(HttpStatusCode.NotFound);
            AddCorsHeaders(notFound);
            return notFound;
        }

        var auth = await AuthorizeAsync(request, cancellationToken);
        if (auth.Response is not null)
        {
            return auth.Response;
        }

        var result = await _expenseStorageService.PurgeAllDataAsync(auth.User!.UserId, cancellationToken);
        return await JsonAsync(request, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
    }

    private async Task<(ApiUserContext? User, HttpResponseData? Response)> AuthorizeAsync(HttpRequestData request, CancellationToken cancellationToken)
    {
        var header = request.Headers.TryGetValues("Authorization", out var values)
            ? values.FirstOrDefault()
            : null;

        if (_authOptions.AllowAnonymousInDevelopment && _hostEnvironment.IsDevelopment() && string.IsNullOrWhiteSpace(header))
        {
            return (new ApiUserContext("dev-user", "dev-user", "Dev User", "dev@controleo.local"), null);
        }

        var validation = await _accessTokenValidator.ValidateAsync(header, cancellationToken);
        if (!validation.IsValid || validation.User is null)
        {
            var unauthorized = await JsonAsync(
                request,
                HttpStatusCode.Unauthorized,
                new OperationResult(false, string.IsNullOrWhiteSpace(validation.ErrorMessage) ? "Unauthorized" : validation.ErrorMessage),
                cancellationToken);

            return (null, unauthorized);
        }

        return (validation.User, null);
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpRequestData request, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await ReadRawBodyAsync(request, cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var normalized = NormalizeRawPayload(raw);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    var parsed = TryDeserialize<T>(normalized);
                    if (parsed is not null)
                    {
                        return parsed;
                    }

                    try
                    {
                        var root = JsonNode.Parse(normalized);
                        if (root is JsonValue valueNode && valueNode.TryGetValue<string>(out var innerJson))
                        {
                            var nestedParsed = TryDeserialize<T>(innerJson);
                            if (nestedParsed is not null)
                            {
                                return nestedParsed;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            return await request.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch
        {
            return default;
        }
    }

    private static async Task<ExpenseEntryRequest?> ReadExpenseEntryRequestAsync(HttpRequestData request, CancellationToken cancellationToken)
    {
        var raw = await ReadRawBodyAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalized = NormalizeRawPayload(raw);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var direct = TryDeserialize<ExpenseEntryRequest>(normalized);
        if (direct is not null)
        {
            return direct;
        }

        if (TryParseKeyValueBody(normalized, out var keyValues)
            && TryGetValue(keyValues, "date", out var dateText)
            && TryParseFlexibleDate(dateText, out var kvDate))
        {
            var kvDescription = TryGetValue(keyValues, "description", out var parsedDescription) ? parsedDescription : string.Empty;
            var kvMovementType = TryGetValue(keyValues, "movementType", out var parsedMovementType) ? parsedMovementType : string.Empty;
            var kvPaymentMethod = TryGetValue(keyValues, "paymentMethod", out var parsedPaymentMethod) ? parsedPaymentMethod : string.Empty;
            var kvAmount = TryGetValue(keyValues, "amount", out var amountRaw) && TryParseFlexibleAmount(amountRaw, out var parsedAmount)
                ? parsedAmount
                : 0;

            return new ExpenseEntryRequest(kvDate, kvDescription, kvAmount, kvMovementType, kvPaymentMethod);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(normalized);
        }
        catch
        {
            return null;
        }

        if (root is JsonValue valueNode && valueNode.TryGetValue<string>(out var innerJson))
        {
            direct = TryDeserialize<ExpenseEntryRequest>(innerJson);
            if (direct is not null)
            {
                return direct;
            }

            try
            {
                root = JsonNode.Parse(innerJson);
            }
            catch
            {
                return null;
            }
        }

        if (root is not JsonObject payload)
        {
            return null;
        }

        if (!TryReadDate(payload, out var date))
        {
            return null;
        }

        var description = ReadString(payload, "description");
        var movementType = ReadString(payload, "movementType");
        var paymentMethod = ReadString(payload, "paymentMethod");

        if (!TryReadAmount(payload, out var amount))
        {
            amount = 0;
        }

        return new ExpenseEntryRequest(date, description, amount, movementType, paymentMethod);
    }

    private static async Task<string> ReadRawBodyAsync(HttpRequestData request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeRawPayload(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            try
            {
                var node = JsonNode.Parse(trimmed);
                if (node is JsonValue valueNode && valueNode.TryGetValue<string>(out var inner) && !string.IsNullOrWhiteSpace(inner))
                {
                    return inner.Trim();
                }
            }
            catch
            {
            }
        }

        return trimmed;
    }

    private static bool TryParseKeyValueBody(string raw, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw) || !raw.Contains('=', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = raw.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var pivot = part.IndexOf('=');
            if (pivot <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..pivot]).Trim();
            var value = Uri.UnescapeDataString(part[(pivot + 1)..]).Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        return values.Count > 0;
    }

    private static bool TryGetValue(Dictionary<string, string> values, string key, out string value)
    {
        if (values.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch
        {
            return default;
        }
    }

    private static string ReadString(JsonObject payload, string propertyName)
    {
        var node = FindProperty(payload, propertyName);
        if (node is JsonValue valueNode && valueNode.TryGetValue<string>(out var text))
        {
            return text?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool TryReadDate(JsonObject payload, out DateOnly date)
    {
        var node = FindProperty(payload, "date");
        if (node is null)
        {
            date = default;
            return false;
        }

        if (node is JsonValue valueNode)
        {
            if (valueNode.TryGetValue<DateOnly>(out date))
            {
                return true;
            }

            if (valueNode.TryGetValue<DateTime>(out var dateTime))
            {
                date = DateOnly.FromDateTime(dateTime);
                return true;
            }

            if (valueNode.TryGetValue<string>(out var rawDate))
            {
                return TryParseFlexibleDate(rawDate, out date);
            }
        }

        if (node is JsonObject dateObject
            && TryReadInteger(dateObject, "year", out var year)
            && TryReadInteger(dateObject, "month", out var month)
            && TryReadInteger(dateObject, "day", out var day))
        {
            try
            {
                date = new DateOnly(year, month, day);
                return true;
            }
            catch
            {
            }
        }

        date = default;
        return false;
    }

    private static bool TryReadAmount(JsonObject payload, out decimal amount)
    {
        var node = FindProperty(payload, "amount");
        if (node is JsonValue valueNode)
        {
            if (valueNode.TryGetValue<decimal>(out amount))
            {
                return true;
            }

            if (valueNode.TryGetValue<double>(out var numberAsDouble))
            {
                amount = (decimal)numberAsDouble;
                return true;
            }

            if (valueNode.TryGetValue<string>(out var rawAmount))
            {
                return TryParseFlexibleAmount(rawAmount, out amount);
            }
        }

        amount = 0;
        return false;
    }

    private static bool TryParseFlexibleDate(string? value, out DateOnly date)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateOnly.TryParseExact(value, "dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"), DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDateTime))
        {
            date = DateOnly.FromDateTime(parsedDateTime);
            return true;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        return DateOnly.TryParse(value, out date);
    }

    private static bool TryParseFlexibleAmount(string? rawAmount, out decimal amount)
    {
        var normalized = (rawAmount ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            amount = 0;
            return false;
        }

        if (normalized.Contains('.', StringComparison.Ordinal)
            && !normalized.Contains(',', StringComparison.Ordinal)
            && IsLikelyThousandsGrouping(normalized))
        {
            var withoutGroupSeparator = normalized.Replace(".", string.Empty, StringComparison.Ordinal);
            if (decimal.TryParse(withoutGroupSeparator, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            {
                return true;
            }
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            return true;
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out amount))
        {
            return true;
        }

        var digitsOnly = new string(normalized.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digitsOnly))
        {
            amount = 0;
            return false;
        }

        return decimal.TryParse(digitsOnly, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static bool IsLikelyThousandsGrouping(string value)
    {
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
        {
            return false;
        }

        if (parts[0].Length is < 1 or > 3 || !parts[0].All(char.IsDigit))
        {
            return false;
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (parts[index].Length != 3 || !parts[index].All(char.IsDigit))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadInteger(JsonObject payload, string propertyName, out int value)
    {
        var node = FindProperty(payload, propertyName);
        if (node is JsonValue valueNode)
        {
            if (valueNode.TryGetValue<int>(out value))
            {
                return true;
            }

            if (valueNode.TryGetValue<string>(out var textValue) && int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static JsonNode? FindProperty(JsonObject payload, string propertyName)
    {
        foreach (var item in payload)
        {
            if (string.Equals(item.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return item.Value;
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseQuery(HttpRequestData request)
    {
        var parsed = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in parsed.AllKeys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = parsed[key] ?? string.Empty;
            }
        }

        return result;
    }

    private static async Task<HttpResponseData> JsonAsync(HttpRequestData request, HttpStatusCode statusCode, object payload, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(payload, cancellationToken: cancellationToken);
        response.StatusCode = statusCode;
        AddCorsHeaders(response);
        return response;
    }

    private static void AddCorsHeaders(HttpResponseData response)
    {
        if (!response.Headers.TryGetValues("Access-Control-Allow-Origin", out _))
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
        }

        if (!response.Headers.TryGetValues("Access-Control-Allow-Headers", out _))
        {
            response.Headers.Add("Access-Control-Allow-Headers", "authorization, content-type, x-requested-with");
        }

        if (!response.Headers.TryGetValues("Access-Control-Allow-Methods", out _))
        {
            response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");
        }
    }

    private static async Task<HttpResponseData> ValidationProblemAsync(HttpRequestData request, Dictionary<string, string[]> errors, CancellationToken cancellationToken)
    {
        var payload = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            title = "One or more validation errors occurred.",
            status = (int)HttpStatusCode.BadRequest,
            errors
        };

        return await JsonAsync(request, HttpStatusCode.BadRequest, payload, cancellationToken);
    }

    private static Dictionary<string, string[]> ValidateRequest(ExpenseEntryRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Date == default)
        {
            errors["date"] = ["La fecha es requerida."];
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            errors["description"] = ["La descripción es requerida."];
        }

        if (request.Amount == 0)
        {
            errors["amount"] = ["El valor debe ser diferente de cero."];
        }

        if (string.IsNullOrWhiteSpace(request.MovementType))
        {
            errors["movementType"] = ["El tipo de movimiento es requerido."];
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            errors["paymentMethod"] = ["El medio de pago es requerido."];
        }

        return errors;
    }

    private static bool TryResolveMonth(string? month, out string monthKey)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            monthKey = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return true;
        }

        if (DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedMonth))
        {
            monthKey = parsedMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return true;
        }

        monthKey = string.Empty;
        return false;
    }

    private static int NormalizePageSize(int value)
    {
        return value switch
        {
            10 => 10,
            20 => 20,
            _ => 5
        };
    }
}
