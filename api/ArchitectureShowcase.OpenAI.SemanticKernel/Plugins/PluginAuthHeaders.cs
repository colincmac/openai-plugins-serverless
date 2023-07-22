using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Plugins;
public class PluginAuthHeaders
{
	public static PluginAuthHeaders FromHeaderDictionary(IHeaderDictionary headers)
	{
		headers.TryGetValue("x-sk-copilot-graph-auth", out var graph);
		headers.TryGetValue("x-sk-copilot-jira-auth", out var jira);
		headers.TryGetValue("x-sk-copilot-github-auth", out var github);
		headers.TryGetValue("x-sk-copilot-klarna-auth", out var klarna);

		return new PluginAuthHeaders
		{
			GraphAuthentication = graph,
			JiraAuthentication = jira,
			GithubAuthentication = github,
			KlarnaAuthentication = klarna
		};
	}


	/// <summary>
	/// Gets or sets the MS Graph authentication header value.
	/// </summary>
	[FromHeader(Name = "x-sk-copilot-graph-auth")]
	public string? GraphAuthentication { get; set; }

	/// <summary>
	/// Gets or sets the Jira authentication header value.
	/// </summary>
	[FromHeader(Name = "x-sk-copilot-jira-auth")]
	public string? JiraAuthentication { get; set; }

	/// <summary>
	/// Gets or sets the GitHub authentication header value.
	/// </summary>
	[FromHeader(Name = "x-sk-copilot-github-auth")]
	public string? GithubAuthentication { get; set; }

	/// <summary>
	/// Gets or sets the Klarna header value.
	/// </summary>
	[FromHeader(Name = "x-sk-copilot-klarna-auth")]
	public string? KlarnaAuthentication { get; set; }
}
