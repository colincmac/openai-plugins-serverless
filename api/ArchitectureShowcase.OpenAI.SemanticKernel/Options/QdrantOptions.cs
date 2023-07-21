using System.ComponentModel.DataAnnotations;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Options;
public class QdrantOptions
{
	/// <summary>
	/// Gets or sets the endpoint protocol and host (e.g. http://localhost).
	/// </summary>
	[Required, Url]
	public Uri Host { get; set; } = new Uri("http://localhost");

	/// <summary>
	/// Gets or sets the endpoint port.
	/// </summary>
	[Range(0, 65535)]
	public int Port { get; set; }

	/// <summary>
	/// Gets or sets the vector size.
	/// </summary>
	[Range(1, int.MaxValue)]
	public int VectorSize { get; set; }

	/// <summary>
	/// Gets or sets the Qdrant Cloud "api-key" header value.
	/// </summary>
	public string Key { get; set; } = string.Empty;
}