using Microsoft.Extensions.AI;
using ScribbleBot.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

        string prompt;
        if (string.IsNullOrWhiteSpace(existingSummary))
        {
            prompt = $"""
            Summarize the key facts, user preferences, main topics, and decisions from these conversation turns.

            REQUIREMENTS:
            - Output MUST be a concise summary of 5 to 8 bullet points maximum.
            - Focus on core user goals, important context or constraints mentioned, and key outcomes.
            - Omit conversational filler, casual banter, greetings, and brief status checks.

            Conversation Turns:
            {overflowText}
            """;
        }
        else
        {
            prompt = $"""
            Synthesize and update the existing conversation summary with the new conversation turns provided below.

            REQUIREMENTS:
            - Combine and condense the old summary and new turns into a SINGLE updated summary.
            - Output MUST be kept to a strict maximum of 5 to 8 bullet points.
            - Retain important long-term facts, preferences, and key decisions.
            - Overwrite obsolete details or changing user preferences with the newest information.
            - Do NOT simply append text; re-compress the combined context so it remains clean and concise.

            Existing Summary:
            {existingSummary}

            New Conversation Turns:
            {overflowText}
            """;
        }

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