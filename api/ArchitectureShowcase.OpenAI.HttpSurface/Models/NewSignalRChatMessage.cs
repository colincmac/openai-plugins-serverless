namespace ArchitectureShowcase.OpenAI.HttpSurface.Models;
public class NewSignalRChatMessage
{
	public string ConnectionId { get; }
	public string Sender { get; }
	public string Text { get; }

	public NewSignalRChatMessage(SignalRInvocationContext invocationContext, string message)
	{
		Sender = string.IsNullOrEmpty(invocationContext.UserId) ? string.Empty : invocationContext.UserId;
		ConnectionId = invocationContext.ConnectionId;
		Text = message;
	}
}
