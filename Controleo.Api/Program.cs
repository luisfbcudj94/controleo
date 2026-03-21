using Controleo.Api.Models;
using Controleo.Api.Options;
using Controleo.Api.Services;
using Microsoft.Extensions.Options;

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

app.MapGet("/api/catalogs", (IOptions<FirebaseStorageOptions> options) =>
{
    var config = options.Value;
    return Results.Ok(new ExpenseCatalog(config.MovementTypes, config.PaymentMethods));
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

    if (request.Amount <= 0)
    {
        errors["amount"] = ["El valor debe ser mayor a cero."];
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
