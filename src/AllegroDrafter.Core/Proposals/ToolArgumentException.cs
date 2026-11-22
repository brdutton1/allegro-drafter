namespace AllegroDrafter.Core.Proposals;

/// <summary>Bad tool input. The message goes straight back to the AI so it can fix the call.</summary>
public sealed class ToolArgumentException : Exception
{
    public ToolArgumentException(string message) : base(message) { }
}
