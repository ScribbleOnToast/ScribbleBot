using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using ScribbleBot.Agents;
using ScribbleBot.Models;
using ScribbleBot.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.IO;
using System.Windows.Documents;

namespace ScribbleBot.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SupervisorAgent _supervisorAgent;

        public FileIngestionService _fileIngestionService;
        public ObservableCollection<IngestedFileContext> AttachedFiles { get; } = new();
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

        public MainViewModel(SupervisorAgent supervisorAgent, AgentState state, FileIngestionService fileIngestionService)
        {
            _fileIngestionService = fileIngestionService;
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

        [RelayCommand]
        public void RemoveAttachment(IngestedFileContext? file)
        {
            if (file != null && AttachedFiles.Contains(file))
            {
                AttachedFiles.Remove(file);
            }
        }

        private bool CanSendMessage()
        {
            return !string.IsNullOrWhiteSpace(UserInput) && !State.IsBusy;
        }

        [RelayCommand(CanExecute = nameof(CanSendMessage))]
        private async Task SendMessageAsync()
        {
            var contents = new List<AIContent>();
            var textBuilder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(UserInput))
            {
                textBuilder.AppendLine(UserInput.Trim());
            }

            foreach (var file in AttachedFiles)
            {
                if (file.Type == FileType.Image)
                {
                    if (File.Exists(file.FilePath))
                    {
                        byte[] imageBytes = await File.ReadAllBytesAsync(file.FilePath);

                        // Microsoft.Extensions.AI uses DataContent or ImageContent for raw media
                        // ImageContent accepts a ReadOnlyMemory<byte> and a media type string
                        contents.Add(new DataContent(imageBytes, "image/png"));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(file.TextContent))
                {
                    // For Code, Text, PDFs: Append raw text into the primary text context
                    textBuilder.AppendLine($"\n\n--- File: {file.FileName} ---");
                    textBuilder.AppendLine("```");
                    textBuilder.AppendLine(file.TextContent);
                    textBuilder.AppendLine("```");
                }
            }

            if (textBuilder.Length > 0)
            {
                contents.Insert(0, new TextContent(textBuilder.ToString().Trim()));
            }

            // Clear UI input state
            UserInput = string.Empty;
            AttachedFiles.Clear();
            var richMessage = new ChatMessage(ChatRole.User, contents);
            // Pass rich message down to SupervisorAgent
            await _supervisorAgent.HandleUserRichMessageAsync(richMessage);

            // Reset UI State
            UserInput = string.Empty;
            AttachedFiles.Clear();
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
        private async Task SwitchThreadAsync(ChatThreadEntity? thread)
        {
            await _supervisorAgent.SwitchThreadAsync(thread);
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            State.IsSidebarOpen = !State.IsSidebarOpen;
        }

        [RelayCommand]
        private async Task DeleteThread(ChatThreadEntity? thread)
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