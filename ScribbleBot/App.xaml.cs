using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using ScribbleBot.Agents;
using ScribbleBot.Agents.Tools;
using ScribbleBot.Services;
using ScribbleBot.Settings;
using ScribbleBot.ViewModels;
using Serilog;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace ScribbleBot;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string logFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScribbleBot",
        "logs");

        string logFilePath = Path.Combine(logFolder, "scribblebot-.log");

        // Configure Serilog Logger
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
#if DEBUG
            .WriteTo.Console()
#endif
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day, // Creates scribblebot-20260725.log
                retainedFileCountLimit: 14,            // Keep 2 weeks of logs
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        var builder = Host.CreateApplicationBuilder(e.Args);

        //Grab settings from appsettings.json
        builder.Services.AddSerilog();
        builder.Services.AddOptions<OllamaSettings>()
            .BindConfiguration("OllamaSettings")
            .ValidateOnStart();

        builder.Services.AddOptions<GoogleSearchSettings>()
            .BindConfiguration("GoogleSearchSettings");

        builder.Services.AddOptions<QdrantSettings>()
            .BindConfiguration("QdrantSettings");

        //Ollama IChatClient pointing to Gemma 4
        builder.Services.AddSingleton<IChatClient>(sp =>
        {
            var ollamaOpts = sp.GetRequiredService<IOptions<OllamaSettings>>().Value;
            return new OllamaApiClient(ollamaOpts.Endpoint, ollamaOpts.ModelId);
        });

        // Application State & Infrastructure Services
        builder.Services.AddSingleton<AgentState>();
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddHttpClient<GoogleSearchService>();
        builder.Services.AddSingleton<GoogleSearchService>();
        builder.Services.AddSingleton<CodeIndexerService>();
        builder.Services.AddSingleton<SupervisorAgent>();
        builder.Services.AddSingleton<IIntentRouter, IntentRouter>();
        builder.Services.AddSingleton<ToolDispatcher>();
        builder.Services.AddTransient<ContextCompactor>();
        builder.Services.AddSingleton<FileIngestionService>();

        // Register Agents implementing IWorkerAgent
        builder.Services.AddSingleton<IWorkerAgent, ChatWorker>();
        builder.Services.AddSingleton<IWorkerAgent, CodeAnalysisWorker>();
        builder.Services.AddSingleton<IWorkerAgent, CodeReviewWorker>();

        // ViewModel & MainWindow
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>(sp => new MainWindow
        {
            DataContext = sp.GetRequiredService<MainViewModel>()
        });

        _host = builder.Build();
        await _host.StartAsync();

        // Launch Main Window
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        _ = WarmupModelAsync();
    }

    private async Task WarmupModelAsync()
    {
        var state = Services.GetRequiredService<AgentState>();
        var chatClient = Services.GetRequiredService<IChatClient>();
        var settings = Services.GetRequiredService<IOptions<OllamaSettings>>().Value;
        var httpClient = Services.GetRequiredService<HttpClient>();
        var logger = Services.GetRequiredService<ILogger<App>>();

        try
        {
            state.IsWarmingUp = true;
            state.StatusMessage = "Verifying LLM connection...";
            logger.LogInformation("Initiating LLM health check at {Endpoint}", settings.Endpoint);           

            // Extract base address (e.g. http://localhost:11434) for the health check endpoint
            var baseUri = new Uri(settings.Endpoint).GetLeftPart(UriPartial.Authority);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await httpClient.GetAsync(baseUri, cts.Token);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("LLM host reachable. Triggering model warmup for '{ModelId}'...", settings.ModelId);

            // Send an empty prompt with keep_alive set to load the weights into VRAM
            var options = new ChatOptions
            {
                AdditionalProperties = new()
                {
                    ["keep_alive"] = settings.KeepAlive ? "-1m" : "5m" // e.g.
                }
            };

            await chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, " ")], options);
            logger.LogInformation("Model '{ModelId}' warmed up successfully.", settings.ModelId);
            state.StatusMessage = "Ready";
        }
        catch (HttpRequestException hEX)
        {
            logger.LogError(hEX, "Warmup failed: Unable to reach Ollama {Endpoint}.", settings.Endpoint);
            state.StatusMessage = $"Warmup failed: Unable to reach Ollama server.";
        }
        catch (TaskCanceledException tEX)
        {
            logger.LogError(tEX, "Warmup failed: Ollama connection timed out at endpoint {Endpoint} (5s).", settings.Endpoint);
            state.StatusMessage = "Ollama connection timed out after 5 seconds.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Warmup failed: An unexpected error occurred during model warmup.");
            state.StatusMessage = $"Warmup failed: An unexpected error occurred.";
        }
        finally
        {
            state.IsWarmingUp = false;
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            var chatClient = Services.GetService<IChatClient>();
            var settings = Services.GetRequiredService<IOptions<OllamaSettings>>().Value;

            if (chatClient != null && settings.UnloadOnExit)
            {
                // Passing keep_alive: "0s" instructs Ollama to immediately unload the model from VRAM                
                var options = new ChatOptions
                {
                    AdditionalProperties = new()
                    {
                        ["keep_alive"] = "0s"
                    }
                };
                // Block directly on the task so WPF waits for Ollama to process the unload before terminating
                chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, " ")], options).GetAwaiter().GetResult();
            }
        }
        catch
        {           
        }
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}