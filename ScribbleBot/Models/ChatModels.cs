using System;
using System.Collections.Generic;
using System.Text;

namespace ScribbleBot.Models
{
    public class ChatMessageModel
    {
        public string Role { get; set; } = "user"; // "user", "assistant", "system"
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
    public class ChatThreadModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "New Conversation";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
        public string SystemSummary { get; set; } = string.Empty;

        public List<ChatMessageModel> Messages { get; set; } = [];
    }
}
