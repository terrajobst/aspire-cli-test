#define DISABLE_ASPIRE_LOGGING
//#define DISABLE_ASPIRE_OUTPUT
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal class Program
{
    private static async Task Main(string[] args)
    {
        using var host = Host.CreateDefaultBuilder()
                                  .ConfigureServices(ConfigureServices)
                                  .ConfigureLogging(l => l.ClearProviders())
                                  .Build();
        host.Start();

        var application = host.Services.GetRequiredService<Application>();
        await application.Run(args);

        await host.StopAsync();
        await host.WaitForShutdownAsync();
    }

    private static void ConfigureServices(IServiceCollection collection)
    {
        collection.AddSingleton<Application>();
        collection.AddSingleton<AspireService>();
    }
}

internal sealed class Application(AspireService aspireService)
{
    public async Task Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Wrong number of arguments");
            return;
        }

        var action = args[0];

        switch (action)
        {
            case "start":
                Console.WriteLine("Starting apphost...");
                await aspireService.Start();
                break;
            case "stop":
                Console.WriteLine("Stopping apphost...");
                await aspireService.Stop();
                break;
            case "up":
                Console.WriteLine("Starting webapp...");
                await aspireService.Up("webapp");
                break;
            case "down":
                Console.WriteLine("Shutting down webapp...");
                await aspireService.Down("webapp");
                break;
            default:
                Console.WriteLine($"Unexpected command '{action}'");
                break;
        }
    }
}

internal class AspireService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task Start()
    {
        await EnsureAppHostIsStarted(quiet: false);
    }

    public async Task Stop()
    {
        await ExecuteAspireCommand("stop");
    }

    public async Task<AspirePsHostInfo[]> Ps(bool quiet = false)
    {
        return await ExecuteAspireCommand<AspirePsHostInfo[]>("ps", quiet);
    }

    public async Task Up(string resourceName)
    {
        await EnsureAppHostIsStarted();

        using (CreateLogger(resourceName))
        {
            await StopResource(resourceName);
            await StartResource(resourceName);
        }
    }

    public async Task Down(string resourceName)
    {
        await EnsureAppHostIsStarted();

        using (CreateLogger(resourceName))
        {
            await StopResource(resourceName);
        }
    }

    private static string GetAspireAppHost()
    {
        var repo = GetRepositoryDirectory();
        return Path.Join(repo, "aspire-cli-test.AppHost", "aspire-cli-test.AppHost.csproj");
    }

    private async Task EnsureAppHostIsStarted(bool quiet = false)
    {
        var appHost = GetAspireAppHost();
        var runningAppHosts = await Ps(quiet);
        if (runningAppHosts is not null &&
            runningAppHosts.Any(h => string.Equals(h.AppHostPath, appHost, StringComparison.OrdinalIgnoreCase)))
        {
            if (!quiet)
            {
                Console.WriteLine("Aspire app host is already running.");
            }
            return;
        }

        await ExecuteAspireCommand("start --no-build");
    }

    private async Task StartResource(string resourceName)
    {
        await ExecuteResourceCommand(resourceName, "start");
    }

    private async Task StopResource(string resourceName)
    {
        await ExecuteResourceCommand(resourceName, "stop");
    }

    private async Task ExecuteResourceCommand(string resourceName, string action)
    {
        var command = $"""resource {resourceName} {action}""";
        await ExecuteAspireCommand(command);
    }

    private async Task<int> ExecuteAspireCommand(string command, bool quiet = false)
    {
        var appHost = GetAspireAppHost();
        var arguments = $"""
            {command} --apphost "{appHost}"
            """;

        var workingDirectory = Environment.CurrentDirectory;

        var startInfo = new ProcessStartInfo()
        {
            FileName = "aspire",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
#if ASPIRE_DISABLE_OUTPUT
            RedirectStandardError = false,
            RedirectStandardOutput = false
#else
            RedirectStandardError = !quiet,
            RedirectStandardOutput = !quiet
#endif
        };

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            throw new Exception("Error launching process");
        }

        if (!quiet)
        {
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Console.WriteLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Console.WriteLine(e.Data);
                }
            };
        }

#if !ASPIRE_DISABLE_OUTPUT
        if (!quiet)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
#endif

        await process.WaitForExitAsync();

        return process.ExitCode;
    }

    private async Task<string> ExecuteAspireCommandAndCaptureOutput(string command, bool skipAppHost = false, bool quiet = false)
    {
        var arguments = skipAppHost
            ? command
            : $"""
              {command} --apphost "{GetAspireAppHost()}"
              """;

        var workingDirectory = Environment.CurrentDirectory;

        var startInfo = new ProcessStartInfo()
        {
            FileName = "aspire",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
#if ASPIRE_DISABLE_OUTPUT
            RedirectStandardError = false,
            RedirectStandardOutput = false
#else
            RedirectStandardError = !quiet,
            RedirectStandardOutput = true
#endif
        };

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            throw new Exception("Error launching process");
        }

        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };

        if (!quiet)
        {
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Console.WriteLine(e.Data);
                }
            };
        }

#if !ASPIRE_DISABLE_OUTPUT
        process.BeginOutputReadLine();

        if (!quiet)
        {
            process.BeginErrorReadLine();
        }
#endif

        await process.WaitForExitAsync();

        return output.ToString();
    }

    private async Task<T> ExecuteAspireCommand<T>(string command, bool quiet = true)
    {
        command += " --format json";

        var output = await ExecuteAspireCommandAndCaptureOutput(command, skipAppHost: true, quiet);

#if ASPIRE_DISABLE_OUTPUT
        output = """
            [
                {
                    "appHostPath": "C:\\git\\aspire-cli-test\\aspire-cli-test.AppHost\\aspire-cli-test.AppHost.csproj",
                    "appHostPid": 40068,
                    "cliPid": 61596,
                    "dashboardUrl": "https://localhost:17236/login?t=7b149a40d69aca29500544da63d472da"
                }
            ]
            """;
#endif

        return (T)JsonSerializer.Deserialize(output, typeof(T), AspireJsonSerializationContext.Default)!;
    }

    private DisposableLogging CreateLogger(string resourceName)
    {
#if DISABLE_ASPIRE_LOGGING
        return null!;
#else
        var appHost = GetAspireAppHost();
        var workingDirectory = Environment.CurrentDirectory;
        return new DisposableLogging(resourceName, appHost, workingDirectory);
#endif
    }

    private static string GetRepositoryDirectory()
    {
        return GetRepositoryDirectory(Environment.CurrentDirectory);
    }

    private static string GetRepositoryDirectory(string directory)
    {
        while (true)
        {
            var gitFolder = Path.Join(directory, ".git");
            if (Directory.Exists(gitFolder))
            {
                return directory;
            }

            var next = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(next))
            {
                return directory;
            }

            directory = next;
        }
    }

    private sealed class DisposableLogging : IDisposable
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Process _process;

        public DisposableLogging(string resourceName, string appHost, string workingDirectory)
        {
            _process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "aspire",
                    Arguments = $"""logs {resourceName} --apphost "{appHost}" -f""",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            _cts.Token.Register(
                static o =>
                {
                    var process = (Process)o!;
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                },
                _process);

            var outputDelay = TimeSpan.FromMilliseconds(200);
            var stopwatch = new Stopwatch();

            _process.OutputDataReceived += (_, e) =>
            {
                if (ShouldOutput() && e.Data is not null)
                {
                    Console.WriteLine(e.Data);
                }
            };

            _process.ErrorDataReceived += (_, e) =>
            {
                if (ShouldOutput() && e.Data is not null)
                {
                    Console.WriteLine(e.Data);
                }
            };

            Console.WriteLine($"Running {_process.StartInfo.FileName} {_process.StartInfo.Arguments}...");
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            bool ShouldOutput()
            {
                if (!stopwatch.IsRunning)
                {
                    stopwatch.Start();
                    return false;
                }

                if (stopwatch.Elapsed < outputDelay)
                {
                    stopwatch.Restart();
                    return false;
                }

                return true;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _process.WaitForExit();
            _process.Dispose();
        }
    }
}

public sealed class AspirePsHostInfo
{
    public string? AppHostPath { get; set; }

    public int? AppHostPid { get; set; }

    public int? CliPid { get; set; }

    public string? DashboardUrl { get; set; }
}

[JsonSerializable(typeof(AspirePsHostInfo[]))]
public partial class AspireJsonSerializationContext : JsonSerializerContext
{
}