using System.Collections.Generic;
using System.Threading.Tasks;

// D3 (task.md §2): the pipeline talks to this interface only. DeepSeekClient is
// the direct REST implementation; a Python middleman (RAG/OSHA retrieval,
// proposal Table 2) can be swapped in later without touching the pipeline.

[System.Serializable]
public class ChatMessage
{
    public string role;
    public string content;

    public ChatMessage() { }

    public ChatMessage(string role, string content)
    {
        this.role = role;
        this.content = content;
    }
}

public interface ILLMClient
{
    // Chat-completion call: the full conversation so far (the repair loop
    // reuses the same multi-turn session — task.md §10, open decision 4) plus
    // the sampling temperature. Returns the raw assistant text (expected JSON).
    Task<string> CompleteAsync(List<ChatMessage> messages, float temperature);
}
