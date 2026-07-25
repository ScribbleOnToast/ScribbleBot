using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OllamaSharp;
using ScribbleBot.Agents;
using ScribbleBot.Agents.Tools;
using ScribbleBot.Services;
using ScribbleBot.Settings;
using ScribbleBot.ViewModels;
using System.Windows;


namespace ScribbleBot;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder(e.Args);

        //Grab settings from appsettings.json
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
        builder.Services.AddSingleton<IntentRouter>();
        builder.Services.AddSingleton<ToolDispatcher>();
        builder.Services.AddTransient<ContextCompactor>();

        // Register Agents implementing IWorkerAgent

        builder.Services.AddSingleton<IWorkerAgent, ChatWorker>();
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

        try
        {
            state.IsWarmingUp = true;
            state.StatusMessage = "Warming up model...";

            // Send an empty prompt with keep_alive set to load the weights into VRAM
            var options = new ChatOptions
            {
                AdditionalProperties = new()
                {
                    ["keep_alive"] = settings.KeepAlive ? "-1m" : "5m" // e.g.
                }
            };

            await chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, " ")], options);

            state.StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            state.StatusMessage = $"Warmup failed: {ex.Message}";
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
        base.OnExit(e);
    }
}