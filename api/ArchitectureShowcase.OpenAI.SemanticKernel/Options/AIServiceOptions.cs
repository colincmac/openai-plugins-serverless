using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Options;
public class AIServiceOptions
{
	public const string PropertyName = "AIService";

	public string Endpoint { get; set; } = string.Empty;

	public string Key { get; set; } = string.Empty;

	public AIModelType Models { get; set; } = new AIModelType();

	[JsonConverter(typeof(StringEnumConverter))]
	public AIServiceTypeEnum Type { get; set; } = AIServiceTypeEnum.AzureOpenAI;

}

