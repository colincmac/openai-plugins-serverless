using ArchitectureShowcase.OpenAI.SemanticKernel.Models;

namespace ArchitectureShowcase.OpenAI.HttpSurface.TypedHubClients;
public interface IChatHistoryClient
{
	Task ChatEdited(ChatSession chat);
}
