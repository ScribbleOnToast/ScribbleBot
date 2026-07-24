using Microsoft.Extensions.AI;
using ScribbleBot.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScribbleBot.Worker_Agents
{
    public class ChatWorker
    {
        private readonly IChatClient _chatClient;
        private readonly ContextCompactor _compactor;

        public ChatWorker(IChatClient chatClient, ContextCompactor compactor)
        {
            _chatClient = chatClient;
            _compactor = compactor;
        }

        public async Task<string> ProcessAsync(IEnumerable<ChatMessage> history, string systemSummary)
        {
            const string systemPrompt = "You are ScribbleBot, a helpful engineering assistant.";

            // Compact history using windowing and system summary
            var compactedPayload = await _compactor.PreparePayloadAsync(history, systemSummary, systemPrompt);

            var options = new ChatOptions
            {
                Temperature = 0.7f
            };

            var response = await _chatClient.GetResponseAsync(compactedPayload, options);
            return response.Text ?? "No response generated.";
        }
    }
}