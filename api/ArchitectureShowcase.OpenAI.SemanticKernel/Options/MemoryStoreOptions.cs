namespace ArchitectureShowcase.OpenAI.SemanticKernel.Options;
internal class MemoryStoreOptions
{
	public const string PropertyName = "AIMemoryStore";

	/// <summary>
	/// The type of memories store to use.
	/// </summary>
	public enum MemoryStoreType
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

	/// <summary>
	/// Gets or sets the type of memories store to use.
	/// </summary>
	public MemoryStoreType Type { get; set; } = MemoryStoreType.Volatile;

	/// <summary>
	/// Gets or sets the configuration for the Qdrant memories store.
	/// </summary>
	[RequiredOnPropertyValue(nameof(Type), MemoryStoreType.Qdrant)]
	public QdrantOptions? Qdrant { get; set; }

	/// <summary>
	/// Gets or sets the configuration for the Azure Cognitive Search memories store.
	/// </summary>
	[RequiredOnPropertyValue(nameof(Type), MemoryStoreType.AzureCognitiveSearch)]
	public AzureCognitiveSearchOptions? AzureCognitiveSearch { get; set; }
}
