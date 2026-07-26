using Microsoft.Extensions.AI;
using ScribbleBot.Agents.Tools;
using ScribbleBot.Services;
using System.Text.Json;

namespace ScribbleBot.Agents
{
    internal class CodeReviewWorker : IWorkerAgent
    {
        public string Name { get; set; } = "CodeReviewWorker";
        public string Description { get; set; } = "Specialized agent for conducting pull-request style code audits, identifying memory leaks or performance bugs, and evaluating code against .NET best practices.";
        public string Model { get; set; } = "gemma4:26b";

        private readonly IChatClient _chatClient;
        private readonly ContextCompactor _compactor;
        private readonly ToolDispatcher _toolDispatcher;

        public CodeReviewWorker(IChatClient chatClient, ContextCompactor compactor, ToolDispatcher toolDispatcher)
        {
            _chatClient = chatClient;
            _compactor = compactor;
            _toolDispatcher = toolDispatcher;
        }

        public async Task<ChatResponse?> ProcessAsync(IEnumerable<ChatMessage> history, string systemSummary)
        {
            string systemPrompt = SystemPromptFactory.CreateCodeReviewAgentPrompt();
            systemPrompt += SystemPromptFactory.UpdateWithDarkModeInstructions();

            var compactedPayload = await _compactor.PreparePayloadAsync(history, systemSummary, systemPrompt);

            var options = new ChatOptions
            {
                Temperature = 0.2f, // Lower temperature for deterministic, precise code generation and analysis
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create(
                        (string query) => _toolDispatcher.DispatchAsync("search_code_symbols", $"{{\"query\":\"{query}\"}}"),
                        "search_code_symbols",
                        "Searches the SQLite FTS index for classes, methods, and signatures across the indexed codebase.")
                }
            };

            var iterationTimeout = DateTime.Now.AddMinutes(5); // Set a timeout for the entire tool execution process
            while (iterationTimeout > DateTime.Now)
            {
                var response = await _chatClient.GetResponseAsync(compactedPayload, options);
                var responseMessage = response.Messages[0];
                var functionCalls = responseMessage.Contents.OfType<FunctionCallContent>().ToList();

                if (functionCalls.Any())
                {
                    compactedPayload.Add(responseMessage);
                    foreach (var call in functionCalls)
                    {
                        string argsJson = JsonSerializer.Serialize(call.Arguments);
                        string toolResult = await _toolDispatcher.DispatchAsync(call.Name, argsJson);

                        compactedPayload.Add(new ChatMessage(ChatRole.Tool, new[]
                        {
                            new FunctionResultContent(call.CallId, toolResult)
                        }));
                    }
                    continue;
                }

                return response;
            }

            return null;
        }
    }
}
