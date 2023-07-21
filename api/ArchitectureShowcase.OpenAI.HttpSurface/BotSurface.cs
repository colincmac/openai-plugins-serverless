using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Options;
using ArchitectureShowcase.OpenAI.SemanticKernel.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using System.Net;
using System.Text.Json;

namespace ArchitectureShowcase.OpenAI.HttpSurface;


public class BotSurface
{

	private readonly IKernel _semanticKernel;
	private readonly IMemoryStore? _memoryStore;
	private readonly ISemanticTextMemory _semanticMemory;
	private readonly ChatSessionRepository _chatRepository;
	private readonly ChatMessageRepository _chatMessageRepository;
	private readonly BotSchemaOptions _botSchemaOptions;
	private readonly AIServiceOptions _embeddingOptions;
	private readonly DocumentMemoryOptions _documentMemoryOptions;

	public BotSurface(IKernel semanticKernel, IMemoryStore? memoryStore, ISemanticTextMemory semanticMemory, ChatSessionRepository chatRepository, ChatMessageRepository chatMessageRepository, BotSchemaOptions botSchemaOptions, AIServiceOptions embeddingOptions, DocumentMemoryOptions documentMemoryOptions)
	{
		_semanticKernel = semanticKernel;
		_memoryStore = memoryStore;
		_semanticMemory = semanticMemory;
		_chatRepository = chatRepository;
		_chatMessageRepository = chatMessageRepository;
		_botSchemaOptions = botSchemaOptions;
		_embeddingOptions = embeddingOptions;
		_documentMemoryOptions = documentMemoryOptions;
	}


	/// <summary>
	/// Upload a bot.
	/// </summary>
	/// <param name="req">The HTTP request.</param>
	/// <param name="kernel">The Semantic Kernel instance.</param>
	/// <param name="userId">The user id.</param>
	/// <param name="bot">The bot object from the message body</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The HTTP action result with new chat session object.</returns>
	[Function("UploadBot")]
	[OpenApiOperation(operationId: "bot/upload", tags: new[] { "bot" })]
	[OpenApiParameter(name: "userId", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "The user id.")]
	[OpenApiRequestBody(contentType: "application/json", bodyType: typeof(Bot), Required = true, Description = "The bot object from the message body.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(ChatSession), Description = "The HTTP action result with new chat session object.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(string), Description = "The error message.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(string), Description = "The error message.")]
	public async Task<IActionResult> UploadBot(
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bot/upload")] HttpRequest req,
		CancellationToken cancellationToken)
	{

		var (isAuthenticated, authenticationResponse) = await req.HttpContext.AuthenticateAzureFunctionAsync();
		var userId = req.HttpContext.User.GetObjectId();
		var tenantId = req.HttpContext.User.GetTenantId();

		if (!isAuthenticated || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tenantId))
			return authenticationResponse ?? new UnauthorizedResult();

		var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
		var bot = JsonSerializer.Deserialize<Bot>(requestBody);

		if (bot == null || !IsBotCompatible(
				externalBotSchema: bot.Schema,
				externalBotEmbeddingConfig: bot.EmbeddingConfigurations,
				embeddingOptions: _embeddingOptions,
				botSchemaOptions: _botSchemaOptions))
		{
			return new BadRequestObjectResult("Incompatible schema. " +
								   $"The supported bot schema is {_botSchemaOptions.Name}/{_botSchemaOptions.Version} " +
								   $"for the {_embeddingOptions.Models.Embedding} model from {_embeddingOptions.Type}. " +
								   $"But the uploaded file is with schema {bot.Schema.Name}/{bot.Schema.Version} " +
								   $"for the {bot.EmbeddingConfigurations.DeploymentOrModelId} model from {bot.EmbeddingConfigurations.AIService}.");
		}

		var chatTitle = $"{bot.ChatTitle} - Clone";

		// Upload chat history into chat repository and embeddings into memory.

		// 1. Create a new chat and get the chat id.
		var newChat = new ChatSession(userId, chatTitle);
		await _chatRepository.CreateAsync(newChat);
		var chatId = newChat.Id;

		var oldChatId = bot.ChatHistory.First().ChatId;

		// 2. Update the app's chat storage.
		foreach (var message in bot.ChatHistory)
		{
			var chatMessage = new ChatMessage(
				message.UserId,
				message.UserName,
				chatId,
				message.Content,
				message.Prompt,
				ChatMessage.AuthorRoles.Participant)
			{
				Timestamp = message.Timestamp
			};
			await _chatMessageRepository.CreateAsync(chatMessage);
		}

		// 3. Update the memory.
		await BulkUpsertMemoryRecordsAsync(oldChatId, chatId, bot.Embeddings, cancellationToken);

		// TODO: Revert changes if any of the actions failed

		return new CreatedResult(
			$"api/chatHistory/{chatId}", // You may need to adjust the location header according to your routing scheme
			newChat);
	}

	/// <summary>
	/// Download a bot.
	/// </summary>
	/// <param name="kernel">The Semantic Kernel instance.</param>
	/// <param name="chatId">The chat id to be downloaded.</param>
	/// <returns>The serialized Bot object of the chat id.</returns>
	[Function("DownloadBot")]
	[OpenApiOperation(operationId: "bot/download", tags: new[] { "bot" })]
	[OpenApiParameter(name: "chatId", In = ParameterLocation.Path, Required = true, Type = typeof(Guid), Description = "The chat id to be downloaded.")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(string), Description = "The serialized Bot object of the chat id.")]
	[OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid input parameters.")]
	[OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "The chat id does not exist.")]
	public async Task<IActionResult> DownloadAsync(
		[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "bot/download/{chatId:guid}")] HttpRequest req,
		FunctionContext executionContext,
		Guid chatId)
	{
		var log = executionContext.GetLogger<BotSurface>();
		var (isAuthenticated, authenticationResponse) = await req.HttpContext.AuthenticateAzureFunctionAsync();
		var userId = req.HttpContext.User.GetObjectId();
		var tenantId = req.HttpContext.User.GetTenantId();

		if (!isAuthenticated || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tenantId))
			return authenticationResponse ?? new UnauthorizedResult();

		log.LogInformation("Received call to download a bot");
		var memory = await CreateBotAsync(kernel: _semanticKernel, chatId);

		return new OkObjectResult(JsonSerializer.Serialize(memory));
	}


	/// <summary>
	/// Check if an external bot file is compatible with the application.
	/// </summary>
	/// <remarks>
	/// If the embeddings are not generated from the same model, the bot file is not compatible.
	/// </remarks>
	/// <param name="externalBotSchema">The external bot schema.</param>
	/// <param name="externalBotEmbeddingConfig">The external bot embedding configuration.</param>
	/// <param name="embeddingOptions">The embedding options.</param>
	/// <param name="botSchemaOptions">The bot schema options.</param>
	/// <returns>True if the bot file is compatible with the app; otherwise false.</returns>
	private static bool IsBotCompatible(
			BotSchemaOptions externalBotSchema,
			BotEmbeddingConfig externalBotEmbeddingConfig,
			AIServiceOptions embeddingOptions,
			BotSchemaOptions botSchemaOptions)
	{
		// The app can define what schema/version it supports before the community comes out with an open schema.
		return externalBotSchema.Name.Equals(botSchemaOptions.Name, StringComparison.OrdinalIgnoreCase)
			   && externalBotSchema.Version == botSchemaOptions.Version
			   && externalBotEmbeddingConfig.AIService == embeddingOptions.Type
			   && externalBotEmbeddingConfig.DeploymentOrModelId.Equals(embeddingOptions.Models.Embedding, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Get memory from memory store and append the memory records to a given list.
	/// It will update the memory collection name in the new list if the newCollectionName is provided.
	/// </summary>
	/// <param name="kernel">The Semantic Kernel instance.</param>
	/// <param name="collectionName">The current collection name. Used to query the memory storage.</param>
	/// <param name="embeddings">The embeddings list where we will append the fetched memory records.</param>
	/// <param name="newCollectionName">
	/// The new collection name when appends to the embeddings list. Will use the old collection name if not provided.
	/// </param>
	private static async Task GetMemoryRecordsAndAppendToEmbeddingsAsync(
		IKernel kernel,
		string collectionName,
		List<KeyValuePair<string, List<MemoryQueryResult>>> embeddings,
		string newCollectionName = "")
	{
		var collectionMemoryRecords = await kernel.Memory.SearchAsync(
			collectionName,
			"abc", // dummy query since we don't care about relevance. An empty string will cause exception.
			limit: 999999999, // temp solution to get as much as record as a workaround.
			minRelevanceScore: -1, // no relevance required since the collection only has one entry
			withEmbeddings: true,
			cancellationToken: default
		).ToListAsync();

		embeddings.Add(new KeyValuePair<string, List<MemoryQueryResult>>(
			string.IsNullOrEmpty(newCollectionName) ? collectionName : newCollectionName,
			collectionMemoryRecords));
	}

	/// <summary>
	/// Prepare the bot information of a given chat.
	/// </summary>
	/// <param name="kernel">The semantic kernel object.</param>
	/// <param name="chatId">The chat id of the bot</param>
	/// <returns>A Bot object that represents the chat session.</returns>
	private async Task<Bot> CreateBotAsync(IKernel kernel, Guid chatId)
	{
		var chatIdString = chatId.ToString();
		var bot = new Bot
		{
			// get the bot schema version
			Schema = _botSchemaOptions,

			// get the embedding configuration
			EmbeddingConfigurations = new BotEmbeddingConfig
			{
				AIService = _embeddingOptions.Type,
				DeploymentOrModelId = _embeddingOptions.Models.Embedding
			}
		};

		// get the chat title
		var chat = await _chatRepository.FindByIdAsync(chatIdString);
		bot.ChatTitle = chat.Title;

		// get the chat history
		bot.ChatHistory = await GetAllChatMessagesAsync(chatIdString);

		// get the memory collections associated with this chat
		// TODO: filtering memory collections by name might be fragile.
		var chatCollections = (await kernel.Memory.GetCollectionsAsync())
			.Where(collection => collection.StartsWith(chatIdString, StringComparison.OrdinalIgnoreCase));

		foreach (var collection in chatCollections)
		{
			await GetMemoryRecordsAndAppendToEmbeddingsAsync(kernel: kernel, collectionName: collection, embeddings: bot.Embeddings);
		}

		// get the document memory collection names (global scope)
		await GetMemoryRecordsAndAppendToEmbeddingsAsync(
			kernel: kernel,
			collectionName: _documentMemoryOptions.GlobalDocumentCollectionName,
			embeddings: bot.DocumentEmbeddings);

		// get the document memory collection names (user scope)
		await GetMemoryRecordsAndAppendToEmbeddingsAsync(
			kernel: kernel,
			collectionName: _documentMemoryOptions.ChatDocumentCollectionNamePrefix + chatIdString,
			embeddings: bot.DocumentEmbeddings);

		return bot;
	}

	/// <summary>
	/// Get chat messages of a given chat id.
	/// </summary>
	/// <param name="chatId">The chat id</param>
	/// <returns>The list of chat messages in descending order of the timestamp</returns>
	private async Task<List<ChatMessage>> GetAllChatMessagesAsync(string chatId)
	{
		// TODO: We might want to set limitation on the number of messages that are pulled from the storage.
		return (await _chatMessageRepository.FindByChatIdAsync(chatId))
			.OrderByDescending(m => m.Timestamp).ToList();
	}

	/// <summary>
	/// Bulk upsert memory records into memory store.
	/// </summary>
	/// <param name="oldChatId">The original chat id of the memory records.</param>
	/// <param name="chatId">The new chat id that will replace the original chat id.</param>
	/// <param name="embeddings">The list of embeddings of the chat id.</param>
	/// <returns>The function doesn't return anything.</returns>
	private async Task BulkUpsertMemoryRecordsAsync(string oldChatId, string chatId, List<KeyValuePair<string, List<MemoryQueryResult>>> embeddings, CancellationToken cancellationToken = default)
	{
		foreach (var collection in embeddings)
		{
			foreach (var record in collection.Value)
			{
				if (record != null && record.Embedding != null)
				{
					var newCollectionName = collection.Key.Replace(oldChatId, chatId, StringComparison.OrdinalIgnoreCase);

					if (_memoryStore == null)
					{
						await _semanticMemory.SaveInformationAsync(
							collection: newCollectionName,
							text: record.Metadata.Text,
							id: record.Metadata.Id,
							cancellationToken: cancellationToken);
					}
					else
					{
						var data = MemoryRecord.LocalRecord(
							id: record.Metadata.Id,
							text: record.Metadata.Text,
							embedding: record.Embedding.Value,
							description: null,
							additionalMetadata: null);

						if (!await _memoryStore.DoesCollectionExistAsync(newCollectionName, default))
						{
							await _memoryStore.CreateCollectionAsync(newCollectionName, default);
						}

						await _memoryStore.UpsertAsync(newCollectionName, data, default);
					}
				}
			}
		}
	}
}
