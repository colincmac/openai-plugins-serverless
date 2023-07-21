using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using System.ComponentModel.DataAnnotations;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Options;
public class PlannerOptions
{
	public const string PropertyName = "Planner";

	/// <summary>
	/// Define if the planner must be Sequential or not.
	/// </summary>
	[Required]
	public PlanTypeEnum Type { get; set; } = PlanTypeEnum.Action;
}
