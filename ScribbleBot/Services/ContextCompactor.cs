using Microsoft.Extensions.AI;

namespace ScribbleBot.Services;

public class ContextCompactor
{
    private readonly IChatClient _chatClient;
    private const int SlidingWindowSize = 8; // Adjust window size as needed

    public ContextCompactor(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<List<ChatMessage>> PreparePayloadAsync(
        IEnumerable<ChatMessage> rawMessages,
        string systemSummary,
        string systemInstruction)
    {
        var payload = new List<ChatMessage>
        {
            new(ChatRole.System, systemInstruction)
        };

        if (!string.IsNullOrWhiteSpace(systemSummary))
        {
            payload.Add(new ChatMessage(ChatRole.System, $"Summary of prior context:\n{systemSummary}"));
        }

        // Take only the most recent window of turns
        var recent = rawMessages.TakeLast(SlidingWindowSize);
        payload.AddRange(recent);

        return payload;
    }

    public async Task<string> GenerateSummaryAsync(IEnumerable<ChatMessage> overflowMessages)
    {
        var summaryPrompt = "Summarize the key points, user choices, and technical details from these earlier conversation turns in 3-4 concise bullet points:\n\n" +
                            string.Join("\n", overflowMessages.Select(m => $"{m.Role.Value.ToUpper()}: {m.Text}"));

        try
        {
            var response = await _chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, summaryPrompt)]);
            return response.Text ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}