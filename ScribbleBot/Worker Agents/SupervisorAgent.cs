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

        public SupervisorAgent(ChatWorker chatWorker, DatabaseService dbService, AgentState state)
        {
            _chatWorker = chatWorker;
            _dbService = dbService;
            _state = state;
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

            var messages = await _dbService.GetMessagesForThreadAsync(thread.Id);

            foreach (var msg in messages)
            {
                _state.ActiveMessages.Add(msg);

                var role = msg.Role.ToLower() switch
                {
                    "assistant" => ChatRole.Assistant,
                    "system" => ChatRole.System,
                    _ => ChatRole.User
                };

                _state.Messages.Add(new ChatMessage(role, msg.Content));
            }
        }

        public async Task HandleUserMessageAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage) || _state.CurrentThread == null) return;

            _state.IsBusy = true;
            _state.StatusMessage = "Supervisor: Routing request to Chatbot...";

            var now = DateTime.Now;

            // 1. Add to Microsoft.Extensions.AI collection
            _state.Messages.Add(new ChatMessage(ChatRole.User, userMessage));

            // 2. Add to UI Observable Collection & Persist
            var userMsgModel = new ChatMessageModel { Role = "user", Content = userMessage, Timestamp = now };
            _state.ActiveMessages.Add(userMsgModel);
            await _dbService.AddMessageAsync(_state.CurrentThread.Id, userMsgModel);

            // Auto-title on first turn
            if (_state.ActiveMessages.Count == 1 && _state.CurrentThread.Title == "New Conversation")
            {
                _state.CurrentThread.Title = userMessage.Length > 25 ? userMessage[..25] + "..." : userMessage;
                await _dbService.SaveThreadAsync(_state.CurrentThread);
            }

            try
            {
                // 3. Delegate execution to ChatWorker
                _state.StatusMessage = "Chatbot: Thinking...";
                string summary = _state.CurrentThread.SystemSummary ?? string.Empty;

                string botResponse = await _chatWorker.ProcessAsync(_state.Messages, summary);

                // 4. Update memory & UI models
                _state.Messages.Add(new ChatMessage(ChatRole.Assistant, botResponse));

                var botMsgModel = new ChatMessageModel { Role = "assistant", Content = botResponse, Timestamp = DateTime.Now };
                _state.ActiveMessages.Add(botMsgModel);

                // 5. Save assistant response to SQLite
                await _dbService.AddMessageAsync(_state.CurrentThread.Id, botMsgModel);

                _state.StatusMessage = "Ready";
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
    }
}