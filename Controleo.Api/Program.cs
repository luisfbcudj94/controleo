using System.Text.Json;
using Azure.Core.Serialization;
using Controleo.Api.Options;
using Controleo.Api.Services;
using Controleo.Api.Services.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, configurationBuilder) =>
    {
        configurationBuilder
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.Configure<WorkerOptions>(workerOptions =>
        {
            var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            workerOptions.Serializer = new JsonObjectSerializer(serializerOptions);
        });

        services.Configure<CosmosStorageOptions>(context.Configuration.GetSection(CosmosStorageOptions.SectionName));
        services.PostConfigure<CosmosStorageOptions>(options =>
        {
            options.MovementTypes = NormalizeCatalog(options.MovementTypes, GetDefaultMovementTypes());
            options.PaymentMethods = NormalizeCatalog(options.PaymentMethods, GetDefaultPaymentMethods());
        });

        services.Configure<LocalAuthOptions>(context.Configuration.GetSection(LocalAuthOptions.SectionName));

        services.AddSingleton<IExpenseStorageService, CosmosExpenseStorageService>();
        services.AddSingleton<IUserAuthService, LocalUserAuthService>();
        services.AddSingleton<IAccessTokenValidator, JwtAccessTokenValidator>();
    })
    .Build();

host.Run();

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
