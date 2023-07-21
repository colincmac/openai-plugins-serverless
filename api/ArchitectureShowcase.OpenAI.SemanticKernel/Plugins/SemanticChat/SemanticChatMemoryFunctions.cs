using ArchitectureShowcase.OpenAI.SemanticKernel.Options;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.SkillDefinition;
using System.ComponentModel;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Plugins.SemanticChat;

/// <summary>
/// This skill provides the functions to query the semantic chat memory.
/// </summary>
public class SemanticChatMemoryFunctions
{
	/// <summary>
	/// Prompt settings.
	/// </summary>
	private readonly PromptsOptions _promptOptions;

	/// <summary>
	/// Create a new instance of SemanticChatMemorySkill.
	/// </summary>
	public SemanticChatMemoryFunctions(
		IOptions<PromptsOptions> promptOptions)
	{
		_promptOptions = promptOptions.Value;
	}


	/// <summary>
	/// Query relevant memories based on the query.
	/// </summary>
	/// <param name="query">Query to match.</param>
	/// <param name="context">The SKContext</param>
	/// <returns>A string containing the relevant memories.</returns>
	[SKFunction, Description("Query chat memories")]
	public async Task<string> QueryMemoriesAsync(
		[Description("Query to match.")] string query,
		[Description("Chat ID to query history from")] string chatId,
		[Description("Maximum number of tokens")] int tokenLimit,
		ISemanticTextMemory textMemory)
	{
		var remainingToken = tokenLimit;

		// Search for relevant memories.
		List<MemoryQueryResult> relevantMemories = new();
		foreach (var memoryName in this._promptOptions.MemoryMap.Keys)
		{
			var results = textMemory.SearchAsync(
				SemanticChatMemoryExtractor.MemoryCollectionName(chatId, memoryName),
				query,
				limit: 100,
				minRelevanceScore: this._promptOptions.SemanticMemoryMinRelevance);
			await foreach (var memory in results)
			{
				relevantMemories.Add(memory);
			}
		}

		relevantMemories = relevantMemories.OrderByDescending(m => m.Relevance).ToList();

		var memoryText = "";
		foreach (var memory in relevantMemories)
		{
			var tokenCount = PluginUtilities.TokenCount(memory.Metadata.Text);
			if (remainingToken - tokenCount > 0)
			{
				memoryText += $"\n[{memory.Metadata.Description}] {memory.Metadata.Text}";
				remainingToken -= tokenCount;
			}
			else
			{
				break;
			}
		}

		if (string.IsNullOrEmpty(memoryText))
		{
			// No relevant memories found
			return string.Empty;
		}

		return $"Past memories (format: [memory type] <label>: <details>):\n{memoryText.Trim()}";
	}
}
