using ArchitectureShowcase.OpenAI.HttpSurface.TypedHubClients;
using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Storage;
using Azure.Core.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Identity.Web;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;
using System.Text.Json;

namespace ArchitectureShowcase.OpenAI.HttpSurface;



public class ChatSurface : ServerlessHub<IChatClient>
{
	private const string ChatSkillName = "ChatPlugin";
	private const string ChatFunction = "Chat";
	private static readonly ObjectSerializer ObjectSerializer = new JsonObjectSerializer(new(JsonSerializerDefaults.Web));

	private readonly ChatParticipantRepository _chatParticipantRepository;
	private readonly ChatSessionRepository _chatSessionRepository;
	private readonly IKernel _semanticKernel;

	public ChatSurface(IServiceProvider serviceProvider, ChatParticipantRepository chatParticipantRepository, ChatSessionRepository chatSessionRepository, IKernel semanticKernel) : base(serviceProvider)
	{
		_chatParticipantRepository = chatParticipantRepository;
		_chatSessionRepository = chatSessionRepository;
		_semanticKernel = semanticKernel;
	}

	[Function("negotiate")]
	public async Task<IActionResult> Negotiate([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
	{
		var (isAuthenticated, _) = await req.HttpContext.AuthenticateAzureFunctionAsync();
		var userId = req.HttpContext.User.GetObjectId();
		if (!isAuthenticated || string.IsNullOrEmpty(userId))
			return new UnauthorizedResult();

		var negotiateResponse = await NegotiateAsync(new() { UserId = userId });
		return new OkObjectResult(negotiateResponse.ToStream());
	}

	[Function("JoinChat")]
	public async Task<IActionResult> JoinChat(
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chatParticipant/join")] HttpRequest req,
		[FromQuery] string chatId)
	{
		var (isAuthenticated, _) = await req.HttpContext.AuthenticateAzureFunctionAsync();
		var userId = req.HttpContext.User.GetObjectId();

		if (!isAuthenticated || string.IsNullOrEmpty(userId))
			return new UnauthorizedResult();

		if (string.IsNullOrEmpty(chatId))
			return new BadRequestResult();

		if (!await _chatSessionRepository.TryFindByIdAsync(chatId, v => _ = v))
		{
			return new BadRequestObjectResult("Chat session does not exist.");
		}

		// Make sure the user is not already in the chat session.
		if (await _chatParticipantRepository.IsUserInChatAsync(userId, chatId))
		{
			return new BadRequestObjectResult("User is already in the chat session.");
		}
		var chatParticipant = new ChatParticipant(userId, chatId);
		await _chatParticipantRepository.CreateAsync(chatParticipant);

		await Clients.Group(chatId).UserJoined(chatId, userId);

		return new OkObjectResult(chatParticipant);
	}

	[Function("Chat")]
	public async Task<IActionResult> Chat(
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequest req,
		FunctionContext executionContext)
	{
		var (isAuthenticated, authenticationResponse) = await req.HttpContext.AuthenticateAzureFunctionAsync();
		var userId = req.HttpContext.User.GetObjectId();
		if (!isAuthenticated || string.IsNullOrEmpty(userId))
			return authenticationResponse ?? new UnauthorizedResult();

		var ask = await req.ReadFromJsonAsync<Ask>();

		if (ask is null)
			return new BadRequestResult();


		// Put ask's variables in the context we will use.
		var contextVariables = new ContextVariables(ask.Input);
		foreach (var input in ask.Variables)
		{
			contextVariables.Set(input.Key, input.Value);
		}

		// Get the function to invoke
		ISKFunction? function = null;
		try
		{
			function = _semanticKernel.Skills.GetFunction(ChatSkillName, ChatFunction);
		}
		catch (KernelException ke)
		{
			executionContext.GetLogger<HealthSurface>().LogError("Failed to find {0}/{1} on server: {2}", ChatSkillName, ChatFunction, ke);

			return new NotFoundObjectResult($"Failed to find {ChatSkillName}/{ChatFunction} on server");
		}

		// Broadcast bot typing state to all users
		if (ask.Variables.Where(v => v.Key == "chatId").Any())
		{
			var chatId = ask.Variables.Where(v => v.Key == "chatId").First().Value;
			await Clients.Group(chatId).ReceiveBotTypingState(chatId, true);
		}

		// Run the function.
		var result = await _semanticKernel.RunAsync(contextVariables, function);

		if (result.ErrorOccurred)
		{
			if (result.LastException is AIException aiException && aiException.Detail is not null)
			{
				return new BadRequestObjectResult(string.Concat(aiException.Message, " - Detail: " + aiException.Detail));
			}

			return new BadRequestObjectResult(result.LastErrorDescription);
		}
		var askResult = new AskResult { Value = result.Result, Variables = result.Variables.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)) };

		// Broadcast AskResult to all users
		if (ask.Variables.Where(v => v.Key == "chatId").Any())
		{
			var chatId = ask.Variables.Where(v => v.Key == "chatId").First().Value;
			await Clients.Group(chatId).ReceiveResponse(askResult, chatId);
			await Clients.Group(chatId).ReceiveBotTypingState(chatId, false);
		}

		return new OkObjectResult(askResult);
	}

	#region Client Method Calls
	//[Function("OnConnected")]
	//public Task OnConnected([SignalRTrigger(nameof(ChatHub), "connections", "connected")] SignalRInvocationContext invocationContext, FunctionContext functionContext)
	//{
	//	invocationContext.Headers.TryGetValue("Authorization", out var auth);
	//	functionContext.GetLogger<ChatHub>().LogInformation($"{invocationContext.ConnectionId} has connected");

	//	return Clients.All.newConnection(new NewConnection(invocationContext.ConnectionId, auth));
	//}

	[Function("AddClientToGroup")]
	[SignalROutput(HubName = nameof(ChatSurface))]
	public Task AddClientToGroup([SignalRTrigger(nameof(ChatSurface), "messages", "AddClientToGroup", "chatId")] SignalRInvocationContext invocationContext,
		string chatId,
		FunctionContext functionContext)
	{
		return Groups.AddToGroupAsync(invocationContext.ConnectionId, chatId);
	}

	[Function("SendUserTypingState")]
	public Task SendUserTypingState([SignalRTrigger(nameof(ChatSurface), "messages", "SendUserTypingState", "chatId", "userId", "isTyping")] SignalRInvocationContext invocationContext,
		string chatId,
		string userId,
		bool isTyping,
		FunctionContext functionContext)
	{
		return Clients.GroupExcept(chatId, new[] { invocationContext.ConnectionId }).ReceiveUserTypingState(chatId, userId, isTyping);
	}

	[Function("SendMessage")]
	public Task SendMessage([SignalRTrigger(nameof(ChatSurface), "messages", "SendMessage", "chatId", "message")] SignalRInvocationContext invocationContext,
		string chatId,
		ChatMessage message,
		FunctionContext functionContext)
	{
		return Clients.GroupExcept(chatId, new[] { invocationContext.ConnectionId }).ReceiveMessage(message, chatId);
	}
	#endregion
}
