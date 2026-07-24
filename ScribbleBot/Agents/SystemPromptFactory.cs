using System;

namespace ScribbleBot.Agents;

public static class SystemPromptFactory
{
    private static readonly DateTime KnowledgeCutoff = new(2025, 1, 1);

    /// <summary>
    /// Builds the primary conversational system prompt with dynamic temporal anchoring.
    /// </summary>
    public static string CreateGeneralChatPrompt()
    {
        var now = DateTimeOffset.Now;

        return $"""
            You are ScribbleBot, a direct, reliable, and grounded AI assistant equipped with web search capabilities.

            === TEMPORAL CONTEXT & KNOWLEDGE BOUNDARIES ===
            - CURRENT TIMESTAMP: {now:dddd, MMMM d, yyyy 'at' h:mm tt zzz}
            - KNOWLEDGE CUTOFF: {KnowledgeCutoff:MMMM yyyy}

            === TOOL USAGE & SEARCH DIRECTIVES ===
            1. RECENT PAST EVENTS (Between {KnowledgeCutoff:MMMM yyyy} and {now:yyyy-MM-dd}):
               - If asked about an event, result, or news item that occurred after your knowledge cutoff, DO NOT answer "I don't know" directly.
               - INSTEAD, issue a function call to `google_search` with a targeted query to fetch up-to-date information.

            2. UNCERTAIN FACTS:
               - If you are uncertain about factual information that may have changed since {KnowledgeCutoff:MMMM yyyy}, execute a search before attempting an answer.

            3. FUTURE EVENTS (After {now:yyyy-MM-dd}):
               - Events scheduled after today ({now:yyyy-MM-dd}) have not occurred yet. Search only if asked for schedules, venues, or upcoming details.

            === RESPONSE SYNTHESIS ===
            - When search results are returned, synthesize them directly into your response.
            - Cite source titles or domain names where appropriate.
            """;
    }

    /// <summary>
    /// Creates a prompt for generating a concise summary of conversation turns that have overflowed the active context window.
    /// </summary>
    /// <param name="overflowText"></param>
    /// <returns></returns>
    public static string CreateChatSummaryPrompt(string overflowText)
    {
        return $"""
            Summarize the key facts, user preferences, main topics, and decisions from these conversation turns.

            REQUIREMENTS:
            - Output MUST be a concise summary of 5 to 8 bullet points maximum.
            - Focus on core user goals, important context or constraints mentioned, and key outcomes.
            - Omit conversational filler, casual banter, greetings, and brief status checks.

            Conversation Turns:
            {overflowText}
            """;
            
    }

    /// <summary>
    /// Creates a prompt for updating an existing conversation summary with new overflowed conversation turns.
    /// </summary>
    /// <param name="existingSummary"></param>
    /// <param name="overflowText"></param>
    /// <returns></returns>
    public static string UpdateChatSummaryPrompt(string existingSummary, string overflowText)
    {
        return $"""
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

    /* 
    ===================================================================
    FUTURE AGENT PROMPTS (Ready to expand as new agents are added)
    ===================================================================

    public static string CreateCodeReviewAgentPrompt()
    {
        // ... AST, diff analysis, and coding guidelines ...
    }

    public static string CreateSearchOrchestratorPrompt()
    {
        // ... Tool calling directives for web search ...
    }
    */
}