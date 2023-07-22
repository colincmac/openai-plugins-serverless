// Copyright (c) Microsoft. All rights reserved.

using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Options;
using ArchitectureShowcase.OpenAI.SemanticKernel.Plugins.GithubOpenApi.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.SkillDefinition;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Plugins.SemanticChat;

/// <summary>
/// skill provides the functions to acquire external information.
/// </summary>
public class ExternalInformationFunctions
{
	/// <summary>
	/// Prompt settings.
	/// </summary>
	private readonly PromptsOptions _promptOptions;

	/// <summary>
	/// CopilotChat's planner to gather additional information for the chat context.
	/// </summary>
	private readonly ChatPlanner _planner;

	/// <summary>
	/// Proposed plan to return for approval.
	/// </summary>
	public ProposedPlan? ProposedPlan { get; private set; }

	/// <summary>
	/// Preamble to add to the related information text.
	/// </summary>
	private const string PromptPreamble = "[RELATED START]";

	/// <summary>
	/// Postamble to add to the related information text.
	/// </summary>
	private const string PromptPostamble = "[RELATED END]";

	/// <summary>
	/// Create a new instance of ExternalInformationSkill.
	/// </summary>
	public ExternalInformationFunctions(
		IOptions<PromptsOptions> promptOptions,
		ChatPlanner planner)
	{
		_promptOptions = promptOptions.Value;
		_planner = planner;
	}

	/// <summary>
	/// Extract relevant additional knowledge using a planner.
	/// </summary>
	[SKFunction, Description("Acquire external information")]
	[SKParameter("tokenLimit", "Maximum number of tokens")]
	[SKParameter("proposedPlan", "Previously proposed plan that is approved")]
	public async Task<string> AcquireExternalInformationAsync(
		[Description("The intent to whether external information is needed")] string userIntent,
		SKContext context)
	{
		var functions = _planner.Kernel.Skills.GetFunctionsView(true, true);
		if (functions.NativeFunctions.IsEmpty && functions.SemanticFunctions.IsEmpty)
		{
			return string.Empty;
		}

		// Check if plan exists in ask's context variables.
		var planExists = context.Variables.TryGetValue(SemanticContextConstants.ProposedPlanKey, out var proposedPlanJson);
		var deserializedPlan = planExists && !string.IsNullOrWhiteSpace(proposedPlanJson) ? JsonSerializer.Deserialize<ProposedPlan>(proposedPlanJson) : null;

		// Run plan if it was approved
		if (deserializedPlan != null && deserializedPlan.State == PlanStateEnum.Approved)
		{
			var planJson = JsonSerializer.Serialize(deserializedPlan.Plan);
			// Reload the plan with the planner's kernel so
			// it has full context to be executed
			var newPlanContext = new SKContext(
				null,
				_planner.Kernel.Memory,
				_planner.Kernel.Skills,
				_planner.Kernel.Log
			);
			var plan = Plan.FromJson(planJson, newPlanContext);

			// Invoke plan
			newPlanContext = await plan.InvokeAsync(newPlanContext);
			var tokenLimit =
				int.Parse(context[SemanticContextConstants.TokenLimitKey], new NumberFormatInfo()) -
				PluginUtilities.TokenCount(PromptPreamble) -
				PluginUtilities.TokenCount(PromptPostamble);

			// The result of the plan may be from an OpenAPI skill. Attempt to extract JSON from the response.
			var extractJsonFromOpenApi =
				TryExtractJsonFromOpenApiPlanResult(newPlanContext, newPlanContext.Result, out var planResult);
			if (extractJsonFromOpenApi)
			{
				planResult = OptimizeOpenApiSkillJson(planResult, tokenLimit, plan);
			}
			else
			{
				// If not, use result of the plan execution result directly.
				planResult = newPlanContext.Variables.Input;
			}

			return $"{PromptPreamble}\n{planResult.Trim()}\n{PromptPostamble}\n";
		}
		else
		{
			// Create a plan and set it in context for approval.
			var contextString = string.Join("\n", context.Variables.Where(v => v.Key != SemanticContextConstants.UserIntentKey).Select(v => $"{v.Key}: {v.Value}"));
			var plan = await _planner.CreatePlanAsync($"Given the following context, accomplish the user intent.\nContext:{contextString}\nUser Intent:{userIntent}");

			if (plan.Steps.Count > 0)
			{
				// Parameters stored in plan's top level
				MergeContextIntoPlan(context.Variables, plan.Parameters);

				// TODO: Improve Kernel to give developers option to skip override 
				// (i.e., keep functions regardless of whether they're available in the planner's context or not)
				var sanitizedPlan = SanitizePlan(plan, context);
				sanitizedPlan.Parameters.Update(plan.Parameters);

				ProposedPlan = new ProposedPlan(sanitizedPlan, _planner.PlannerOptions.Type, PlanStateEnum.NoOp);
			}
		}

		return string.Empty;
	}

	#region Private

	/// <summary>
	/// Scrubs plan of functions not available in Planner's kernel.
	/// </summary>
	private Plan SanitizePlan(Plan plan, SKContext context)
	{
		var authHeaders = context.Variables.Where(x => x.Key.StartsWith(SemanticContextConstants.PluginAuthHeaderPrefix)).ToList();
		List<Plan> sanitizedSteps = new();
		var availableFunctions = _planner.Kernel.Skills.GetFunctionsView(true);

		foreach (var step in plan.Steps)
		{
			if (_planner.Kernel.Skills.TryGetFunction(step.SkillName, step.Name, out var function))
			{
				MergeContextIntoPlan(context.Variables, step.Parameters);
				sanitizedSteps.Add(step);
			}
		}

		return new Plan(plan.Description, sanitizedSteps.ToArray<Plan>());
	}

	/// <summary>
	/// Merge any variables from the context into plan parameters as these will be used on plan execution.
	/// These context variables come from user input, so they are prioritized.
	/// </summary>
	private void MergeContextIntoPlan(ContextVariables variables, ContextVariables planParams)
	{
		foreach (var param in planParams)
		{
			if (param.Key.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (variables.TryGetValue(param.Key, out var value))
			{
				planParams.Set(param.Key, value);
			}
		}
	}

	/// <summary>
	/// Try to extract json from the planner response as if it were from an OpenAPI skill.
	/// </summary>
	private bool TryExtractJsonFromOpenApiPlanResult(SKContext context, string openApiSkillResponse, out string json)
	{
		try
		{
			var jsonNode = JsonNode.Parse(openApiSkillResponse);
			var contentType = jsonNode?["contentType"]?.ToString() ?? string.Empty;
			if (contentType.StartsWith("application/json", StringComparison.InvariantCultureIgnoreCase))
			{
				var content = jsonNode?["content"]?.ToString() ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(content))
				{
					json = content;
					return true;
				}
			}
		}
		catch (JsonException)
		{
			context.Log.LogDebug("Unable to extract JSON from planner response, it is likely not from an OpenAPI skill.");
		}
		catch (InvalidOperationException)
		{
			context.Log.LogDebug("Unable to extract JSON from planner response, it may already be proper JSON.");
		}

		json = string.Empty;
		return false;
	}

	/// <summary>
	/// Try to optimize json from the planner response
	/// based on token limit
	/// </summary>
	private string OptimizeOpenApiSkillJson(string jsonContent, int tokenLimit, Plan plan)
	{
		// Remove all new line characters + leading and trailing white space
		jsonContent = Regex.Replace(jsonContent.Trim(), @"[\n\r]", string.Empty);
		var document = JsonDocument.Parse(jsonContent);
		var lastSkillInvoked = plan.Steps[^1].SkillName;
		var lastSkillFunctionInvoked = plan.Steps[^1].Name;
		var trimSkillResponse = false;

		// The json will be deserialized based on the response type of the particular operation that was last invoked by the planner
		// The response type can be a custom trimmed down json structure, which is useful in staying within the token limit
		var skillResponseType = GetOpenApiSkillResponseType(ref document, ref lastSkillInvoked, ref trimSkillResponse);

		if (trimSkillResponse)
		{
			// Deserializing limits the json content to only the fields defined in the respective OpenApiSkill's Model classes
			var skillResponse = JsonSerializer.Deserialize(jsonContent, skillResponseType);
			jsonContent = skillResponse != null ? JsonSerializer.Serialize(skillResponse) : string.Empty;
			document = JsonDocument.Parse(jsonContent);
		}

		var jsonContentTokenCount = PluginUtilities.TokenCount(jsonContent);

		// Return the JSON content if it does not exceed the token limit
		if (jsonContentTokenCount < tokenLimit)
		{
			return jsonContent;
		}

		List<object> itemList = new();

		// Some APIs will return a JSON response with one property key representing an embedded answer.
		// Extract value for further processing
		var resultsDescriptor = "";

		if (document.RootElement.ValueKind == JsonValueKind.Object)
		{
			var propertyCount = 0;
			foreach (var property in document.RootElement.EnumerateObject())
			{
				propertyCount++;
			}

			if (propertyCount == 1)
			{
				// Save property name for result interpolation
				var firstProperty = document.RootElement.EnumerateObject().First();
				tokenLimit -= PluginUtilities.TokenCount(firstProperty.Name);
				resultsDescriptor = string.Format(CultureInfo.InvariantCulture, "{0}: ", firstProperty.Name);

				// Extract object to be truncated
				var value = firstProperty.Value;
				document = JsonDocument.Parse(value.GetRawText());
			}
		}

		// Detail Object
		// To stay within token limits, attempt to truncate the list of properties
		if (document.RootElement.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in document.RootElement.EnumerateObject())
			{
				var propertyTokenCount = PluginUtilities.TokenCount(property.ToString());

				if (tokenLimit - propertyTokenCount > 0)
				{
					itemList.Add(property);
					tokenLimit -= propertyTokenCount;
				}
				else
				{
					break;
				}
			}
		}

		// Summary (List) Object
		// To stay within token limits, attempt to truncate the list of results
		if (document.RootElement.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in document.RootElement.EnumerateArray())
			{
				var itemTokenCount = PluginUtilities.TokenCount(item.ToString());

				if (tokenLimit - itemTokenCount > 0)
				{
					itemList.Add(item);
					tokenLimit -= itemTokenCount;
				}
				else
				{
					break;
				}
			}
		}

		return itemList.Count > 0
			? string.Format(CultureInfo.InvariantCulture, "{0}{1}", resultsDescriptor, JsonSerializer.Serialize(itemList))
			: string.Format(CultureInfo.InvariantCulture, "JSON response for {0} is too large to be consumed at time.", lastSkillInvoked);
	}

	private Type GetOpenApiSkillResponseType(ref JsonDocument document, ref string lastSkillInvoked, ref bool trimSkillResponse)
	{
		var skillResponseType = typeof(object); // Use a reasonable default response type

		// Different operations under the skill will return responses as json structures;
		// Prune each operation response according to the most important/contextual fields only to avoid going over the token limit
		// Check what the last skill invoked was and deserialize the JSON content accordingly
		if (string.Equals(lastSkillInvoked, "GitHubSkill", StringComparison.Ordinal))
		{
			trimSkillResponse = true;
			skillResponseType = GetGithubSkillResponseType(ref document);
		}

		return skillResponseType;
	}

	private Type GetGithubSkillResponseType(ref JsonDocument document)
	{
		return document.RootElement.ValueKind == JsonValueKind.Array ? typeof(PullRequest[]) : typeof(PullRequest);
	}


	#endregion
}
