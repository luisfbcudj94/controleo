using System.Text.Json;
using Azure.Core.Serialization;
using Controleo.Application;
using Controleo.Infrastructure;
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

        // Clean Architecture layers
        services.AddApplicationServices();
        services.AddInfrastructureServices(context.Configuration);
    })
    .Build();

host.Run();
