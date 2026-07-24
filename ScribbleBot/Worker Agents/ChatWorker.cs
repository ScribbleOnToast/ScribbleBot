using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScribbleBot.Worker_Agents
{
    public class ChatWorker
    {
        private readonly IChatClient _chatClient;

        public ChatWorker(IChatClient chatClient)
        {
            _chatClient = chatClient;
        }

        public async Task<string> ProcessAsync(IEnumerable<ChatMessage> history)
        {
            var options = new ChatOptions
            {
                Temperature = 0.7f
            };

            var response = await _chatClient.GetResponseAsync(history, options);
            return response.Text ?? "No response generated.";
        }
    }
}
