using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace fn_lot_scanner.Tests;

// Launches the REAL compiled app via `func start` (Azure Functions Core Tools) and sends
// real HTTP requests to it. This is the only way to test this app's actual HTTP pipeline
// (routing + middleware together): the isolated-worker model requires a real gRPC-connected
// Functions Host to run at all, with or without the ASP.NET Core integration package -- see
// Program.cs's comment. A unit test on a function body directly bypasses routing and
// middleware entirely, so it can't catch what this fixture catches (the actual CORS/routing
// regression this app shipped with).
//
// Needs `func` (azure-functions-core-tools@4) on PATH. Install: npm install -g
// azure-functions-core-tools@4 --unsafe-perm true. CI must do the same.
public class FuncHostFixture : IAsyncLifetime
{
    private Process? _process;
    private readonly int _port;

    public HttpClient Client { get; }

    public FuncHostFixture()
    {
        _port = GetFreeTcpPort();
        Client = new HttpClient { BaseAddress = new Uri($"http://localhost:{_port}/") };
    }

    public async Task InitializeAsync()
    {
        var workingDirectory = FindBuildOutputDirectory();

        // Process.Start (UseShellExecute=false) needs the real executable, not the bare
        // command name -- on Windows, `func` resolves via a func.cmd shell wrapper that
        // only cmd.exe's own PATH search knows how to invoke.
        var funcExecutable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "func.cmd" : "func";
        var psi = new ProcessStartInfo
        {
            FileName = funcExecutable,
            Arguments = $"start --port {_port} --no-build",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Deliberately minimal: these tests only exercise paths CorsMiddleware/
        // JwtAuthenticationMiddleware reject before any real dependency (DB, blob
        // storage, Anthropic) is touched, so none of those need to be configured here.
        psi.Environment["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated";
        psi.Environment["AzureWebJobsStorage"] = "UseDevelopmentStorage=true";
        psi.Environment["Cors__AllowedOrigins"] = "http://localhost:8081";

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start func host process.");

        await WaitUntilReadyAsync(TimeSpan.FromSeconds(60));
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        _process?.Dispose();
        return Task.CompletedTask;
    }

    private async Task WaitUntilReadyAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                var stdout = await _process.StandardOutput.ReadToEndAsync();
                var stderr = await _process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException(
                    $"func host exited early (code {_process.ExitCode}):\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
            }

            try
            {
                // Any response at all (even 401/404) means the host is actually listening
                // and routing requests, which is all readiness needs to confirm here.
                using var response = await Client.GetAsync("api/lot-scans");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                await Task.Delay(500);
            }
        }

        throw new TimeoutException($"func host never became ready within {timeout}.", lastError);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // The test assembly builds to tests/fn_lot_scanner.Tests/bin/<Config>/<TFM>/; the main
    // project (referenced, so already built) sits at bin/<Config>/<TFM>/ from the repo root.
    private static string FindBuildOutputDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        var configAndTfm = Path.Combine(dir.Parent!.Name, dir.Name); // e.g. Debug/net10.0
        var repoRoot = dir.Parent!.Parent!.Parent!.Parent!.Parent!.FullName; // up past tests/fn_lot_scanner.Tests/bin/<cfg>/<tfm>
        var outputDir = Path.Combine(repoRoot, "bin", configAndTfm);
        if (!File.Exists(Path.Combine(outputDir, "host.json")))
        {
            throw new DirectoryNotFoundException(
                $"Expected the main project's build output (with host.json) at '{outputDir}' -- " +
                "build fn-lot-scanner.csproj before running these tests.");
        }
        return outputDir;
    }
}
