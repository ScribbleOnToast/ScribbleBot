using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScribbleBot.Agents;
using ScribbleBot.Models;

namespace ScribbleBot.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SupervisorAgent _supervisorAgent;

        public AgentState State { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
        private string _userInput = string.Empty;

        public MainViewModel(SupervisorAgent supervisorAgent, AgentState state)
        {
            _supervisorAgent = supervisorAgent;
            State = state;

            // Load saved threads from SQLite on startup
            _ = _supervisorAgent.InitializeAsync();
        }

        private bool CanSendMessage()
        {
            return !string.IsNullOrWhiteSpace(UserInput) && !State.IsBusy;
        }

        [RelayCommand(CanExecute = nameof(CanSendMessage))]
        private async Task SendMessageAsync()
        {
            string textToSend = UserInput.Trim();
            UserInput = string.Empty;

            await _supervisorAgent.HandleUserMessageAsync(textToSend);
        }

        [RelayCommand]
        private async Task CreateNewThreadAsync()
        {
            await _supervisorAgent.CreateNewThreadAsync();
        }

        [RelayCommand]
        private async Task SwitchThreadAsync(ChatThreadModel? thread)
        {
            await _supervisorAgent.SwitchThreadAsync(thread);
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            State.IsSidebarOpen = !State.IsSidebarOpen;
        }

        [RelayCommand]
        private async Task DeleteThread(ChatThreadModel? thread)
        {
            if (thread != null)
            {
                await _supervisorAgent.DeleteThreadAsync(thread);
            }
        }
    }
}