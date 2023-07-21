namespace ArchitectureShowcase.OpenAI.SemanticKernel.Models;
public enum AIMemoryStoreTypeEnum
{
	/// <summary>
	/// Non-persistent memories store.
	/// </summary>
	Volatile,

	/// <summary>
	/// Qdrant based persistent memories store.
	/// </summary>
	Qdrant,

	/// <summary>
	/// Azure Cognitive Search persistent memories store.
	/// </summary>
	AzureCognitiveSearch
}
