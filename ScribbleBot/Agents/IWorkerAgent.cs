using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScribbleBot.Agents
{
    public interface IWorkerAgent
    {
        // Worker Name
        string Name { get; set; }

        // Worker Description
        string Description { get; set; }

        // Worker Model
        string Model { get; set; } 

        Task<ChatResponse?> ProcessAsync(IEnumerable<ChatMessage> history, string systemSummary);

    }
}
