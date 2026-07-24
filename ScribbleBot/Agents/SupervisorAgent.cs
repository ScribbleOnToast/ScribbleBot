using Microsoft.Extensions.AI;
using ScribbleBot.Models;
using ScribbleBot.Services;

namespace ScribbleBot.Agents
{
    /// <summary>
    /// Coordinates high-level chat workflow: loads threads, routes user messages
    /// to the ChatWorker, updates persistent storage, and manages in-memory
    /// conversation state and summaries.
    /// </summary>
    public class SupervisorAgent
    {
        private readonly ChatWorker _chatWorker;
        private readonly DatabaseService _dbService;
        private readonly AgentState _state;
        private readonly ContextCompactor _compactor;

        /// <summary>
        /// Creates a new SupervisorAgent.
        /// </summary>
        /// <param name="chatWorker">Worker that handles interactions with the chatbot/LLM.</param>
        /// <param name="dbService">Service for persisting and retrieving threads and messages.</param>
        /// <param name="state">Shared application state for threads, messages and UI flags.</param>
        /// <param name="compactor">Component responsible for segmenting and summarizing history.</param>
        public SupervisorAgent(ChatWorker chatWorker, DatabaseService dbService, AgentState state, ContextCompactor compactor)
        {
            _chatWorker = chatWorker;
            _dbService = dbService;
            _state = state;
            _compactor = compactor;
        }

        /// <summary>
        /// Loads saved threads from the database into state and selects an initial
        /// thread (either the first existing thread or a newly created one).
        /// </summary>
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

        /// <summary>
        /// Creates and persists a new chat thread, inserts it into the state list,
        /// and switches the UI context to the new thread.
        /// </summary>
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

        /// <summary>
        /// Switches the active thread context to the provided thread. Clears
        /// in-memory message lists and loads the full message history for UI
        /// rendering. Also constructs the ChatMessage list used by the LLM.
        /// </summary>
        /// <param name="thread">Thread to switch to. If null, the call is ignored.</param>
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

        /// <summary>
        /// Handles a user-submitted message: updates UI state, persists the
        /// message, sends the composed context to the ChatWorker, persists the
        /// assistant response, and triggers background checkpointing.
        /// </summary>
        /// <param name="userMessage">Raw user message text to process.</param>
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

        /// <summary>
        /// Performs background checkpointing: if the conversation exceeds the
        /// active window budget, compacts overflow into the thread's
        /// SystemSummary, persists the summary, and trims the in-memory
        /// Messages list to the active window.
        /// </summary>
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

        public async Task DeleteThreadAsync(ChatThreadModel threadToDelete)
        {
            if (threadToDelete == null) return;

            int deletedIndex = _state.Threads.IndexOf(threadToDelete);
            if (deletedIndex == -1) return;

            bool isDeletingCurrent = _state.CurrentThread?.Id == threadToDelete.Id;

            // 1. Remove from Database and ObservableCollection
            await _dbService.DeleteThreadAsync(threadToDelete.Id);
            _state.Threads.Remove(threadToDelete);

            // 2. Handle thread selection logic
            if (isDeletingCurrent)
            {
                if (_state.Threads.Count == 0)
                {
                    // List is completely empty, spin up a fresh one
                    await CreateNewThreadAsync();
                }
                else
                {
                    // Select thread directly "above" it (index - 1). 
                    // If the deleted item was at index 0, take the new index 0.
                    int nextIndex = Math.Max(0, deletedIndex - 1);
                    await SwitchThreadAsync(_state.Threads[nextIndex]);
                }
            }
        }
    }
}