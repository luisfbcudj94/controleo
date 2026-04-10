using System.Net;
using System.Globalization;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Application.Validation;
using Controleo.Domain.Common;
using Controleo.Domain.Interfaces;
using Controleo.Infrastructure.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Controleo.Api.Functions;

public sealed class ExpenseApiFunctions
{
    private readonly IExpenseService _expenseService;
    private readonly ICatalogService _catalogService;
    private readonly IBudgetService _budgetService;
    private readonly IDashboardService _dashboardService;
    private readonly IRecurringExpenseService _recurringService;
    private readonly IObligationService _obligationService;
    private readonly IReportExportService _reportExportService;
    private readonly IAccessTokenValidator _tokenValidator;
    private readonly LocalAuthOptions _authOptions;
    private readonly IHostEnvironment _env;

    public ExpenseApiFunctions(
        IExpenseService expenseService, ICatalogService catalogService, IBudgetService budgetService,
        IDashboardService dashboardService, IRecurringExpenseService recurringService, IObligationService obligationService,
        IReportExportService reportExportService,
        IAccessTokenValidator tokenValidator, IOptions<LocalAuthOptions> authOptions, IHostEnvironment env)
    {
        _expenseService = expenseService; _catalogService = catalogService; _budgetService = budgetService;
        _dashboardService = dashboardService; _recurringService = recurringService; _obligationService = obligationService;
        _reportExportService = reportExportService;
        _tokenValidator = tokenValidator; _authOptions = authOptions.Value; _env = env;
    }

    private Task<(ApiUserContext? User, HttpResponseData? Response)> AuthAsync(HttpRequestData req, CancellationToken ct)
        => FunctionHelpers.AuthorizeAsync(req, _tokenValidator, _authOptions, _env.IsDevelopment(), ct);

    private static async Task<HttpResponseData?> EnsurePremiumAsync(HttpRequestData req, ApiUserContext user, string featureName, CancellationToken ct)
    {
        if (user.IsPremium)
        {
            return null;
        }

        return await FunctionHelpers.JsonAsync(
            req,
            HttpStatusCode.Forbidden,
            new OperationResult(false, $"{featureName} está disponible solo para usuarios premium."),
            ct);
    }

    private static bool TryResolveDateRange(
        Dictionary<string, string> query,
        out DateOnly startDate,
        out DateOnly endDate,
        out string error)
    {
        startDate = default;
        endDate = default;

        var fromRaw = query.TryGetValue("from", out var fromToken)
            ? fromToken
            : query.TryGetValue("startDate", out var startToken)
                ? startToken
                : null;
        var toRaw = query.TryGetValue("to", out var toToken)
            ? toToken
            : query.TryGetValue("endDate", out var endToken)
                ? endToken
                : null;

        if (string.IsNullOrWhiteSpace(fromRaw) || string.IsNullOrWhiteSpace(toRaw))
        {
            error = "Debes enviar from y to en formato yyyy-MM-dd.";
            return false;
        }

        if (!DateOnly.TryParseExact(fromRaw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDate)
            || !DateOnly.TryParseExact(toRaw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out endDate))
        {
            error = "Formato de fecha inválido. Usa yyyy-MM-dd.";
            return false;
        }

        if (startDate > endDate)
        {
            error = "La fecha inicial no puede ser mayor que la fecha final.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static async Task<HttpResponseData> FileAsync(HttpRequestData req, ReportExportFile file, CancellationToken ct)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", file.ContentType);
        response.Headers.Add("Content-Disposition", $"attachment; filename*=UTF-8''{Uri.EscapeDataString(file.FileName)}");
        response.Headers.Add("Access-Control-Expose-Headers", "Content-Disposition");
        FunctionHelpers.AddCorsHeaders(response);
        await response.Body.WriteAsync(file.Content, ct);
        return response;
    }

    [Function("OptionsPreflight")]
    public HttpResponseData OptionsPreflight([HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "{*path}")] HttpRequestData req) => FunctionHelpers.NoContent(req);

    // ── Catalogs ──

    [Function("GetCatalogs")]
    public async Task<HttpResponseData> GetCatalogsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "catalogs")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _catalogService.GetCatalogsAsync(user!.UserId, ct), ct);
    }

    [Function("UpdateCatalogs")]
    public async Task<HttpResponseData> UpdateCatalogsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "catalogs")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var payload = await FunctionHelpers.ReadBodyAsync<UpdateCatalogsRequest>(req, ct);
        if (payload is null || payload.MovementTypes is null || payload.PaymentMethods is null)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Debes enviar secciones y medios de pago."), ct);
        var result = await _catalogService.UpdateCatalogsAsync(user!.UserId, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    // ── Expenses ──

    [Function("GetExpenses")]
    public async Task<HttpResponseData> GetExpensesAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var q = FunctionHelpers.ParseQuery(req);
        if (!FunctionHelpers.TryResolveMonth(q.TryGetValue("month", out var m) ? m : null, out var mk))
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Mes inválido."), ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _expenseService.GetExpensesAsync(user!.UserId, mk, ct), ct);
    }

    [Function("GetExpensesPaged")]
    public async Task<HttpResponseData> GetExpensesPagedAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/paged")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var q = FunctionHelpers.ParseQuery(req);
        if (!FunctionHelpers.TryResolveMonth(q.TryGetValue("month", out var m) ? m : null, out var mk))
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Mes inválido."), ct);

        var pn = q.TryGetValue("pageNumber", out var pnr) && int.TryParse(pnr, out var ppn) ? Math.Max(ppn, 1) : 1;
        var ps = q.TryGetValue("pageSize", out var psr) && int.TryParse(psr, out var pps) ? FunctionHelpers.NormalizePageSize(pps) : 5;
        var mt = q.TryGetValue("movementType", out var mtr) ? mtr : null;
        var pm = q.TryGetValue("paymentMethod", out var pmr) ? pmr : null;
        var st = q.TryGetValue("searchTerm", out var str) ? str : null;

        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _expenseService.GetExpensesPageAsync(user!.UserId, mk, pn, ps, mt, pm, st, ct), ct);
    }

    [Function("GetExpenseMonths")]
    public async Task<HttpResponseData> GetMonthsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/months")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _expenseService.GetAvailableMonthsAsync(user!.UserId, ct), ct);
    }

    [Function("SaveExpense")]
    public async Task<HttpResponseData> SaveAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var payload = await FunctionHelpers.ReadExpenseEntryRequestAsync(req, ct);
        if (payload is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);
        var ve = ExpenseValidator.Validate(payload);
        if (ve.Count > 0) return await FunctionHelpers.ValidationProblemAsync(req, ve, ct);
        var result = await _expenseService.SaveExpenseAsync(user!.UserId, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.Created : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("UpdateExpense")]
    public async Task<HttpResponseData> UpdateAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/{id}")] HttpRequestData req, string id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var payload = await FunctionHelpers.ReadExpenseEntryRequestAsync(req, ct);
        if (payload is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);
        var ve = ExpenseValidator.Validate(payload);
        if (ve.Count > 0) return await FunctionHelpers.ValidationProblemAsync(req, ve, ct);
        var result = await _expenseService.UpdateExpenseAsync(user!.UserId, id, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("DeleteExpense")]
    public async Task<HttpResponseData> DeleteAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/{id}")] HttpRequestData req, string id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var result = await _expenseService.DeleteExpenseAsync(user!.UserId, id, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("CountExpensesByMovementType")]
    public async Task<HttpResponseData> CountByTypeAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/count-by-type/{movementType}")] HttpRequestData req, string movementType, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var count = await _expenseService.CountByMovementTypeAsync(user!.UserId, Uri.UnescapeDataString(movementType), ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, new { count }, ct);
    }

    [Function("DeleteAllByMovementType")]
    public async Task<HttpResponseData> DeleteAllByTypeAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "movement-types/{movementType}")] HttpRequestData req, string movementType, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var result = await _expenseService.DeleteAllByMovementTypeAsync(user!.UserId, Uri.UnescapeDataString(movementType), ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    // ── Budgets ──

    [Function("GetBudgets")]
    public async Task<HttpResponseData> GetBudgetsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budgets")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _budgetService.GetBudgetsAsync(user!.UserId, ct), ct);
    }

    [Function("UpsertBudget")]
    public async Task<HttpResponseData> UpsertBudgetAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "budgets/{movementType}")] HttpRequestData req, string movementType, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var payload = await FunctionHelpers.ReadBodyAsync<BudgetUpsertRequest>(req, ct);
        if (payload is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);
        var result = await _budgetService.UpsertBudgetAsync(user!.UserId, movementType, payload.Amount, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("DeleteBudget")]
    public async Task<HttpResponseData> DeleteBudgetAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budgets/{movementType}")] HttpRequestData req, string movementType, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var result = await _budgetService.DeleteBudgetAsync(user!.UserId, movementType, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    // ── Dashboard ──

    [Function("GetDashboardByCategory")]
    public async Task<HttpResponseData> GetDashboardAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/by-category")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var q = FunctionHelpers.ParseQuery(req);
        if (!FunctionHelpers.TryResolveMonth(q.TryGetValue("month", out var m) ? m : null, out var mk))
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Mes inválido."), ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _dashboardService.GetDashboardByCategoryAsync(user!.UserId, mk, ct), ct);
    }

    [Function("GetDashboardByPaymentMethod")]
    public async Task<HttpResponseData> GetDashboardByPaymentMethodAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/by-payment-method")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var q = FunctionHelpers.ParseQuery(req);
        if (!FunctionHelpers.TryResolveMonth(q.TryGetValue("month", out var m) ? m : null, out var mk))
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Mes inválido."), ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _dashboardService.GetDashboardByPaymentMethodAsync(user!.UserId, mk, ct), ct);
    }

    // ── Reports (Premium) ──

    [Function("GetReportPreview")]
    public async Task<HttpResponseData> GetReportPreviewAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/preview")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var premiumErr = await EnsurePremiumAsync(req, user!, "Analitica avanzada", ct); if (premiumErr is not null) return premiumErr;

        var q = FunctionHelpers.ParseQuery(req);
        if (!TryResolveDateRange(q, out var startDate, out var endDate, out var dateError))
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, dateError), ct);

        try
        {
            var preview = await _reportExportService.BuildPreviewAsync(user!.UserId, startDate, endDate, ct);
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, preview, ct);
        }
        catch (ArgumentException ex)
        {
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, ex.Message), ct);
        }
    }

    [Function("ExportReportCsv")]
    public async Task<HttpResponseData> ExportReportCsvAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/export/csv")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var premiumErr = await EnsurePremiumAsync(req, user!, "Analitica avanzada", ct); if (premiumErr is not null) return premiumErr;

        var q = FunctionHelpers.ParseQuery(req);
        if (!TryResolveDateRange(q, out var startDate, out var endDate, out var dateError))
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, dateError), ct);

        try
        {
            var file = await _reportExportService.BuildCsvAsync(user!.UserId, startDate, endDate, ct);
            return await FileAsync(req, file, ct);
        }
        catch (ArgumentException ex)
        {
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, ex.Message), ct);
        }
    }

    [Function("ExportReportPdf")]
    public async Task<HttpResponseData> ExportReportPdfAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/export/pdf")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var premiumErr = await EnsurePremiumAsync(req, user!, "Analitica avanzada", ct); if (premiumErr is not null) return premiumErr;

        var q = FunctionHelpers.ParseQuery(req);
        if (!TryResolveDateRange(q, out var startDate, out var endDate, out var dateError))
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, dateError), ct);

        try
        {
            var file = await _reportExportService.BuildPdfAsync(user!.UserId, startDate, endDate, ct);
            return await FileAsync(req, file, ct);
        }
        catch (ArgumentException ex)
        {
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, ex.Message), ct);
        }
    }

    // ── Recurring ──

    [Function("GetRecurringExpenses")]
    public async Task<HttpResponseData> GetRecurringAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recurring-expenses")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _recurringService.GetRecurringExpensesAsync(user!.UserId, ct), ct);
    }

    [Function("GetRecurringExpensesPaged")]
    public async Task<HttpResponseData> GetRecurringPagedAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recurring-expenses/paged")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var q = FunctionHelpers.ParseQuery(req);
        var pn = q.TryGetValue("pageNumber", out var pnr) && int.TryParse(pnr, out var ppn) ? Math.Max(ppn, 1) : 1;
        var ps = q.TryGetValue("pageSize", out var psr) && int.TryParse(psr, out var pps) ? FunctionHelpers.NormalizePageSize(pps) : 5;

        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _recurringService.GetRecurringExpensesPageAsync(user!.UserId, pn, ps, ct), ct);
    }

    [Function("CreateRecurringExpense")]
    public async Task<HttpResponseData> CreateRecurringAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recurring-expenses")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var payload = await FunctionHelpers.ReadRecurringExpenseRequestAsync(req, ct);
        if (payload is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);
        var result = await _recurringService.UpsertRecurringExpenseAsync(user!.UserId, null, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.Created : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("UpdateRecurringExpense")]
    public async Task<HttpResponseData> UpdateRecurringAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "recurring-expenses/{id}")] HttpRequestData req, string id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var payload = await FunctionHelpers.ReadRecurringExpenseRequestAsync(req, ct);
        if (payload is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);
        var result = await _recurringService.UpsertRecurringExpenseAsync(user!.UserId, id, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("DeleteRecurringExpense")]
    public async Task<HttpResponseData> DeleteRecurringAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "recurring-expenses/{id}")] HttpRequestData req, string id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var result = await _recurringService.DeleteRecurringExpenseAsync(user!.UserId, id, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    // ── Obligations ──

    [Function("GetObligations")]
    public async Task<HttpResponseData> GetObligationsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "obligations")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var premiumErr = await EnsurePremiumAsync(req, user!, "Obligaciones", ct); if (premiumErr is not null) return premiumErr;
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _obligationService.GetObligationsAsync(user!.UserId, ct), ct);
    }

    [Function("GetObligationsPaged")]
    public async Task<HttpResponseData> GetObligationsPagedAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "obligations/paged")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var premiumErr = await EnsurePremiumAsync(req, user!, "Obligaciones", ct); if (premiumErr is not null) return premiumErr;
        var q = FunctionHelpers.ParseQuery(req);
        var pn = q.TryGetValue("pageNumber", out var pnr) && int.TryParse(pnr, out var ppn) ? Math.Max(ppn, 1) : 1;
        var ps = q.TryGetValue("pageSize", out var psr) && int.TryParse(psr, out var pps) ? FunctionHelpers.NormalizePageSize(pps) : 5;
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, await _obligationService.GetObligationsPageAsync(user!.UserId, pn, ps, ct), ct);
    }

    [Function("CreateObligation")]
    public async Task<HttpResponseData> CreateObligationAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "obligations")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var premiumErr = await EnsurePremiumAsync(req, user!, "Obligaciones", ct); if (premiumErr is not null) return premiumErr;
        var payload = await FunctionHelpers.ReadBodyAsync<ObligationUpsertRequest>(req, ct);
        if (payload is null)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);
        var result = await _obligationService.UpsertObligationAsync(user!.UserId, null, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.Created : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("UpdateObligation")]
    public async Task<HttpResponseData> UpdateObligationAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "obligations/{id}")] HttpRequestData req, string id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var premiumErr = await EnsurePremiumAsync(req, user!, "Obligaciones", ct); if (premiumErr is not null) return premiumErr;
        var payload = await FunctionHelpers.ReadBodyAsync<ObligationUpsertRequest>(req, ct);
        if (payload is null)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);
        var result = await _obligationService.UpsertObligationAsync(user!.UserId, id, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("DeleteObligation")]
    public async Task<HttpResponseData> DeleteObligationAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "obligations/{id}")] HttpRequestData req, string id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct); if (err is not null) return err;
        var premiumErr = await EnsurePremiumAsync(req, user!, "Obligaciones", ct); if (premiumErr is not null) return premiumErr;
        var result = await _obligationService.DeleteObligationAsync(user!.UserId, id, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }
}
