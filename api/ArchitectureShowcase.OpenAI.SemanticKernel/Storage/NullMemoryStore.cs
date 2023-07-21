using Microsoft.SemanticKernel.Memory;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Storage;

/// <summary>
/// Wrapper around IMemoryStore to allow for null values.
/// </summary>
public sealed class NullMemoryStore
{
	/// <summary>
	/// Optional memory store.
	/// </summary>
	public IMemoryStore? MemoryStore { get; set; }
}
