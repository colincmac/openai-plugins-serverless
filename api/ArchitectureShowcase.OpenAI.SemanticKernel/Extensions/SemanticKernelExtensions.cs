using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Options;
using ArchitectureShowcase.OpenAI.SemanticKernel.Plugins.SemanticChat;
using ArchitectureShowcase.OpenAI.SemanticKernel.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI.Embeddings;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI.TextEmbedding;
using Microsoft.SemanticKernel.Connectors.Memory.AzureCognitiveSearch;
using Microsoft.SemanticKernel.Connectors.Memory.Qdrant;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Skills.Core;
using Microsoft.SemanticKernel.TemplateEngine;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Extensions;
public static class SemanticKernelExtensions
{
	/// <summary>
	/// Delegate to register skills with a Semantic Kernel
	/// </summary>
	public delegate Task RegisterSkillsWithKernel(IServiceProvider sp, IKernel kernel);

	/// <summary>
	/// Delegate to register skills with a Copilot Planner
	/// </summary>
	public delegate Task RegisterSkillsWithPlannerKernel(IServiceProvider sp, IKernel kernel);

	/// <summary>
	/// Add Semantic Kernel services
	/// </summary>
	public static IServiceCollection AddSemanticKernelServices(this IServiceCollection services)
	{
		// Semantic Kernel
		services.AddScoped(sp =>
		{
			var kernel = Kernel.Builder
				.WithLogger(sp.GetRequiredService<ILogger<IKernel>>())
				.WithMemory(sp.GetRequiredService<ISemanticTextMemory>())
				.WithCompletionBackend(sp.GetRequiredService<IOptions<AIServiceOptions>>().Value)
				.WithEmbeddingBackend(sp.GetRequiredService<IOptions<AIServiceOptions>>().Value)
				.Build();

			sp.GetRequiredService<RegisterSkillsWithKernel>()(sp, kernel);
			return kernel;
		});

		// Semantic memory
		services.AddSemanticTextMemory();

		// Register skills
		services.AddScoped<RegisterSkillsWithKernel>(sp => RegisterKernelPluginsAsync);

		return services;
	}

	/// <summary>
	/// Add Planner services
	/// </summary>
	public static IServiceCollection AddCopilotChatPlannerServices(this IServiceCollection services)
	{
		services.AddScoped(sp =>
		{
			var plannerKernel = Kernel.Builder
				.WithLogger(sp.GetRequiredService<ILogger<IKernel>>())
				.WithPlannerBackend(sp.GetRequiredService<IOptions<AIServiceOptions>>().Value)
				.Build();

			sp.GetRequiredService<RegisterSkillsWithPlannerKernel>()(sp, plannerKernel);
			return new ChatPlanner(plannerKernel, sp.GetRequiredService<IOptions<PlannerOptions>>().Value);
		});
		services.AddScoped<RegisterSkillsWithPlannerKernel>(sp => RegisterPlannerPlugins);

		return services;
	}


	/// <summary>
	/// Register the skills with the kernel.
	/// </summary>
	private static Task RegisterKernelPluginsAsync(IServiceProvider sp, IKernel kernel)
	{
		kernel.RegisterCopilotChatPlugins(sp);

		kernel.ImportSkill(new TimeSkill(), nameof(TimeSkill));

		// Semantic skills
		var options = sp.GetRequiredService<IOptions<ServiceOptions>>().Value;
		if (!string.IsNullOrWhiteSpace(options.SemanticSkillsDirectory))
		{
			foreach (var subDir in Directory.GetDirectories(options.SemanticSkillsDirectory))
			{
				try
				{
					kernel.ImportSemanticSkillFromDirectory(options.SemanticSkillsDirectory, Path.GetFileName(subDir)!);
				}
				catch (TemplateException e)
				{
					kernel.Log.LogError("Could not load skill from {Directory}: {Message}", subDir, e.Message);
				}
			}
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Register the Copilot chat skills with the kernel.
	/// </summary>
	public static IKernel RegisterCopilotChatPlugins(this IKernel kernel, IServiceProvider sp)
	{
		// Chat skill
		kernel.ImportSkill(new ChatPlugin(
				kernel: kernel,
				chatMessageRepository: sp.GetRequiredService<ChatMessageRepository>(),
				chatSessionRepository: sp.GetRequiredService<ChatSessionRepository>(),
				promptOptions: sp.GetRequiredService<IOptions<PromptsOptions>>(),
				documentImportOptions: sp.GetRequiredService<IOptions<DocumentMemoryOptions>>(),
				planner: sp.GetRequiredService<ChatPlanner>(),
				logger: sp.GetRequiredService<ILogger<ChatPlugin>>()),
			nameof(ChatPlugin));

		return kernel;
	}


	/// <summary>
	/// Register skills with the planner's kernel.
	/// </summary>
	private static Task RegisterPlannerPlugins(IServiceProvider sp, IKernel plannerKernel)
	{
		// TODO Can't seem to get the auth headers through FunctionContext or HttpContextAccessor without a hacky solution https://github.com/Azure/azure-functions-dotnet-worker/issues/950
		// It's possible that a functions middleware could register a service before invocation though
		return Task.CompletedTask;
	}

	/// <summary>
	/// Add the semantic memory.
	/// </summary>
	private static void AddSemanticTextMemory(this IServiceCollection services)
	{
		var config = services.BuildServiceProvider().GetRequiredService<IOptions<MemoryStoreOptions>>().Value;
		switch (config.Type)
		{
			case MemoryStoreOptions.MemoryStoreType.Volatile:
				services.AddSingleton<IMemoryStore, VolatileMemoryStore>();
				services.AddScoped<ISemanticTextMemory>(sp => new SemanticTextMemory(
					sp.GetRequiredService<IMemoryStore>(),
					sp.GetRequiredService<IOptions<AIServiceOptions>>().Value
						.ToTextEmbeddingsService(logger: sp.GetRequiredService<ILogger<AIServiceOptions>>())));
				break;

			case MemoryStoreOptions.MemoryStoreType.Qdrant:
				if (config.Qdrant == null)
				{
					throw new InvalidOperationException("MemoriesStore type is Qdrant and Qdrant configuration is null.");
				}


				services.AddSingleton<IMemoryStore>(sp =>
				{
					HttpClient httpClient = new(new HttpClientHandler { CheckCertificateRevocationList = true });
					if (!string.IsNullOrWhiteSpace(config.Qdrant.Key))
					{
						httpClient.DefaultRequestHeaders.Add("api-key", config.Qdrant.Key);
					}

					var endPointBuilder = new UriBuilder(config.Qdrant.Host)
					{
						Port = config.Qdrant.Port
					};

					return new QdrantMemoryStore(
						httpClient: httpClient,
						config.Qdrant.VectorSize,
						endPointBuilder.ToString(),
						logger: sp.GetRequiredService<ILogger<IQdrantVectorDbClient>>()
					);
				});
				services.AddScoped<ISemanticTextMemory>(sp => new SemanticTextMemory(
					sp.GetRequiredService<IMemoryStore>(),
					sp.GetRequiredService<IOptions<AIServiceOptions>>().Value
						.ToTextEmbeddingsService(logger: sp.GetRequiredService<ILogger<AIServiceOptions>>())));
				break;

			case MemoryStoreOptions.MemoryStoreType.AzureCognitiveSearch:
				if (config.AzureCognitiveSearch == null)
				{
					throw new InvalidOperationException("MemoryStore type is AzureCognitiveSearch and AzureCognitiveSearch configuration is null.");
				}

				services.AddSingleton<ISemanticTextMemory>(sp => new AzureCognitiveSearchMemory(config.AzureCognitiveSearch.Endpoint, config.AzureCognitiveSearch.Key));
				break;

			default:
				throw new InvalidOperationException($"Invalid 'MemoriesStore' type '{config.Type}'.");
		}

		// High level semantic memory implementations, such as Azure Cognitive Search, do not allow for providing embeddings when storing memories.
		// We wrap the memory store in an optional memory store to allow controllers to pass dependency injection validation and potentially optimize
		// for a lower-level memory implementation (e.g. Qdrant). Lower level memory implementations (i.e., IMemoryStore) allow for reusing embeddings,
		// whereas high level memory implementation (i.e., ISemanticTextMemory) assume embeddings get recalculated on every write.
		services.AddSingleton(sp => new NullMemoryStore() { MemoryStore = sp.GetService<IMemoryStore>() });
	}

	/// <summary>
	/// Add the completion backend to the kernel config
	/// </summary>
	private static KernelBuilder WithCompletionBackend(this KernelBuilder kernelBuilder, AIServiceOptions options)
	{
		return options.Type switch
		{
			AIServiceTypeEnum.AzureOpenAI
				=> kernelBuilder.WithAzureChatCompletionService(options.Models.Completion, options.Endpoint, options.Key),
			AIServiceTypeEnum.OpenAI
				=> kernelBuilder.WithOpenAIChatCompletionService(options.Models.Completion, options.Key),
			_
				=> throw new ArgumentException($"Invalid {nameof(options.Type)} value in '{AIServiceOptions.PropertyName}' settings."),
		};
	}

	/// <summary>
	/// Add the embedding backend to the kernel config
	/// </summary>
	private static KernelBuilder WithEmbeddingBackend(this KernelBuilder kernelBuilder, AIServiceOptions options)
	{
		return options.Type switch
		{
			AIServiceTypeEnum.AzureOpenAI
				=> kernelBuilder.WithAzureTextEmbeddingGenerationService(options.Models.Embedding, options.Endpoint, options.Key),
			AIServiceTypeEnum.OpenAI
				=> kernelBuilder.WithOpenAITextEmbeddingGenerationService(options.Models.Embedding, options.Key),
			_
				=> throw new ArgumentException($"Invalid {nameof(options.Type)} value in '{AIServiceOptions.PropertyName}' settings."),
		};
	}

	/// <summary>
	/// Construct IEmbeddingGeneration from <see cref="AIServiceOptions"/>
	/// </summary>
	/// <param name="options">The service configuration</param>
	/// <param name="httpClient">Custom <see cref="HttpClient"/> for HTTP requests.</param>
	/// <param name="logger">Application logger</param>
	private static ITextEmbeddingGeneration ToTextEmbeddingsService(this AIServiceOptions options,
		HttpClient? httpClient = null,
		ILogger? logger = null)
	{
		return options.Type switch
		{
			AIServiceTypeEnum.AzureOpenAI
				=> new AzureTextEmbeddingGeneration(options.Models.Embedding, options.Endpoint, options.Key, httpClient: httpClient, logger: logger),
			AIServiceTypeEnum.OpenAI
				=> new OpenAITextEmbeddingGeneration(options.Models.Embedding, options.Key, httpClient: httpClient, logger: logger),
			_
				=> throw new ArgumentException($"Invalid {nameof(options.Type)} value in '{AIServiceOptions.PropertyName}' settings."),
		};
	}

	/// <summary>
	/// Add the completion backend to the kernel config for the planner.
	/// </summary>
	private static KernelBuilder WithPlannerBackend(this KernelBuilder kernelBuilder, AIServiceOptions options)
	{
		return options.Type switch
		{
			AIServiceTypeEnum.AzureOpenAI => kernelBuilder.WithAzureChatCompletionService(options.Models.Planner, options.Endpoint, options.Key),
			AIServiceTypeEnum.OpenAI => kernelBuilder.WithOpenAIChatCompletionService(options.Models.Planner, options.Key),
			_ => throw new ArgumentException($"Invalid {nameof(options.Type)} value in '{AIServiceOptions.PropertyName}' settings."),
		};
	}

}