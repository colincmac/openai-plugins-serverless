using Microsoft.DurableTask;

namespace ArchitectureShowcase.OpenAI.HttpSurface.Plugins.CapitalMarkets;
public class DurableFunctions
{
	public DurableFunctions()
	{

	}
	[Function(nameof(HelloCities))]
	public static async Task<string> HelloCities([OrchestrationTrigger] TaskOrchestrationContext context)
	{
		var result = "";
		result += await context.CallActivityAsync<string>(nameof(SayHello), "Tokyo") + " ";
		result += await context.CallActivityAsync<string>(nameof(SayHello), "London") + " ";
		result += await context.CallActivityAsync<string>(nameof(SayHello), "Seattle");
		return result;
	}

	[Function(nameof(SayHello))]
	public static string SayHello([ActivityTrigger] string cityName, FunctionContext executionContext)
	{
		var logger = executionContext.GetLogger(nameof(SayHello));
		logger.LogInformation("Saying hello to {name}", cityName);
		return $"Hello, {cityName}!";
	}

}
