using ArchitectureShowcase.OpenAI.SemanticKernel.Models;

namespace ArchitectureShowcase.OpenAI.HttpSurface.TypedHubClients;
public interface IDocumentManagementClient
{
	Task GlobalDocumentUploaded(string documentMessageContent, string username);
	Task ChatDocumentUploaded(ChatMessage chatMessage, string chatId);
}
