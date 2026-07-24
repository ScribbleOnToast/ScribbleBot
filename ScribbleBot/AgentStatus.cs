using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using ScribbleBot.Models;
using System.Collections.ObjectModel;

public partial class AgentState : ObservableObject
{
    [ObservableProperty]
    private string _currentInput = string.Empty;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isWarmingUp;

    [ObservableProperty]
    private ObservableCollection<ChatThreadModel> _threads = [];

    [ObservableProperty]
    private ChatThreadModel? _currentThread;

    [ObservableProperty]
    private ObservableCollection<ChatMessageModel> _activeMessages = [];

    // Conversation history passed across calls
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    // Flag to indicate whether the model should be kept alive between calls
    public bool KeepModelAlive { get; set; }
}