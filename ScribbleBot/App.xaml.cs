using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OllamaSharp;
using ScribbleBot.Settings;
using ScribbleBot.Worker_Agents;
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
        // Singletons
        builder.Services.AddSingleton<AgentState>();
        builder.Services.AddSingleton<ChatWorker>();
        builder.Services.AddSingleton<SupervisorAgent>();

        // ViewModel & MainWindow
        builder.Services.AddSingleton<ViewModels.MainViewModel>();
        builder.Services.AddSingleton<MainWindow>(sp => new MainWindow
        {
            DataContext = sp.GetRequiredService<ViewModels.MainViewModel>()
        });

        _host = builder.Build();
        await _host.StartAsync();

        // Launch Main Window
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}