using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScribbleBot.Worker_Agents;
using System.Threading.Tasks;

namespace ScribbleBot.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SupervisorAgent _supervisorAgent;

        // Expose our central AgentState so XAML can bind directly to state properties
        public AgentState State { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
        private string _userInput = string.Empty;

        public MainViewModel(SupervisorAgent supervisorAgent, AgentState state)
        {
            _supervisorAgent = supervisorAgent;
            State = state;
        }

        // Condition controlling whether the Send button is enabled
        private bool CanSendMessage()
        {
            return !string.IsNullOrWhiteSpace(UserInput) && !State.IsBusy;
        }

        [RelayCommand(CanExecute = nameof(CanSendMessage))]
        private async Task SendMessageAsync()
        {
            string textToSend = UserInput.Trim();
            UserInput = string.Empty; // Clear text box immediately

            // Hand off user prompt to the Supervisor Agent orchestrator
            await _supervisorAgent.HandleUserMessageAsync(textToSend);
        }
    }
}
