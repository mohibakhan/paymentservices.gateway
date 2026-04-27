using System.Diagnostics.CodeAnalysis;
using Azure.Identity;
using FluentValidation;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentServices.Gateway.Models;
using PaymentServices.Gateway.Services;
using PaymentServices.Gateway.Validators;
using PaymentServices.Shared.Extensions;
using Serilog;
using Serilog.Events;

namespace PaymentServices.Gateway;

[ExcludeFromCodeCoverage]
public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(SetupAppConfiguration)
            .ConfigureFunctionsWebApplication()
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;

                SetupSerilog(config);

                // Application Insights
                services.AddApplicationInsightsTelemetryWorkerService();
                services.ConfigureFunctionsApplicationInsights();

                // Shared infrastructure — Cosmos, Service Bus, AppSettings
                services.AddPaymentAppSettings(config);
                services.AddPaymentCosmosClient(config, "gateway:AppSettings");
                services.AddPaymentServiceBusPublisher(config);

                // Cosmos containers needed by Gateway
                services.AddCosmosContainer(config,
                    config["gateway:AppSettings:COSMOS_TRANSACTIONS_CONTAINER"] ?? "tchSendTransactions",
                    "transactions");
                services.AddCosmosContainer(config,
                    config["gateway:AppSettings:COSMOS_IDEMPOTENCY_CONTAINER"] ?? "tchSendIdempotency",
                    "idempotency");

                // Gateway-specific settings
                services.AddOptions<GatewaySettings>()
                    .Configure<IConfiguration>((settings, cfg) =>
                        cfg.GetSection("gateway:AppSettings").Bind(settings));

                // Gateway services
                services.AddTransient<IIdempotencyService, IdempotencyService>();
                services.AddTransient<IGatewayService, GatewayService>();

                // FluentValidation
                services.AddValidatorsFromAssemblyContaining<TchSendRequestValidator>();

                // HTTP client factory (for any outbound calls)
                services.AddHttpClient();
                services.AddHttpContextAccessor();

                // Health checks
                services.AddHealthChecks();
            })
            .ConfigureLogging((context, logging) =>
            {
                // Remove default Application Insights logging rule to
                // allow Serilog to control the pipeline
                logging.Services.Configure<LoggerFilterOptions>(options =>
                {
                    var defaultRule = options.Rules.FirstOrDefault(rule =>
                        rule.ProviderName ==
                        "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");

                    if (defaultRule is not null)
                        options.Rules.Remove(defaultRule);
                });

                logging.AddSerilog(dispose: true);
            })
            .Build();

        await host.RunAsync();
    }

    // App Configuration — Managed Identity in Azure, local.settings.json locally
    private static void SetupAppConfiguration(IConfigurationBuilder builder)
    {
        // Pull environment variables first — needed to read AppConfig endpoint
        builder.AddEnvironmentVariables();
        var settings = builder.Build();

        var appConfigUrl = settings["AppConfig:Endpoint"];
        var azureClientId = settings["AZURE_CLIENT_ID"];

        if (!string.IsNullOrWhiteSpace(appConfigUrl) && !string.IsNullOrWhiteSpace(azureClientId))
        {
            // User-assigned Managed Identity
            var credentialOptions = new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = azureClientId
            };
            var credential = new DefaultAzureCredential(credentialOptions);

            builder.AddAzureAppConfiguration(options =>
            {
                options
                    .Connect(new Uri(appConfigUrl), credential)
                    .Select("gateway:*")
                    .Select("telemetry:*")
                    .ConfigureKeyVault(kv => kv.SetCredential(credential));
            });
        }

        // Local development fallback
        builder
            .SetBasePath(Environment.CurrentDirectory)
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: false);
    }

    // Serilog — Application Insights sink
    private static void SetupSerilog(IConfiguration config)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Azure.Functions.Worker", LogEventLevel.Warning)
            .MinimumLevel.Override("Host", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "PaymentServices.Gateway")
            .Enrich.WithProperty("Environment",
                Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Production")
            .WriteTo.ApplicationInsights(
                config["APPLICATIONINSIGHTS_CONNECTION_STRING"]
                    ?? $"InstrumentationKey={config["APPINSIGHTS_INSTRUMENTATIONKEY"]}",
                TelemetryConverter.Traces)
            .CreateLogger();
    }
}
