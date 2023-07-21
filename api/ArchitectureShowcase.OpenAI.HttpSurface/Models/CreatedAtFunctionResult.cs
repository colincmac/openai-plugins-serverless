using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace ArchitectureShowcase.OpenAI.HttpSurface.Models;
public class CreatedAtFunctionResult : ObjectResult
{
	private readonly string _location;
	private readonly string _id;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatedObjectResult"/> class.
	/// </summary>
	/// <param name="location">Route location.</param>
	/// <param name="id">Id of the resource.</param>
	/// <param name="value">Object to return.</param>
	public CreatedAtFunctionResult(string location, string id, object value)
		: base(value)
	{
		_location = location;
		_id = id;
	}

	/// <inheritdoc/>
	public override Task ExecuteResultAsync(ActionContext context)
	{
		context.HttpContext.Response.StatusCode = (int)HttpStatusCode.Created;
		var uri = new UriBuilder(context.HttpContext.Request.Scheme, context.HttpContext.Request.Host.Host)
		{
			Path = $"{_location}/{_id}",
		};
		if (context.HttpContext.Request.Host.Port.HasValue)
		{
			uri.Port = context.HttpContext.Request.Host.Port.Value;
		}

		context.HttpContext.Response.Headers.Add("Location", uri.ToString());

		return base.ExecuteResultAsync(context);
	}
}