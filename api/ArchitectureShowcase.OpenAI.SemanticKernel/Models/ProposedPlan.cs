using Microsoft.SemanticKernel.Planning;
using System.Text.Json.Serialization;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Models;
/// <summary>
/// Information about a single proposed plan.
/// </summary>
public class ProposedPlan
{
	/// <summary>
	/// Plan object to be approved or invoked.
	/// </summary>
	[JsonPropertyName("proposedPlan")]
	public Plan Plan { get; set; }

	/// <summary>
	/// Indicates whether plan is Action (single-step) or Sequential (multi-step).
	/// </summary>
	[JsonPropertyName("type")]
	public PlanTypeEnum Type { get; set; }

	/// <summary>
	/// State of plan
	/// </summary>
	[JsonPropertyName("state")]
	public PlanStateEnum State { get; set; }

	/// <summary>
	/// Create a new proposed plan.
	/// </summary>
	/// <param name="plan">Proposed plan object</param>
	public ProposedPlan(Plan plan, PlanTypeEnum type, PlanStateEnum state)
	{
		this.Plan = plan;
		this.Type = type;
		this.State = state;
	}
}