using ArchitectureShowcase.OpenAI.HttpSurface;
using ArchitectureShowcase.OpenAI.SemanticKernel.Extensions;
using Azure.Core.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using System.Text.Json;

var host = new HostBuilder()
	.ConfigureFunctionsWebApplication()
	.ConfigureServices((host, services) =>
	{
		var configuration = host.Configuration;
		services.AddApplicationInsightsTelemetryWorkerService();
		services.AddServerlessHub<ChatSurface>(builder => builder.WithOptions(opt => opt.UseJsonObjectSerializer(new JsonObjectSerializer(new(JsonSerializerDefaults.Web)))));
		services.AddServerlessHub<ChatHistorySurface>(builder => builder.WithOptions(opt => opt.UseJsonObjectSerializer(new JsonObjectSerializer(new(JsonSerializerDefaults.Web)))));
		services
		.AddAIServiceOptions(configuration)
		.AddCopilotChatOptions(configuration)
		.AddSemanticKernelServices()
		.AddCopilotChatPlannerServices()
		.AddPersistentChatStore();

		if (configuration.GetSection(ApplicationConstants.AadConfigurationSectionName).Exists())
		{
			services
				.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
				.AddMicrosoftIdentityWebApi(options =>
				{
					configuration.Bind(ApplicationConstants.AadConfigurationSectionName, options);
					options.TokenValidationParameters.NameClaimType = "name";
				}, options => configuration.Bind("AzureAd", options));

			services.AddAuthorization();

		}
	})
	.Build();

host.Run();
