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

        // Current Sidebar View State
        [ObservableProperty]
        private SidebarViewType _currentSidebarView = SidebarViewType.Threads;

        // Dark Mode Property (Triggers theme change when toggled)
        [ObservableProperty]
        private bool _isDarkMode = true;

        public MainViewModel(SupervisorAgent supervisorAgent, AgentState state)
        {
            _supervisorAgent = supervisorAgent;
            State = state;

            // Re-evaluate SendMessageCommand when IsBusy changes
            State.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AgentState.IsBusy))
                {
                    SendMessageCommand.NotifyCanExecuteChanged();
                }
            };

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

        // --- NAVIGATION COMMANDS ---
        [RelayCommand]
        private void Settings()
        {
            CurrentSidebarView = SidebarViewType.Settings;
        }

        [RelayCommand]
        private void ShowThreads()
        {
            CurrentSidebarView = SidebarViewType.Threads;
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
        partial void OnIsDarkModeChanged(bool value)
        {
            ScribbleBot.Services.ThemeManager.ApplyTheme(value);
        }
    }
    public enum SidebarViewType
    {
        Threads,
        Settings
    }    
}