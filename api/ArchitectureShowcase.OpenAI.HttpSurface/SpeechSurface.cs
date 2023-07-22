using ArchitectureShowcase.OpenAI.HttpSurface.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Models;
using ArchitectureShowcase.OpenAI.SemanticKernel.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using System.Net;

namespace ArchitectureShowcase.OpenAI.HttpSurface;
public class SpeechSurface
{
	private readonly AzureSpeechOptions _azureSpeechOptions;
	private readonly IHttpClientFactory _httpClientFactory;

	public SpeechSurface(IOptions<AzureSpeechOptions> azureSpeechOptions, IHttpClientFactory httpClientFactory)
	{
		_azureSpeechOptions = azureSpeechOptions.Value;
		_httpClientFactory = httpClientFactory;
	}

	/// <summary>
	/// Get an authorization token and region for the Azure Speech service.
	/// </summary>
	/// <param name="req">The HTTP request.</param>
	/// <returns>The HTTP response.</returns>
	[Function("GetSpeechAuthorizationToken")]
	[OpenApiOperation(operationId: "getSpeechAuthorizationToken", tags: new[] { "chatSession" }, Summary = "Create a new chat session and populate the session with the initial bot message.", Description = "This creates a new chat session and populates the session with the initial bot message.", Visibility = OpenApiVisibilityType.Important)]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ProblemDetails), Summary = "Invalid request")]
	[OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(ProblemDetails), Summary = "Resource not found")]
	[OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ChatSession), Required = true, Description = "The chat session parameters")]
	public async Task<IActionResult> GetSpeechAuthorizationToken(
		[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "speechToken")] HttpRequest req,
		FunctionContext executionContext)
	{
		var log = executionContext.GetLogger<SpeechSurface>();
		var (isAuthenticated, authenticationResponse) =
		await req.HttpContext.AuthenticateAzureFunctionAsync();
		var userId = req.HttpContext.User.GetObjectId();

		if (!isAuthenticated || string.IsNullOrEmpty(userId))
			return authenticationResponse ?? new UnauthorizedResult();

		// Azure Speech token support is optional. If the configuration is missing or incomplete, return an unsuccessful token response.
		if (string.IsNullOrWhiteSpace(_azureSpeechOptions.Region) ||
			string.IsNullOrWhiteSpace(_azureSpeechOptions.Key))
		{
			return new OkObjectResult(new SpeechTokenResponse { IsSuccess = false });
		}
		var client = _httpClientFactory.CreateClient();
		var request = _azureSpeechOptions.GetSpeechTokenRequest();
		var result = await client.SendAsync(request);

		return new OkObjectResult(new SpeechTokenResult
		{
			IsSuccess = result.IsSuccessStatusCode,
			Region = _azureSpeechOptions.Region,
			Token = await result.Content.ReadAsStringAsync()
		});
	}
}
