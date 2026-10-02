using Anthropic.SDK;
using Azure.Storage.Queues;
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

        services.AddSingleton(sp =>
        {
            var apiKey = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AnthropicOptions>>().Value.ApiKey;
            return string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient(apiKey);
        });

        var queueConnectionString = cfg["AzureWebJobsStorage"] ?? "UseDevelopmentStorage=true";
        services.AddSingleton(_ =>
        {
            var queueClient = new QueueClient(queueConnectionString, "lot-scan-identify");
            queueClient.CreateIfNotExists();
            return queueClient;
        });

        services.AddScoped<ILotScanRepository, LotScanRepository>();
        services.AddScoped<IdentificationService>();
        services.AddSingleton<PhotoStorageService>();
    })
    .Build();

host.Run();
