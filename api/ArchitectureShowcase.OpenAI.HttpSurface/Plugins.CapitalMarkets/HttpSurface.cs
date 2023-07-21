using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask.Client;

namespace ArchitectureShowcase.OpenAI.HttpSurface.Plugins.CapitalMarkets;
public class HttpSurface
{
	public HttpSurface()
	{

	}

	[Function(nameof(StartHelloCities))]
	public static async Task<IActionResult> StartHelloCities(
		[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req,
		[DurableClient] DurableTaskClient client,
		FunctionContext executionContext)
	{
		var logger = executionContext.GetLogger(nameof(StartHelloCities));
		var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(DurableFunctions.HelloCities));
		logger.LogInformation("Created new orchestration with instance ID = {instanceId}", instanceId);

		return new OkResult();
	}

}
