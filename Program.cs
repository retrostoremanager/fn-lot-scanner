using Anthropic.SDK;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using fn_lot_scanner.Middleware;
using fn_lot_scanner.Repos;
using fn_lot_scanner.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        builder.UseMiddleware<JwtAuthenticationMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        var cfg = context.Configuration;

        var connectionString = cfg["ConnectionStrings:lotscanner"]
            ?? cfg["ConnectionStrings__lotscanner"]
            ?? "Host=localhost;Database=lotscanner;Username=postgres;Password=postgres";
        services.AddDbContext<LotScanDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.Configure<ApiGamedbOptions>(options =>
        {
            cfg.GetSection(ApiGamedbOptions.SectionName).Bind(options);
            if (string.IsNullOrWhiteSpace(options.BaseUrl) || options.BaseUrl == new ApiGamedbOptions().BaseUrl)
                options.BaseUrl = cfg["ApiGamedb__BaseUrl"] ?? options.BaseUrl;
        });

        services.Configure<MyStoreApiOptions>(options =>
        {
            cfg.GetSection(MyStoreApiOptions.SectionName).Bind(options);
            if (string.IsNullOrWhiteSpace(options.BaseUrl) || options.BaseUrl == new MyStoreApiOptions().BaseUrl)
                options.BaseUrl = cfg["MyStoreApi__BaseUrl"] ?? options.BaseUrl;
        });

        services.Configure<AnthropicOptions>(options =>
        {
            cfg.GetSection(AnthropicOptions.SectionName).Bind(options);
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                options.ApiKey = cfg["Anthropic__ApiKey"] ?? string.Empty;
        });

        services.Configure<BlobOptions>(options =>
        {
            cfg.GetSection(BlobOptions.SectionName).Bind(options);
            if (string.IsNullOrWhiteSpace(options.ConnectionString) || options.ConnectionString == new BlobOptions().ConnectionString)
                options.ConnectionString = cfg["Blob__ConnectionString"] ?? options.ConnectionString;
            if (string.IsNullOrWhiteSpace(options.ContainerName) || options.ContainerName == new BlobOptions().ContainerName)
                options.ContainerName = cfg["Blob__ContainerName"] ?? options.ContainerName;
        });

        services.AddHttpClient<ApiGamedbClient>((sp, client) =>
        {
            var baseUrl = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiGamedbOptions>>().Value.BaseUrl;
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<MyStoreBillingClient>((sp, client) =>
        {
            var baseUrl = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MyStoreApiOptions>>().Value.BaseUrl;
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton(sp =>
        {
            var apiKey = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AnthropicOptions>>().Value.ApiKey;
            return string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient(apiKey);
        });

        var queueConnectionString = cfg["AzureWebJobsStorage"] ?? "UseDevelopmentStorage=true";
        services.AddSingleton(_ =>
        {
            // The Functions queue-trigger extension expects base64-encoded message
            // bodies (the classic Azure Queue Storage convention); the plain
            // Azure.Storage.Queues v12 SDK defaults to no encoding. Without this, every
            // message the extension tries to deliver fails to decode before our
            // trigger's Run() even starts, poisoning the queue silently within seconds
            // (found via a real end-to-end test, 2026-10-14).
            var queueClient = new QueueClient(queueConnectionString, "lot-scan-identify",
                new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
            queueClient.CreateIfNotExists();
            return queueClient;
        });

        services.AddScoped<ILotScanRepository, LotScanRepository>();
        services.AddScoped<IdentificationService>();
        services.AddSingleton<PhotoStorageService>();
    })
    .Build();

host.Run();
