using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScribbleBot.Worker_Agents
{
    public class SupervisorAgent
    {
        private readonly ChatWorker _chatWorker;
        private readonly AgentState _state;

        public SupervisorAgent(ChatWorker chatWorker, AgentState state)
        {
            _chatWorker = chatWorker;
            _state = state;
        }

        public async Task HandleUserMessageAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            _state.IsBusy = true;
            _state.StatusMessage = "Supervisor: Routing request to Chatbot...";

            // 1. Append user message to history
            _state.Messages.Add(new ChatMessage(ChatRole.User, userMessage));

            try
            {
                // 2. Delegate to Chatbot Worker
                _state.StatusMessage = "Chatbot: Thinking...";
                string botResponse = await _chatWorker.ProcessAsync(_state.Messages);

                // 3. Append assistant response to history
                _state.Messages.Add(new ChatMessage(ChatRole.Assistant, botResponse));
                _state.StatusMessage = "Ready";
            }
            catch (Exception ex)
            {
                _state.Messages.Add(new ChatMessage(ChatRole.Assistant, $"[Error]: {ex.Message}"));
                _state.StatusMessage = "Error occurred";
            }
            finally
            {
                _state.IsBusy = false;
            }
        }
    }
}
