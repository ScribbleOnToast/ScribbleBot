using Microsoft.Extensions.AI;
using ScribbleBot.Models;
using ScribbleBot.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ScribbleBot.Worker_Agents
{
    public class SupervisorAgent
    {
        private readonly ChatWorker _chatWorker;
        private readonly DatabaseService _dbService;
        private readonly AgentState _state;
        private readonly ContextCompactor _compactor;

        public SupervisorAgent(ChatWorker chatWorker, DatabaseService dbService, AgentState state, ContextCompactor compactor)
        {
            _chatWorker = chatWorker;
            _dbService = dbService;
            _state = state;
            _compactor = compactor;
        }

        public async Task InitializeAsync()
        {
            var savedThreads = await _dbService.GetAllThreadsAsync();
            _state.Threads.Clear();

            foreach (var thread in savedThreads)
            {
                _state.Threads.Add(thread);
            }

            if (_state.Threads.Any())
            {
                await SwitchThreadAsync(_state.Threads.First());
            }
            else
            {
                await CreateNewThreadAsync();
            }
        }

        public async Task CreateNewThreadAsync()
        {
            var newThread = new ChatThreadModel
            {
                Id = Guid.NewGuid().ToString(),
                Title = "New Conversation",
                CreatedAt = DateTime.Now,
                LastUpdatedAt = DateTime.Now
            };

            await _dbService.SaveThreadAsync(newThread);
            _state.Threads.Insert(0, newThread);
            await SwitchThreadAsync(newThread);
        }

        public async Task SwitchThreadAsync(ChatThreadModel? thread)
        {
            if (thread == null) return;

            _state.CurrentThread = thread;
            _state.Messages.Clear();
            _state.ActiveMessages.Clear();

            // 1. Fetch full history from DB for UI rendering
            var messages = await _dbService.GetMessagesForThreadAsync(thread.Id);

            var allAiMessages = new List<ChatMessage>();

            foreach (var msg in messages)
            {
                _state.ActiveMessages.Add(msg);

                var role = msg.Role.ToLower() switch
                {
                    "assistant" => ChatRole.Assistant,
                    "system" => ChatRole.System,
                    _ => ChatRole.User
                };

                allAiMessages.Add(new ChatMessage(role, msg.Content));
            }

            // 2. Segment history: Only push token-budgeted active window to state
            var (activeWindow, _) = _compactor.SegmentHistory(allAiMessages);

            foreach (var activeMsg in activeWindow)
            {
                _state.Messages.Add(activeMsg);
            }
        }

        public async Task HandleUserMessageAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage) || _state.CurrentThread == null) return;

            _state.IsBusy = true;
            _state.StatusMessage = "Supervisor: Routing request to Chatbot...";

            var now = DateTime.Now;

            // 1. Add to active context & UI
            _state.Messages.Add(new ChatMessage(ChatRole.User, userMessage));
            var userMsgModel = new ChatMessageModel { Role = "user", Content = userMessage, Timestamp = now };
            _state.ActiveMessages.Add(userMsgModel);
            await _dbService.AddMessageAsync(_state.CurrentThread.Id, userMsgModel);

            // Auto-title thread on first message
            if (_state.ActiveMessages.Count == 1 && _state.CurrentThread.Title == "New Conversation")
            {
                _state.CurrentThread.Title = userMessage.Length > 25 ? userMessage[..25] + "..." : userMessage;
                await _dbService.SaveThreadAsync(_state.CurrentThread);
            }

            try
            {
                _state.StatusMessage = "Chatbot: Thinking...";
                string summary = _state.CurrentThread.SystemSummary ?? string.Empty;

                // 2. Process query with active window + summary checkpoint
                string botResponse = await _chatWorker.ProcessAsync(_state.Messages, summary);

                // 3. Update memory & UI
                _state.Messages.Add(new ChatMessage(ChatRole.Assistant, botResponse));
                var botMsgModel = new ChatMessageModel { Role = "assistant", Content = botResponse, Timestamp = DateTime.Now };
                _state.ActiveMessages.Add(botMsgModel);
                await _dbService.AddMessageAsync(_state.CurrentThread.Id, botMsgModel);

                _state.StatusMessage = "Ready";

                // 4. Fire Checkpointing Evaluation (Background Task)
                _ = CheckpointMemoryAsync();
            }
            catch (Exception ex)
            {
                var errorMsg = $"[Error]: {ex.Message}";
                _state.Messages.Add(new ChatMessage(ChatRole.Assistant, errorMsg));
                _state.ActiveMessages.Add(new ChatMessageModel { Role = "assistant", Content = errorMsg, Timestamp = DateTime.Now });
                _state.StatusMessage = "Error occurred";
            }
            finally
            {
                _state.IsBusy = false;
            }
        }

        private async Task CheckpointMemoryAsync()
        {
            if (_state.CurrentThread == null) return;

            var (activeWindow, overflow) = _compactor.SegmentHistory(_state.Messages);

            // If history exceeds active window budget, roll overflow turns into SystemSummary
            if (overflow.Any())
            {
                var updatedSummary = await _compactor.UpdateSummaryAsync(_state.CurrentThread.SystemSummary ?? string.Empty, overflow);

                _state.CurrentThread.SystemSummary = updatedSummary;
                await _dbService.UpdateThreadSummaryAsync(_state.CurrentThread.Id, updatedSummary);

                // Trim in-memory Messages list down to active window only
                _state.Messages.Clear();
                foreach (var msg in activeWindow)
                {
                    _state.Messages.Add(msg);
                }
            }
        }
    }
}