namespace ArchitectureShowcase.OpenAI.HttpSurface.Models;
public class NewSignalRConnection
{
	public string ConnectionId { get; }

	public string Authentication { get; }

	public NewSignalRConnection(string connectionId, string auth)
	{
		ConnectionId = connectionId;
		Authentication = auth;
	}
}
