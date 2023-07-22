using ArchitectureShowcase.OpenAI.SemanticKernel.Models;

namespace ArchitectureShowcase.OpenAI.HttpSurface.TypedHubClients;
public interface IChatClient
{
	Task ReceiveMessage(ChatMessage message, string chatId);
	Task ReceiveResponse(AskResult askResult, string chatId);

	Task UserJoined(string chatId, string userId);
	Task ReceiveUserTypingState(string chatId, string userId, bool isTyping);
	Task ReceiveBotTypingState(string chatId, bool isTyping);
	Task ChatDocumentUploaded(ChatMessage message, string chatId);
	Task GlobalDocumentUploaded(string fileNames, string username);
}
