// Copyright (c) Microsoft. All rights reserved.

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Options;

/// <summary>
/// Configuration options for Azure speech recognition.
/// </summary>
public sealed class AzureSpeechOptions
{
	public const string PropertyName = "AzureSpeech";

	/// <summary>
	/// Location of the Azure speech service to use (e.g. "South Central US")
	/// </summary>
	public string Region { get; set; } = string.Empty;

	/// <summary>
	/// Key to access the Azure speech service.
	/// </summary>
	public string Key { get; set; } = string.Empty;

	private string SpeechTokenEndpoint => $"https://{Region}.api.cognitive.microsoft.com/sts/v1.0/issueToken";
	public HttpRequestMessage GetSpeechTokenRequest()
	{
		var request = new HttpRequestMessage(HttpMethod.Post, SpeechTokenEndpoint);
		request.Headers.Add("Ocp-Apim-Subscription-Key", Key);
		return request;
	}
}
