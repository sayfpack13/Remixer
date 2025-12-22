namespace Remixer.Core.AI;

public class ChatMessage
{
    public string Role { get; set; } = ""; // "user", "assistant", "system"
    public string Content { get; set; } = "";
}

