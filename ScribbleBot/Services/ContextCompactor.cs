using Microsoft.Extensions.AI;
using ScribbleBot.Agents;
namespace ScribbleBot.Services;

public class ContextCompactor
{
    private readonly IChatClient _chatClient;

    // ~4 characters per token heuristic
    private const int TargetActiveTokenBudget = 8000;
    private const int CharsPerToken = 4;
    private const int MaxActiveCharLength = TargetActiveTokenBudget * CharsPerToken;

    public ContextCompactor(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    /// <summary>
    /// Splits raw message history into active window payload and overflow messages for summarization.
    /// Snaps boundaries so atomic message blocks are never sliced.
    /// </summary>
    public (List<ChatMessage> ActiveWindow, List<ChatMessage> Overflow) SegmentHistory(
        IEnumerable<ChatMessage> fullHistory)
    {
        var messagesList = fullHistory.ToList();
        var activeWindow = new List<ChatMessage>();
        var overflow = new List<ChatMessage>();

        int accumulatedChars = 0;

        // Iterate backwards from newest to oldest
        for (int i = messagesList.Count - 1; i >= 0; i--)
        {
            var msg = messagesList[i];
            int msgLength = msg.Text?.Length ?? 0;

            if (accumulatedChars + msgLength <= MaxActiveCharLength || activeWindow.Count == 0)
            {
                activeWindow.Insert(0, msg);
                accumulatedChars += msgLength;
            }
            else
            {
                // All remaining older messages fall into overflow
                overflow = messagesList.Take(i + 1).ToList();
                break;
            }
        }

        return (activeWindow, overflow);
    }

    /// <summary>
    /// Formats the final payload to send to Ollama (System Instruction + System Summary + Active Window).
    /// </summary>
    public async Task<List<ChatMessage>> PreparePayloadAsync(
        IEnumerable<ChatMessage> activeWindow,
        string systemSummary,
        string systemInstruction)
    {
        var payload = new List<ChatMessage>
        {
            new(ChatRole.System, systemInstruction)
        };

        if (!string.IsNullOrWhiteSpace(systemSummary))
        {
            payload.Add(new ChatMessage(ChatRole.System, $"[Context Checkpoint Summary]:\n{systemSummary}"));
        }

        payload.AddRange(activeWindow);
        return await Task.FromResult(payload);
    }

    /// <summary>
    /// Incrementally updates an existing summary with newly overflowed messages.
    /// Designed for general chat context (goals, facts, user preferences, key decisions).
    /// </summary>
    public async Task<string> UpdateSummaryAsync(string existingSummary, IEnumerable<ChatMessage> overflowMessages)
    {
        if (!overflowMessages.Any()) return existingSummary;

        var overflowText = string.Join("\n", overflowMessages.Select(m => $"{m.Role.Value.ToUpper()}: {m.Text}"));

        string prompt = string.IsNullOrWhiteSpace(existingSummary) ? SystemPromptFactory.CreateChatSummaryPrompt(overflowText) : SystemPromptFactory.UpdateChatSummaryPrompt(existingSummary, overflowText);

        try
        {
            var response = await _chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)]);
            return response.Text?.Trim() ?? existingSummary;
        }
        catch
        {
            return existingSummary;
        }
    }
}