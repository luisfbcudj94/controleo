using Controleo.Api.Models;
using Controleo.Api.Options;
using Controleo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<FirebaseStorageOptions>(builder.Configuration.GetSection(FirebaseStorageOptions.SectionName));
builder.Services.PostConfigure<FirebaseStorageOptions>(options =>
{
    options.MovementTypes = NormalizeCatalog(options.MovementTypes, GetDefaultMovementTypes());
    options.PaymentMethods = NormalizeCatalog(options.PaymentMethods, GetDefaultPaymentMethods());
});
builder.Services.AddSingleton<IExpenseStorageService, FirestoreExpenseStorageService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapGet("/api/catalogs", async (IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    var catalog = await expenseStorageService.GetCatalogsAsync(cancellationToken);
    return Results.Ok(catalog);
});

app.MapPut("/api/catalogs", async (UpdateCatalogsRequest request, IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    if (request.MovementTypes is null || request.PaymentMethods is null)
    {
        return Results.BadRequest(new OperationResult(false, "Debes enviar secciones y medios de pago."));
    }

    var result = await expenseStorageService.UpdateCatalogsAsync(request, cancellationToken);
    return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/api/expenses", async (string? month, IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    if (!TryResolveMonth(month, out var monthKey))
    {
        return Results.BadRequest(new OperationResult(false, "Mes inválido. Usa formato yyyy-MM."));
    }

    var data = await expenseStorageService.GetExpensesAsync(monthKey, cancellationToken);
    return Results.Ok(data);
});

app.MapPost("/api/expenses", async (ExpenseEntryRequest request, IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    var validationErrors = ValidateRequest(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    var result = await expenseStorageService.SaveAsync(request, cancellationToken);
    return result.IsSuccess
        ? Results.Created($"/api/expenses/{result.RowNumber}", result)
        : Results.BadRequest(result);
});

app.MapPut("/api/expenses/{id}", async (string id, ExpenseEntryRequest request, IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    var validationErrors = ValidateRequest(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    var result = await expenseStorageService.UpdateExpenseAsync(id, request, cancellationToken);
    return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapDelete("/api/expenses/{id}", async (string id, IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    var result = await expenseStorageService.DeleteExpenseAsync(id, cancellationToken);
    return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/api/budgets", async (string? month, IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    if (!TryResolveMonth(month, out var monthKey))
    {
        return Results.BadRequest(new OperationResult(false, "Mes inválido. Usa formato yyyy-MM."));
    }

    var data = await expenseStorageService.GetBudgetsAsync(monthKey, cancellationToken);
    return Results.Ok(data);
});

app.MapPut("/api/budgets/{movementType}", async (string movementType, string? month, BudgetUpsertRequest request, IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    if (!TryResolveMonth(month, out var monthKey))
    {
        return Results.BadRequest(new OperationResult(false, "Mes inválido. Usa formato yyyy-MM."));
    }

    var result = await expenseStorageService.UpsertBudgetAsync(movementType, monthKey, request, cancellationToken);
    return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapDelete("/api/budgets/{movementType}", async (string movementType, string? month, IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    if (!TryResolveMonth(month, out var monthKey))
    {
        return Results.BadRequest(new OperationResult(false, "Mes inválido. Usa formato yyyy-MM."));
    }

    var result = await expenseStorageService.DeleteBudgetAsync(movementType, monthKey, cancellationToken);
    return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/api/dashboard/by-category", async (string? month, IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
{
    if (!TryResolveMonth(month, out var monthKey))
    {
        return Results.BadRequest(new OperationResult(false, "Mes inválido. Usa formato yyyy-MM."));
    }

    var data = await expenseStorageService.GetDashboardByCategoryAsync(monthKey, cancellationToken);
    return Results.Ok(data);
});

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/admin/purge-data", async (IExpenseStorageService expenseStorageService, CancellationToken cancellationToken) =>
    {
        var result = await expenseStorageService.PurgeAllDataAsync(cancellationToken);
        return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
    });
}

app.Run();

static Dictionary<string, string[]> ValidateRequest(ExpenseEntryRequest request)
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

static string[] NormalizeCatalog(string[]? configuredValues, string[] fallbackValues)
{
    var source = configuredValues is { Length: > 0 }
        ? configuredValues
        : fallbackValues;

    return source
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string[] GetDefaultMovementTypes() =>
[
    "Basicos para vivir",
    "Hogar",
    "Salidas",
    "Imprevistos",
    "Suscripciones",
    "Deudas",
    "-",
    "Prestamo",
    "Bienestar",
    "Viajes",
    "Sogamoso obra"
];

static string[] GetDefaultPaymentMethods() =>
[
    "TC Black",
    "TC Rappi",
    "TD Bancolombia",
    "TC Nu",
    "Efectivo",
    "Transferencia",
    "-",
    "Nequi"
];

static bool TryResolveMonth(string? month, out string monthKey)
{
    if (string.IsNullOrWhiteSpace(month))
    {
        monthKey = DateTime.UtcNow.ToString("yyyy-MM");
        return true;
    }

    if (DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedMonth))
    {
        monthKey = parsedMonth.ToString("yyyy-MM");
        return true;
    }

    monthKey = string.Empty;
    return false;
}
