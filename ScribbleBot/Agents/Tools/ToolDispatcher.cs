using ScribbleBot.Services;
using System.IO;
using System.Text.Json;

namespace ScribbleBot.Agents.Tools
{    public class ToolDispatcher
    {
        private readonly GoogleSearchService _searchService;
        private readonly DatabaseService _dbService;
        private readonly CodeIndexerService _indexerService;

        public ToolDispatcher(GoogleSearchService searchService, DatabaseService dbService, CodeIndexerService indexerService)
        {
            _searchService = searchService;
            _dbService = dbService;
            _indexerService = indexerService;
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

                case "search_code_symbols":
                    string symbolQuery = root.GetProperty("query").GetString() ?? string.Empty;
                    var symbols = await _dbService.SearchSymbolsFtsAsync(symbolQuery);
                    return JsonSerializer.Serialize(symbols);

                case "get_pending_reviews":
                    var pendingReviews = await _dbService.GetPendingReviewItemsAsync();
                    return JsonSerializer.Serialize(pendingReviews);

                case "index_codebase":
                    try
                    {
                        string folderPath = root.GetProperty("folderPath").GetString() ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(folderPath))
                        {
                            return "Error: 'folderPath' argument was empty or missing.";
                        }

                        int count = await _indexerService.IndexDirectoryAsync(folderPath);
                        return $"SUCCESS: Indexed {count} code/XAML symbols from '{folderPath}' into the database.";
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return $"FAILED: Directory not found. Details: {ex.Message}";
                    }
                    catch (Exception ex)
                    {
                        return $"FAILED: Error during indexing operation. Details: {ex.Message}";
                    }

                default:
                    return $"Error: Tool '{functionName}' is not implemented.";
            }
        }
    }
}
