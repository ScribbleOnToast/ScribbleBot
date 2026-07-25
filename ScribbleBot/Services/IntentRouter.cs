using Microsoft.Extensions.AI;
using ScribbleBot.Agents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScribbleBot.Services
{
    public class IntentRouter
    {
        private readonly IChatClient _chatClient;

        public IntentRouter(IChatClient chatClient)
        {
            _chatClient = chatClient;
        }

        public async Task<string> DetermineBestAgentAsync(string userMessage, IEnumerable<IWorkerAgent> availableAgents)
        {
            var agentCapabilities = availableAgents.Select(a => new
            {
                Name = a.Name,
                Description = a.Description
            });

            //Should we move this into the SystemPromptFactory? It is a bit of a different use case than the other system prompts,
            //but it could be useful to have a dedicated system prompt for intent routing.
            string prompt = SystemPromptFactory.CreateIntentRouterPrompt(userMessage, JsonSerializer.Serialize(agentCapabilities));

            try
            {
                var response = await _chatClient.GetResponseAsync(prompt, new ChatOptions
                {
                    Temperature = 0.0f // Zero temperature for deterministic classification
                });

                string selectedName = response.Text?.Trim().Trim('"', '\'') ?? "ChatWorker";

                // Ensure the LLM returned a valid, registered agent name
                return availableAgents.Any(a => a.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
                    ? selectedName
                    : "ChatWorker";
            }
            catch
            {
                // Fallback to default worker on network/model error
                return "ChatWorker";
            }
        }
    }
}