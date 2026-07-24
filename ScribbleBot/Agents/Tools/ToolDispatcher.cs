using System.Text.Json;

namespace ScribbleBot.Agents.Tools
{    public class ToolDispatcher
    {
        private readonly GoogleSearchService _searchService;

        public ToolDispatcher(GoogleSearchService searchService)
        {
            _searchService = searchService;
        }

        public async Task<string> DispatchAsync(string functionName, string argumentsJson)
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            switch (functionName)
            {
                case "google_search":
                    string query = root.GetProperty("query").GetString() ?? string.Empty;
                    return await _searchService.ExecuteSearchPipelineAsync(query);

                default:
                    return $"Error: Tool '{functionName}' is not implemented.";
            }
        }
    }
}
