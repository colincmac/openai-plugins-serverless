using ArchitectureShowcase.OpenAI.HttpSurface.Models;
using ArchitectureShowcase.OpenAI.HttpSurface.TypedHubClients;
using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Options;
using ArchitectureShowcase.OpenAI.SemanticKernel.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using System.Net;
using System.Web.Http;

namespace ArchitectureShowcase.OpenAI.HttpSurface;

public class ChatHistorySurface : ServerlessHub<IChatHistoryClient>
{
	private readonly ChatSessionRepository _sessionRepository;
	private readonly ChatMessageRepository _messageRepository;
	private readonly PromptsOptions _promptOptions;
	private readonly ChatMemorySourceRepository _sourceRepository;
	public ChatHistorySurface(IServiceProvider serviceProvider, ChatSessionRepository sessionRepository, ChatMessageRepository messageRepository, ChatMemorySourceRepository sourceRepository, IOptions<PromptsOptions> promptOptions) : base(serviceProvider)
	{
		_sessionRepository = sessionRepository;
		_messageRepository = messageRepository;
		_promptOptions = promptOptions.Value;
		_sourceRepository = sourceRepository;
	}

	/// <summary>
	/// Create a new chat session and populate the session with the initial bot message.
	/// </summary>
	/// <param name="req">The HTTP request.</param>
	/// <returns>The HTTP response.</returns>
	[Function("CreateChatSession")]
	[OpenApiOperation(operationId: "createChatSession", tags: new[] { "chatSession" }, Summary = "Create a new chat session and populate the session with the initial bot message.", Description = "This creates a new chat session and populates the session with the initial bot message.", Visibility = OpenApiVisibilityType.Important)]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(ChatSession), Summary = "The created chat session")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ProblemDetails), Summary = "Invalid request")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(ProblemDetails), Summary = "Resource not found")]
	[OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ChatSession), Required = true, Description = "The chat session parameters")]
	public async Task<IActionResult> CreateChatSessionAsync(
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chatSession")] HttpRequest req,
		FunctionContext executionContext)
	{

		var chatParameters = await req.ReadFromJsonAsync<ChatSession>();

		var (isAuthenticated, authenticationResponse) =
		await req.HttpContext.AuthenticateAzureFunctionAsync();
		var userId = req.HttpContext.User.GetObjectId();
		var tenantId = req.HttpContext.User.GetTenantId();

		if (!isAuthenticated || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tenantId))
			return authenticationResponse ?? new UnauthorizedResult();

		if (chatParameters == null || string.IsNullOrEmpty(chatParameters.UserId) || string.IsNullOrEmpty(chatParameters.Title))
		{
			return new BadRequestObjectResult(new ProblemDetails
			{
				Title = "Invalid request",
				Detail = "The request body is missing or invalid"
			});
		}

		var title = chatParameters.Title;

		var newChat = new ChatSession($"{userId}.{tenantId}", title);
		await _sessionRepository.CreateAsync(newChat);

		var initialBotMessage = _promptOptions.InitialBotMessage;
		// The initial bot message doesn't need a prompt.
		await SaveResponseAsync(initialBotMessage, string.Empty, newChat.Id);

		executionContext.GetLogger<ChatHistorySurface>().LogDebug("Created chat session with id {0} for user {1}", newChat.Id, userId);
		return new CreatedAtFunctionResult("api/chatSession", newChat.Id, newChat);
	}

	[Function("GetChatSessionById")]
	[OpenApiOperation(operationId: "GetChatSessionById", tags: new[] { "chat" })]
	[OpenApiParameter(name: "chatId", In = ParameterLocation.Path, Required = true, Type = typeof(Guid), Description = "The chat id.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(ChatSession), Description = "The chat session.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "text/plain", bodyType: typeof(string), Description = "The bad request error message.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "text/plain", bodyType: typeof(string), Description = "The not found error message.")]
	public async Task<IActionResult> GetChatSessionById(
	[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "chatSession/{chatId:guid}")] HttpRequest req,
	Guid chatId,
	FunctionContext executionContext)
	{
		var (isAuthenticated, authenticationResponse) =
		   await req.HttpContext.AuthenticateAzureFunctionAsync();
		var userId = req.HttpContext.User.GetObjectId();

		if (!isAuthenticated || string.IsNullOrEmpty(userId))
			return authenticationResponse ?? new UnauthorizedResult();

		ChatSession? chat;
		try
		{
			// Make sure the chat session exists
			chat = await _sessionRepository.FindByIdAsync(chatId.ToString());
		}
		catch (KeyNotFoundException)
		{
			return new NotFoundObjectResult($"No chat session found for chat id '{chatId}'.");
		}

		return new OkObjectResult(chat);
	}

	/// <summary>
	/// Get all chat sessions associated with a user. Return an empty list if no chats are found.
	/// The regex pattern that is used to match the user id will match the following format:
	///    - 2 period separated groups of one or more hyphen-delimited alphanumeric strings.
	/// The pattern matches two GUIDs in canonical textual representation separated by a period.
	/// </summary>
	/// <param name="req">The HTTP request.</param>
	[Function("GetAllChatSessionsForUser")]
	[OpenApiOperation(operationId: "GetAllChatSessions", tags: new[] { "chat" })]
	//[OpenApiParameter(name: "userId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The user id.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<ChatSession>), Description = "The list of chat sessions.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(string), Description = "The error message.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(string), Description = "The error message.")]
	public async Task<IActionResult> GetAllChatSessionsForCurrentUser(
		[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "chatSession")] HttpRequest req,
		FunctionContext executionContext,
		CancellationToken cancellationToken)
	{
		var userId = string.Empty;
		try
		{
			var (isAuthenticated, authenticationResponse) =
			   await req.HttpContext.AuthenticateAzureFunctionAsync();
			//userId = req.HttpContext.User.GetObjectId();
			userId = req.HttpContext.User.GetObjectId();
			if (!isAuthenticated || string.IsNullOrEmpty(userId))
				return authenticationResponse ?? new UnauthorizedResult();

			var chats = await _sessionRepository.FindByUserIdAsync(userId);

			return new OkObjectResult(chats ?? new List<ChatSession>());
		}
		catch (OperationCanceledException)
		{
			return new InternalServerErrorResult();
		}
		catch (Exception ex)
		{
			executionContext.GetLogger<ChatHistorySurface>().LogError(ex, "Error getting chat sessions for user {0}", userId);
			return new BadRequestObjectResult(ex.Message);
		}

	}

	[Function("GetChatMessages")]
	[OpenApiOperation(operationId: "GetChatMessages", tags: new[] { "chatSession" }, Summary = "Get all chat messages for a chat session.", Description = "The list will be ordered with the first entry being the most recent message.")]
	[OpenApiParameter(name: "chatSessionId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The chat id.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(ChatMessage[]), Summary = "The chat messages.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "text/plain", bodyType: typeof(string), Summary = "The error message.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "text/plain", bodyType: typeof(string), Summary = "The not found message.")]
	public async Task<IActionResult> GetChatMessages(
	[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "chatSession/{chatSessionId:guid}/messages")] HttpRequest req,
	Guid chatSessionId,
	FunctionContext executionContext)
	{
		var (isAuthenticated, authenticationResponse) =
		   await req.HttpContext.AuthenticateAzureFunctionAsync();

		var userId = req.HttpContext.User.GetObjectId();

		if (!isAuthenticated || string.IsNullOrEmpty(userId))
			return authenticationResponse ?? new UnauthorizedResult();

		var chatMessages = await _messageRepository.FindByChatIdAsync(chatSessionId.ToString());
		if (chatMessages == null)
		{
			return new NotFoundObjectResult($"No chat messages found for chat id '{chatSessionId}'.");
		}

		//chatMessages = chatMessages.OrderByDescending(m => m.Timestamp).Skip(pagingFilter.Page);
		//if (pagingFilter.Count >= 0)
		//{
		//	chatMessages = chatMessages.Take(pagingFilter.Count);
		//}

		return new OkObjectResult(chatMessages);
	}

	[Function("EditChatSession")]
	[OpenApiOperation(operationId: "EditChatSession", tags: new[] { "chatSession" }, Summary = "Edit Chat Session metadata.", Description = "Edit the chat sessions name and other values.")]
	[OpenApiParameter(name: "chatSessionId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The chat session id.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(ChatSession), Summary = "The created chat session")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ProblemDetails), Summary = "Invalid request")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(ProblemDetails), Summary = "Resource not found")]
	[OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ChatSession), Required = true, Description = "The chat session parameters")]
	public async Task<IActionResult> EditChatSession(
		[HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "chatSession/{chatSessionId:guid}")] HttpRequest req,
		string chatSessionId,
		FunctionContext executionContext)
	{
		var (isAuthenticated, authenticationResponse) =
		   await req.HttpContext.AuthenticateAzureFunctionAsync();

		var userId = req.HttpContext.User.GetObjectId();

		if (!isAuthenticated || string.IsNullOrEmpty(userId))
			return authenticationResponse ?? new UnauthorizedResult();

		var chatParameters = await req.ReadFromJsonAsync<ChatSession>();
		if (chatParameters == null)
		{
			return new BadRequestObjectResult($"No chat parameters found for chat id '{chatSessionId}'.");
		}

		ChatSession? chat = null;
		if (!await _sessionRepository.TryFindByIdAsync(chatSessionId, v => chat = v))
		{
			return new NotFoundObjectResult($"No chat found for chat id '{chatSessionId}'.");
		}

		chat!.Title = chatParameters.Title;
		await _sessionRepository.UpsertAsync(chat);
		await Clients.Group(chatSessionId).ChatEdited(chat);
		return new OkObjectResult(chat);
	}

	[Function("GetChatSessionSources")]
	public async Task<IActionResult> GetSourcesAsync(
	[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "chatSession/{chatSessionId:guid}/sources")] HttpRequest req,
	Guid chatSessionId,
	FunctionContext executionContext)
	{
		var (isAuthenticated, authenticationResponse) = await req.HttpContext.AuthenticateAzureFunctionAsync();

		var userId = req.HttpContext.User.GetObjectId();

		if (!isAuthenticated || string.IsNullOrEmpty(userId))
			return authenticationResponse ?? new UnauthorizedResult();

		var log = executionContext.GetLogger<ChatHistorySurface>();

		log.LogInformation("Get imported sources of chat session {0}", chatSessionId);

		if (await this._sessionRepository.TryFindByIdAsync(chatSessionId.ToString(), v => _ = v))
		{
			var sources = await this._sourceRepository.FindByChatIdAsync(chatSessionId.ToString());
			return new OkObjectResult(sources);
		}

		return new NotFoundObjectResult($"No chat session found for chat id '{chatSessionId}'.");
	}

	/// <summary>
	/// Save a bot response to the chat session.
	/// </summary>
	/// <param name="response">The bot response.</param>
	/// <param name="prompt">The prompt that was used to generate the response.</param>
	/// <param name="chatId">The chat id.</param>
	private async Task SaveResponseAsync(string response, string prompt, string chatId)
	{
		// Make sure the chat session exists
		await _sessionRepository.FindByIdAsync(chatId);

		var chatMessage = ChatMessage.CreateBotResponseMessage(chatId, response, prompt);
		await _messageRepository.CreateAsync(chatMessage);
	}


}
