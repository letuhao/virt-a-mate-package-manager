namespace VirtaMatePackageManager.Presentation.ViewModels.Messaging;

/// <summary>
/// Represents a message to display to the user.
/// </summary>
public class MessageViewModel
{
    public MessageType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// Type of message.
/// </summary>
public enum MessageType
{
    Info,
    Success,
    Warning,
    Error
}

