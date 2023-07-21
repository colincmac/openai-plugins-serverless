using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Planning;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Plugins.SemanticChat;
/// <summary>
/// A lightweight wrapper around a planner to allow for curating which skills are available to it.
/// </summary>
public class ChatPlanner
{
	/// <summary>
	/// The planner's kernel.
	/// </summary>
	public IKernel Kernel { get; }

	/// <summary>
	/// Options for the planner.
	/// </summary>
	private readonly PlannerOptions? _plannerOptions;

	/// <summary>
	/// Gets the pptions for the planner.
	/// </summary>
	public PlannerOptions? PlannerOptions => _plannerOptions;

	/// <summary>
	/// Initializes a new instance of the <see cref="ChatPlanner"/> class.
	/// </summary>
	/// <param name="plannerKernel">The planner's kernel.</param>
	public ChatPlanner(IKernel plannerKernel, PlannerOptions? plannerOptions)
	{
		Kernel = plannerKernel;
		_plannerOptions = plannerOptions;
	}

	/// <summary>
	/// Create a plan for a goal.
	/// </summary>
	/// <param name="goal">The goal to create a plan for.</param>
	/// <returns>The plan.</returns>
	public Task<Plan> CreatePlanAsync(string goal)
	{
		var plannerFunctionsView = Kernel.Skills.GetFunctionsView(true, true);
		if (plannerFunctionsView.NativeFunctions.IsEmpty && plannerFunctionsView.SemanticFunctions.IsEmpty)
		{
			// No functions are available - return an empty plan.
			return Task.FromResult(new Plan(goal));
		}

		if (_plannerOptions?.Type == PlanTypeEnum.Sequential)
		{
			return new SequentialPlanner(Kernel).CreatePlanAsync(goal);
		}

		return new ActionPlanner(Kernel).CreatePlanAsync(goal);
	}
}