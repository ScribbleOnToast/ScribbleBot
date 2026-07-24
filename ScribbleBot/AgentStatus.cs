using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using System.Collections.ObjectModel;

public partial class AgentState : ObservableObject
{
    [ObservableProperty]
    private string _currentInput = string.Empty;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    // Conversation history passed across calls
    public ObservableCollection<ChatMessage> Messages { get; } = new();
}