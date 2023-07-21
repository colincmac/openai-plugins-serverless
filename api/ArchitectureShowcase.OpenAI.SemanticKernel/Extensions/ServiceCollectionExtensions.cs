using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Options;
using ArchitectureShowcase.OpenAI.SemanticKernel.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Extensions;
public static class ServiceCollectionExtensions
{

	public static IServiceCollection AddAIServiceOptions(this IServiceCollection services, IConfiguration configuration)
	{
		// General configuration
		services.AddOptions<ServiceOptions>()
			.Bind(configuration.GetSection(ServiceOptions.PropertyName))
			.PostConfigure(TrimStringProperties);

		// Default AI service configurations for Semantic Kernel
		services.AddOptions<AIServiceOptions>()
			.Bind(configuration.GetSection(AIServiceOptions.PropertyName))
			.PostConfigure(TrimStringProperties);

		// Memory store configuration
		services.AddOptions<MemoryStoreOptions>()
			.Bind(configuration.GetSection(MemoryStoreOptions.PropertyName))
			.PostConfigure(TrimStringProperties);

		return services;
	}

	/// <summary>
	/// Parse configuration into options.
	/// </summary>
	public static IServiceCollection AddCopilotChatOptions(this IServiceCollection services, IConfiguration configuration)
	{

		// Chat log storage configuration
		services.AddOptions<ChatStoreOptions>()
			.Bind(configuration.GetSection(ChatStoreOptions.PropertyName))
			.PostConfigure(TrimStringProperties);

		// Azure speech token configuration
		services.AddOptions<AzureSpeechOptions>()
			.Bind(configuration.GetSection(AzureSpeechOptions.PropertyName))
			.PostConfigure(TrimStringProperties);

		// Bot schema configuration
		services.AddOptions<BotSchemaOptions>()
			.Bind(configuration.GetSection(BotSchemaOptions.PropertyName))
			.PostConfigure(TrimStringProperties);

		// Document memory options
		services.AddOptions<DocumentMemoryOptions>()
			.Bind(configuration.GetSection(DocumentMemoryOptions.PropertyName))
			.PostConfigure(TrimStringProperties);

		// Chat prompt options
		services.AddOptions<PromptsOptions>()
			.Bind(configuration.GetSection(PromptsOptions.PropertyName))
			.PostConfigure(TrimStringProperties);

		// Planner options
		services.AddOptions<PlannerOptions>()
			.Bind(configuration.GetSection(PlannerOptions.PropertyName))
			.PostConfigure(TrimStringProperties);

		return services;
	}

	/// <summary>
	/// Add persistent chat store services.
	/// </summary>
	public static void AddPersistentChatStore(this IServiceCollection services)
	{
		IStorageContext<ChatSession> chatSessionInMemoryContext;
		IStorageContext<ChatMessage> chatMessageInMemoryContext;
		IStorageContext<MemorySource> chatMemorySourceInMemoryContext;
		IStorageContext<ChatParticipant> chatParticipantStorageContext;

		var chatStoreConfig = services.BuildServiceProvider().GetRequiredService<IOptions<ChatStoreOptions>>().Value;

		switch (chatStoreConfig.Type)
		{
			case ChatStoreOptions.ChatStoreType.Volatile:
				{
					chatSessionInMemoryContext = new VolatileContext<ChatSession>();
					chatMessageInMemoryContext = new VolatileContext<ChatMessage>();
					chatMemorySourceInMemoryContext = new VolatileContext<MemorySource>();
					chatParticipantStorageContext = new VolatileContext<ChatParticipant>();
					break;
				}

			case ChatStoreOptions.ChatStoreType.Filesystem:
				{
					if (chatStoreConfig.Filesystem == null)
					{
						throw new InvalidOperationException("ChatStore:Filesystem is required when ChatStore:Type is 'Filesystem'");
					}

					var fullPath = Path.GetFullPath(chatStoreConfig.Filesystem.FilePath);
					var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
					chatSessionInMemoryContext = new FileSystemContext<ChatSession>(
						new FileInfo(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_sessions{Path.GetExtension(fullPath)}")));
					chatMessageInMemoryContext = new FileSystemContext<ChatMessage>(
						new FileInfo(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_messages{Path.GetExtension(fullPath)}")));
					chatMemorySourceInMemoryContext = new FileSystemContext<MemorySource>(
						new FileInfo(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_memorysources{Path.GetExtension(fullPath)}")));
					chatParticipantStorageContext = new FileSystemContext<ChatParticipant>(
						new FileInfo(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_participants{Path.GetExtension(fullPath)}")));
					break;
				}

			case ChatStoreOptions.ChatStoreType.Cosmos:
				{
					if (chatStoreConfig.Cosmos == null)
					{
						throw new InvalidOperationException("ChatStore:Cosmos is required when ChatStore:Type is 'Cosmos'");
					}
#pragma warning disable CA2000 // Dispose objects before losing scope - objects are singletons for the duration of the process and disposed when the process exits.
					chatSessionInMemoryContext = new CosmosDbContext<ChatSession>(
						chatStoreConfig.Cosmos.ConnectionString, chatStoreConfig.Cosmos.Database, chatStoreConfig.Cosmos.ChatSessionsContainer);
					chatMessageInMemoryContext = new CosmosDbContext<ChatMessage>(
						chatStoreConfig.Cosmos.ConnectionString, chatStoreConfig.Cosmos.Database, chatStoreConfig.Cosmos.ChatMessagesContainer);
					chatMemorySourceInMemoryContext = new CosmosDbContext<MemorySource>(
						chatStoreConfig.Cosmos.ConnectionString, chatStoreConfig.Cosmos.Database, chatStoreConfig.Cosmos.ChatMemorySourcesContainer);
					chatParticipantStorageContext = new CosmosDbContext<ChatParticipant>(
						chatStoreConfig.Cosmos.ConnectionString, chatStoreConfig.Cosmos.Database, chatStoreConfig.Cosmos.ChatParticipantsContainer);

#pragma warning restore CA2000 // Dispose objects before losing scope
					break;
				}

			default:
				{
					throw new InvalidOperationException(
						"Invalid 'ChatStore' setting 'chatStoreConfig.Type'.");
				}
		}

		services.AddSingleton<ChatSessionRepository>(new ChatSessionRepository(chatSessionInMemoryContext));
		services.AddSingleton<ChatMessageRepository>(new ChatMessageRepository(chatMessageInMemoryContext));
		services.AddSingleton<ChatMemorySourceRepository>(new ChatMemorySourceRepository(chatMemorySourceInMemoryContext));
		services.AddSingleton<ChatParticipantRepository>(new ChatParticipantRepository(chatParticipantStorageContext));

	}

	/// <summary>
	/// Trim all string properties, recursively.
	/// </summary>
	private static void TrimStringProperties<T>(T options) where T : class
	{
		Queue<object> targets = new();
		targets.Enqueue(options);

		while (targets.Count > 0)
		{
			var target = targets.Dequeue();
			var targetType = target.GetType();
			foreach (var property in targetType.GetProperties())
			{
				// Skip enumerations
				if (property.PropertyType.IsEnum)
				{
					continue;
				}

				// Property is a built-in type, readable, and writable.
				if (property.PropertyType.Namespace == "System" &&
					property.CanRead &&
					property.CanWrite)
				{
					// Property is a non-null string.
					if (property.PropertyType == typeof(string) &&
						property.GetValue(target) != null)
					{
						property.SetValue(target, property.GetValue(target)!.ToString()!.Trim());
					}
				}
				else
				{
					// Property is a non-built-in and non-enum type - queue it for processing.
					if (property.GetValue(target) != null)
					{
						targets.Enqueue(property.GetValue(target)!);
					}
				}
			}
		}
	}

}
