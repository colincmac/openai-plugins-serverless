using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;

namespace ArchitectureShowcase.OpenAI.HttpSurface;
public class HealthSurface
{
	private const string ChatSkillName = "ChatPlugin";
	private const string ChatFunction = "Chat";
	private readonly IKernel _semanticKernel;

	public HealthSurface(IKernel semanticKernel)
	{
		_semanticKernel = semanticKernel;
	}

	[Function("HealthCheck")]
	public IActionResult HealthCheck(
	[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "healthz")] HttpRequest req)
	{

		return new OkResult();
	}
}
