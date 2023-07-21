using System.ComponentModel.DataAnnotations;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Extensions;
internal class ServiceOptions
{
	public const string PropertyName = "Service";

	/// <summary>
	/// Configuration Key Vault URI
	/// </summary>
	[Url]
	public Uri? KeyVaultUri { get; set; }

	/// <summary>
	/// Local directory in which to load semantic skills.
	/// </summary>
	public string? SemanticSkillsDirectory { get; set; }
}
