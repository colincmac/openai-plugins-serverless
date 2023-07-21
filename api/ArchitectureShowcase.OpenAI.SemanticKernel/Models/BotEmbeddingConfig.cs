// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Models;

/// <summary>
/// The embedding configuration of a bot. Used in the Bot object for portability.
/// </summary>
public class BotEmbeddingConfig
{
	/// <summary>
	/// The AI service.
	/// </summary>
	[Required]
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public AIServiceTypeEnum AIService { get; set; } = AIServiceTypeEnum.AzureOpenAI;

	/// <summary>
	/// The deployment or the model id.
	/// </summary>
	public string DeploymentOrModelId { get; set; } = string.Empty;
}
