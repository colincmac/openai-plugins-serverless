using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Options;
using ArchitectureShowcase.OpenAI.SemanticKernel.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI.TextCompletion;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.TemplateEngine;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Plugins.SemanticChat;
public class ChatPlugin
{
	/// <summary>
	/// A kernel instance to create a completion function since each invocation
	/// of the <see cref="ChatAsync"/> function will generate a new prompt dynamically.
	/// </summary>
	private readonly IKernel _kernel;

	/// <summary>
	/// A repository to save and retrieve chat messages.
	/// </summary>
	private readonly ChatMessageRepository _chatMessageRepository;

	/// <summary>
	/// A repository to save and retrieve chat sessions.
	/// </summary>
	private readonly ChatSessionRepository _chatSessionRepository;

	/// <summary>
	/// Settings containing prompt texts.
	/// </summary>
	private readonly PromptsOptions _promptOptions;

	/// <summary>
	/// A semantic chat memory skill instance to query semantic memories.
	/// </summary>
	private readonly SemanticChatMemoryFunctions _semanticChatMemorySkill;

	/// <summary>
	/// A document memory skill instance to query document memories.
	/// </summary>
	private readonly DocumentMemoryFunctions _documentMemorySkill;

	/// <summary>
	/// A skill instance to acquire external information.
	/// </summary>
	private readonly ExternalInformationFunctions _externalInformationSkill;

	private readonly ILogger _log;

	/// <summary>
	/// Create a new instance of <see cref="ChatPlugin"/>.
	/// </summary>
	public ChatPlugin(
		IKernel kernel,
		ChatMessageRepository chatMessageRepository,
		ChatSessionRepository chatSessionRepository,
		IOptions<PromptsOptions> promptOptions,
		IOptions<DocumentMemoryOptions> documentImportOptions,
		ChatPlanner planner,
		ILogger logger)
	{
		_kernel = kernel;
		_chatMessageRepository = chatMessageRepository;
		_chatSessionRepository = chatSessionRepository;
		_promptOptions = promptOptions.Value;

		_semanticChatMemorySkill = new SemanticChatMemoryFunctions(
			promptOptions);
		_documentMemorySkill = new DocumentMemoryFunctions(
			promptOptions,
			documentImportOptions);
		_externalInformationSkill = new ExternalInformationFunctions(
			promptOptions,
			planner);
		_log = logger;
	}

	/// <summary>
	/// Extract user intent from the conversation history.
	/// </summary>
	/// <param name="context">The SKContext.</param>
	[SKFunction, Description("Extract user intent")]
	[SKParameter("chatId", "Chat ID to extract history from")]
	[SKParameter("audience", "The audience the chat bot is interacting with")]
	public async Task<string> ExtractUserIntentAsync(SKContext context)
	{
		var tokenLimit = _promptOptions.CompletionTokenLimit;
		var historyTokenBudget =
			tokenLimit -
			_promptOptions.ResponseTokenLimit -
			PluginUtilities.TokenCount(string.Join("\n", new string[]
				{
					_promptOptions.SystemDescription,
					_promptOptions.SystemIntent,
					_promptOptions.SystemIntentContinuation
				})
			);

		// Clone the context to avoid modifying the original context variables.
		var intentExtractionContext = PluginUtilities.CopyContextWithVariablesClone(context);
		intentExtractionContext.Variables.Set(SemanticContextConstants.TokenLimitKey, historyTokenBudget.ToString(new NumberFormatInfo()));
		intentExtractionContext.Variables.Set(SemanticContextConstants.KnowledgeCutoffKey, _promptOptions.KnowledgeCutoffDate);

		var completionFunction = _kernel.CreateSemanticFunction(
			_promptOptions.SystemIntentExtraction,
			skillName: nameof(ChatPlugin),
			description: "Complete the prompt.");

		var result = await completionFunction.InvokeAsync(
			intentExtractionContext,
			settings: CreateIntentCompletionSettings()
		);

		if (result.ErrorOccurred)
		{
			context.Log.LogError("{0}: {1}", result.LastErrorDescription, result.LastException);
			context.Fail(result.LastErrorDescription);
			return string.Empty;
		}

		return $"User intent: {result}";
	}

	/// <summary>
	/// Extract the list of participants from the conversation history.
	/// Note that only those who have spoken will be included.
	/// </summary>
	[SKFunction, Description("Extract audience list")]
	[SKParameter("chatId", "Chat ID to extract history from")]
	public async Task<string> ExtractAudienceAsync(SKContext context)
	{
		var tokenLimit = _promptOptions.CompletionTokenLimit;
		var historyTokenBudget =
			tokenLimit -
			_promptOptions.ResponseTokenLimit -
			PluginUtilities.TokenCount(string.Join("\n", new string[]
				{
					_promptOptions.SystemAudience,
					_promptOptions.SystemAudienceContinuation,
				})
			);

		// Clone the context to avoid modifying the original context variables.
		var audienceExtractionContext = PluginUtilities.CopyContextWithVariablesClone(context);
		audienceExtractionContext.Variables.Set(SemanticContextConstants.TokenLimitKey, historyTokenBudget.ToString(new NumberFormatInfo()));

		var completionFunction = _kernel.CreateSemanticFunction(
			_promptOptions.SystemAudienceExtraction,
			skillName: nameof(ChatPlugin),
			description: "Complete the prompt.");

		var result = await completionFunction.InvokeAsync(
			audienceExtractionContext,
			settings: CreateIntentCompletionSettings()
		);

		if (result.ErrorOccurred)
		{
			context.Log.LogError("{0}: {1}", result.LastErrorDescription, result.LastException);
			context.Fail(result.LastErrorDescription);
			return string.Empty;
		}

		return $"List of participants: {result}";
	}

	/// <summary>
	/// Extract chat history.
	/// </summary>
	/// <param name="context">Contains the 'tokenLimit' controlling the length of the prompt.</param>
	[SKFunction, Description("Extract chat history")]
	public async Task<string> ExtractChatHistoryAsync(
		[Description("Chat ID to extract history from")] string chatId,
		[Description("Maximum number of tokens")] int tokenLimit)
	{
		var messages = await _chatMessageRepository.FindByChatIdAsync(chatId);
		var sortedMessages = messages.OrderByDescending(m => m.Timestamp);

		var remainingToken = tokenLimit;

		var historyText = "";
		foreach (var chatMessage in sortedMessages)
		{
			var formattedMessage = chatMessage.ToFormattedString();

			// Plan object is not meaningful content in generating bot response, so shorten to intent only to save on tokens
			if (formattedMessage.Contains("proposedPlan\":", StringComparison.InvariantCultureIgnoreCase))
			{
				var pattern = @"(\[.*?\]).*User Intent:User intent: (.*)(?=""}})";
				var match = Regex.Match(formattedMessage, pattern);
				if (match.Success)
				{
					var timestamp = match.Groups[1].Value.Trim();
					var userIntent = match.Groups[2].Value.Trim();

					formattedMessage = $"{timestamp} Bot proposed plan to fulfill user intent: {userIntent}";
				}
				else
				{
					formattedMessage = "Bot proposed plan";
				}
			}

			var tokenCount = PluginUtilities.TokenCount(formattedMessage);

			if (remainingToken - tokenCount >= 0)
			{
				historyText = $"{formattedMessage}\n{historyText}";
				remainingToken -= tokenCount;
			}
			else
			{
				break;
			}
		}

		return $"Chat history:\n{historyText.Trim()}";
	}

	/// <summary>
	/// This is the entry point for getting a chat response. It manages the token limit, saves
	/// messages to memory, and fill in the necessary context variables for completing the
	/// prompt that will be rendered by the template engine.
	/// </summary>
	[SKFunction, Description("Get chat response")]
	public async Task<SKContext> ChatAsync(
		[Description("The new message")] string message,
		[Description("Unique and persistent identifier for the user")] string userId,
		[Description("Name of the user")] string userName,
		[Description("Unique and persistent identifier for the chat")] string chatId,
		[Description("Type of the message")] string messageType,
		[Description("Previously proposed plan that is approved"), DefaultValue(null), SKName("proposedPlan")] string? planJson,
		[Description("ID of the response message for planner"), DefaultValue(null), SKName("responseMessageId")] string? messageId,
		SKContext context)
	{
		// Save this new message to memory such that subsequent chat responses can use it
		await SaveNewMessageAsync(message, userId, userName, chatId, messageType);

		// Clone the context to avoid modifying the original context variables.
		var chatContext = PluginUtilities.CopyContextWithVariablesClone(context);
		chatContext.Variables.Set(SemanticContextConstants.KnowledgeCutoffKey, _promptOptions.KnowledgeCutoffDate);

		// Check if plan exists in ask's context variables.
		// If plan was returned at this point, that means it was approved or cancelled.
		// Update the response previously saved in chat history with state
		if (!string.IsNullOrWhiteSpace(planJson) &&
			!string.IsNullOrEmpty(messageId))
		{
			await UpdateResponseAsync(planJson, messageId);
		}

		var response = chatContext.Variables.ContainsKey(SemanticContextConstants.UserCancelledPlanKey)
			? "I am sorry the plan did not meet your goals."
			: await GetChatResponseAsync(chatId, chatContext);

		if (chatContext.ErrorOccurred)
		{
			context.Fail(chatContext.LastErrorDescription);
			return context;
		}

		// Retrieve the prompt used to generate the response
		// and return it to the caller via the context variables.
		chatContext.Variables.TryGetValue(SemanticContextConstants.PromptKey, out var prompt);
		prompt ??= string.Empty;
		context.Variables.Set(SemanticContextConstants.PromptKey, prompt);

		// Save this response to memory such that subsequent chat responses can use it
		var botMessage = await SaveNewResponseAsync(response, prompt, chatId);
		context.Variables.Set(SemanticContextConstants.MessageIdKey, botMessage.Id);
		context.Variables.Set(SemanticContextConstants.MessageTypeKey, ((int)botMessage.Type).ToString(CultureInfo.InvariantCulture));

		// Extract semantic chat memory
		await SemanticChatMemoryExtractor.ExtractSemanticChatMemoryAsync(
			chatId,
			_kernel,
			chatContext,
			_promptOptions);

		context.Variables.Update(response);
		return context;
	}

	#region Private

	/// <summary>
	/// Generate the necessary chat context to create a prompt then invoke the model to get a response.
	/// </summary>
	/// <param name="chatContext">The SKContext.</param>
	/// <returns>A response from the model.</returns>
	private async Task<string> GetChatResponseAsync(string chatId, SKContext chatContext)
	{
		// 0. Get the audience
		var audience = await GetAudienceAsync(chatContext);
		if (chatContext.ErrorOccurred)
		{
			return string.Empty;
		}

		// 1. Extract user intent from the conversation history.
		var userIntent = await GetUserIntentAsync(chatContext);
		if (chatContext.ErrorOccurred)
		{
			return string.Empty;
		}

		// 2. Calculate the remaining token budget.
		var remainingToken = GetChatContextTokenLimit(userIntent);

		// 3. Acquire external information from planner
		var externalInformationTokenLimit = (int)(remainingToken * _promptOptions.ExternalInformationContextWeight);
		var planResult = await AcquireExternalInformationAsync(chatContext, userIntent, externalInformationTokenLimit);
		if (chatContext.ErrorOccurred)
		{
			return string.Empty;
		}

		// If plan is suggested, send back to user for approval before running
		if (_externalInformationSkill.ProposedPlan != null)
		{
			return JsonSerializer.Serialize(_externalInformationSkill.ProposedPlan);
		}

		// 4. Query relevant semantic memories
		var chatMemoriesTokenLimit = (int)(remainingToken * _promptOptions.MemoriesResponseContextWeight);
		var chatMemories = await _semanticChatMemorySkill.QueryMemoriesAsync(userIntent, chatId, chatMemoriesTokenLimit, chatContext.Memory);
		if (chatContext.ErrorOccurred)
		{
			return string.Empty;
		}

		// 5. Query relevant document memories
		var documentContextTokenLimit = (int)(remainingToken * _promptOptions.DocumentContextWeight);
		var documentMemories = await _documentMemorySkill.QueryDocumentsAsync(userIntent, chatId, documentContextTokenLimit, chatContext.Memory);
		if (chatContext.ErrorOccurred)
		{
			return string.Empty;
		}

		// 6. Fill in the chat history if there is any token budget left
		var chatContextComponents = new List<string>() { chatMemories, documentMemories, planResult };
		var chatContextText = string.Join("\n\n", chatContextComponents.Where(c => !string.IsNullOrEmpty(c)));
		var chatContextTextTokenCount = remainingToken - PluginUtilities.TokenCount(chatContextText);
		if (chatContextTextTokenCount > 0)
		{
			var chatHistory = await ExtractChatHistoryAsync(chatId, chatContextTextTokenCount);
			if (chatContext.ErrorOccurred)
			{
				return string.Empty;
			}
			chatContextText = $"{chatContextText}\n{chatHistory}";
		}

		// Invoke the model
		chatContext.Variables.Set("audience", audience);
		chatContext.Variables.Set("UserIntent", userIntent);
		chatContext.Variables.Set("ChatContext", chatContextText);

		var promptRenderer = new PromptTemplateEngine();
		var renderedPrompt = await promptRenderer.RenderAsync(
			_promptOptions.SystemChatPrompt,
			chatContext);

		var completionFunction = _kernel.CreateSemanticFunction(
			renderedPrompt,
			skillName: nameof(ChatPlugin),
			description: "Complete the prompt.");

		chatContext = await completionFunction.InvokeAsync(
			context: chatContext,
			settings: CreateChatResponseCompletionSettings()
		);

		// Allow the caller to view the prompt used to generate the response
		chatContext.Variables.Set(SemanticContextConstants.PromptKey, renderedPrompt);

		if (chatContext.ErrorOccurred)
		{
			return string.Empty;
		}

		return chatContext.Result;
	}

	/// <summary>
	/// Helper function create the correct context variables to
	/// extract audience from the conversation history.
	/// </summary>
	private async Task<string> GetAudienceAsync(SKContext context)
	{
		var contextVariables = new ContextVariables();
		contextVariables.Set(SemanticContextConstants.ChatIdKey, context[SemanticContextConstants.ChatIdKey]);

		var audienceContext = new SKContext(
			contextVariables,
			context.Memory,
			context.Skills,
			context.Log,
			context.CancellationToken
		);

		var audience = await ExtractAudienceAsync(audienceContext);

		// Propagate the error
		if (audienceContext.ErrorOccurred)
		{
			context.Fail(audienceContext.LastErrorDescription);
		}

		return audience;
	}

	/// <summary>
	/// Helper function create the correct context variables to
	/// extract user intent from the conversation history.
	/// </summary>
	private async Task<string> GetUserIntentAsync(SKContext context)
	{
		// TODO: Regenerate user intent if plan was modified
		if (!context.Variables.TryGetValue(SemanticContextConstants.PlanUserIntentKey, out var userIntent))
		{
			var contextVariables = new ContextVariables();
			contextVariables.Set(SemanticContextConstants.ChatIdKey, context[SemanticContextConstants.ChatIdKey]);
			contextVariables.Set(SemanticContextConstants.AudienceKey, context[SemanticContextConstants.UserNameKey]);

			var intentContext = new SKContext(
				contextVariables,
				context.Memory,
				context.Skills,
				context.Log,
				context.CancellationToken
			);

			userIntent = await ExtractUserIntentAsync(intentContext);
			// Propagate the error
			if (intentContext.ErrorOccurred)
			{
				context.Fail(intentContext.LastErrorDescription);
			}
		}

		return userIntent;
	}

	/// <summary>
	/// Helper function create the correct context variables to
	/// query chat memories from the chat memory store.
	/// </summary>
	private Task<string> QueryChatMemoriesAsync(SKContext context, string userIntent, int tokenLimit)
	{
		return _semanticChatMemorySkill.QueryMemoriesAsync(userIntent, context[SemanticContextConstants.ChatIdKey], tokenLimit, context.Memory);
	}

	/// <summary>
	/// Helper function create the correct context variables to
	/// query document memories from the document memory store.
	/// </summary>
	private Task<string> QueryDocumentsAsync(SKContext context, string userIntent, int tokenLimit)
	{
		return _documentMemorySkill.QueryDocumentsAsync(userIntent, context[SemanticContextConstants.ChatIdKey], tokenLimit, context.Memory);
	}

	/// <summary>
	/// Helper function create the correct context variables to acquire external information.
	/// </summary>
	private async Task<string> AcquireExternalInformationAsync(SKContext context, string userIntent, int tokenLimit)
	{
		var contextVariables = context.Variables.Clone();
		contextVariables.Set(SemanticContextConstants.TokenLimitKey, tokenLimit.ToString(new NumberFormatInfo()));

		var planContext = new SKContext(
			contextVariables,
			context.Memory,
			context.Skills,
			context.Log,
			context.CancellationToken
		);

		var plan = await _externalInformationSkill.AcquireExternalInformationAsync(userIntent, planContext);

		// Propagate the error
		if (planContext.ErrorOccurred)
		{
			context.Fail(planContext.LastErrorDescription);
		}

		return plan;
	}

	/// <summary>
	/// Save a new message to the chat history.
	/// </summary>
	/// <param name="message">The message</param>
	/// <param name="userId">The user ID</param>
	/// <param name="userName"></param>
	/// <param name="chatId">The chat ID</param>
	/// <param name="type">Type of the message</param>
	private async Task<ChatMessage> SaveNewMessageAsync(string message, string userId, string userName, string chatId, string type)
	{
		// Make sure the chat exists.
		if (!await _chatSessionRepository.TryFindByIdAsync(chatId, v => _ = v))
		{
			throw new ArgumentException("Chat session does not exist.");
		}

		var chatMessage = new ChatMessage(
			userId,
			userName,
			chatId,
			message,
			"",
			ChatMessage.AuthorRoles.User,
			// Default to a standard message if the `type` is not recognized
			Enum.TryParse(type, out ChatMessage.ChatMessageType typeAsEnum) && Enum.IsDefined(typeof(ChatMessage.ChatMessageType), typeAsEnum)
				? typeAsEnum
				: ChatMessage.ChatMessageType.Message);

		await _chatMessageRepository.CreateAsync(chatMessage);
		return chatMessage;
	}

	/// <summary>
	/// Save a new response to the chat history.
	/// </summary>
	/// <param name="response">Response from the chat.</param>
	/// <param name="prompt">Prompt used to generate the response.</param>
	/// <param name="chatId">The chat ID</param>
	/// <returns>The created chat message.</returns>
	private async Task<ChatMessage> SaveNewResponseAsync(string response, string prompt, string chatId)
	{
		// Make sure the chat exists.
		if (!await _chatSessionRepository.TryFindByIdAsync(chatId, v => _ = v))
		{
			throw new ArgumentException("Chat session does not exist.");
		}

		var chatMessage = ChatMessage.CreateBotResponseMessage(chatId, response, prompt);
		await _chatMessageRepository.CreateAsync(chatMessage);

		return chatMessage;
	}

	/// <summary>
	/// Updates previously saved response in the chat history.
	/// </summary>
	/// <param name="updatedResponse">Updated response from the chat.</param>
	/// <param name="messageId">The chat message ID</param>
	private async Task UpdateResponseAsync(string updatedResponse, string messageId)
	{
		// Make sure the chat exists.
		var chatMessage = await _chatMessageRepository.FindByIdAsync(messageId);
		chatMessage.Content = updatedResponse;

		await _chatMessageRepository.UpsertAsync(chatMessage);
	}

	/// <summary>
	/// Create a completion settings object for chat response. Parameters are read from the PromptSettings class.
	/// </summary>
	private CompleteRequestSettings CreateChatResponseCompletionSettings()
	{
		return new CompleteRequestSettings
		{
			MaxTokens = _promptOptions.ResponseTokenLimit,
			Temperature = _promptOptions.ResponseTemperature,
			TopP = _promptOptions.ResponseTopP,
			FrequencyPenalty = _promptOptions.ResponseFrequencyPenalty,
			PresencePenalty = _promptOptions.ResponsePresencePenalty
		};
		;
	}

	/// <summary>
	/// Create a completion settings object for intent response. Parameters are read from the PromptSettings class.
	/// </summary>
	private CompleteRequestSettings CreateIntentCompletionSettings()
	{
		return new CompleteRequestSettings
		{
			MaxTokens = _promptOptions.ResponseTokenLimit,
			Temperature = _promptOptions.IntentTemperature,
			TopP = _promptOptions.IntentTopP,
			FrequencyPenalty = _promptOptions.IntentFrequencyPenalty,
			PresencePenalty = _promptOptions.IntentPresencePenalty,
			StopSequences = new string[] { "] bot:" }
		};
	}

	/// <summary>
	/// Calculate the remaining token budget for the chat response prompt.
	/// This is the token limit minus the token count of the user intent and the system commands.
	/// </summary>
	/// <param name="userIntent">The user intent returned by the model.</param>
	/// <returns>The remaining token limit.</returns>
	private int GetChatContextTokenLimit(string userIntent)
	{
		var tokenLimit = _promptOptions.CompletionTokenLimit;

		return tokenLimit -
			PluginUtilities.TokenCount(userIntent) -
			_promptOptions.ResponseTokenLimit -
			PluginUtilities.TokenCount(string.Join("\n", new string[]
				{
							_promptOptions.SystemDescription,
							_promptOptions.SystemResponse,
							_promptOptions.SystemChatContinuation
				})
			);
	}

	#endregion
}
