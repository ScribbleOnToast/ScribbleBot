using Microsoft.Extensions.AI;
using ScribbleBot.Agents.Tools;
using ScribbleBot.Services;
using System.Text.Json;

namespace ScribbleBot.Agents
{
    public class CodeAnalysisWorker : IWorkerAgent
    {
        private readonly IChatClient _chatClient;
        private readonly ContextCompactor _compactor;
        private readonly ToolDispatcher _toolDispatcher;
        private const int MaxToolIterations = 5;

        public string Name { get; set; } = "CodeAnalysisWorker";
        public string Description { get; set; } = "Specialized agent for explaining codebase architecture, tracing execution flow, mapping system dependencies, and describing how components interact.";
        public string Model { get; set; } = "gemma4:26b";

        public CodeAnalysisWorker(IChatClient chatClient, ContextCompactor compactor, ToolDispatcher toolDispatcher)
        {
            _chatClient = chatClient;
            _compactor = compactor;
            _toolDispatcher = toolDispatcher;
        }

        public async Task<string> ProcessAsync(IEnumerable<ChatMessage> history, string systemSummary)
        {
            string systemPrompt = SystemPromptFactory.CreateCodeAnalysisAgentPrompt();
            systemPrompt += SystemPromptFactory.UpdateWithDarkModeInstructions();

            var compactedPayload = await _compactor.PreparePayloadAsync(history, systemSummary, systemPrompt);

            var options = new ChatOptions
            {
                Temperature = 0.2f, // Lower temperature for deterministic, precise code generation and analysis
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create(
                        (string folderPath) => _toolDispatcher.DispatchAsync("index_codebase", JsonSerializer.Serialize(new { folderPath })),
                        "index_codebase",
                        "Scans and indexes all .cs and .xaml files in the target directory into the SQLite structural map. Call this when directed to consume or index a project folder."),
                    AIFunctionFactory.Create(
                        (string query) => _toolDispatcher.DispatchAsync("search_code_symbols", $"{{\"query\":\"{query}\"}}"),
                        "search_code_symbols",
                        "Searches the SQLite FTS index for classes, methods, and signatures across the indexed codebase."),
                    AIFunctionFactory.Create(
                        () => _toolDispatcher.DispatchAsync("get_pending_reviews", "{}"),
                        "get_pending_reviews",
                        "Retrieves pending code review items and architectural recommendations from the database.")
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

                return response.Text ?? "No code analysis response generated.";
            }

            return "Maximum tool execution iterations reached without a final response.";
        }
    }
}