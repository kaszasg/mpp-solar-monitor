using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Solar;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.IncludeScopes = false;
    options.SingleLine = true;
    options.ColorBehavior = LoggerColorBehavior.Enabled;
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);
builder.Logging.AddFilter("Polly", LogLevel.Warning);

builder.Services.Configure<SolarOptions>(builder.Configuration.GetSection(SolarOptions.Section));
builder.Services.AddSingleton<InverterClient>();
builder.Services.AddHttpClient<OpenHabClient>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(2);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });

builder.Services.AddSystemd();
builder.Services.AddHostedService<SolarWorker>();

var host = builder.Build();
await host.RunAsync();
