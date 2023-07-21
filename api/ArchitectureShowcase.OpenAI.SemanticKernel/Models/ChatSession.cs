// Copyright (c) Microsoft. All rights reserved.

using ArchitectureShowcase.OpenAI.SemanticKernel.Storage;
using System.Text.Json.Serialization;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Models;

/// <summary>
/// A chat session
/// </summary>
public class ChatSession : IStoredEntity
{
	/// <summary>
	/// Chat ID that is persistent and unique.
	/// </summary>
	[JsonPropertyName("id")]
	public string Id { get; set; }

	/// <summary>
	/// User ID that is persistent and unique.
	/// </summary>
	[JsonPropertyName("userId")]
	public string UserId { get; set; }

	/// <summary>
	/// Title of the chat.
	/// </summary>
	[JsonPropertyName("title")]
	public string Title { get; set; }

	/// <summary>
	/// Timestamp of the chat creation.
	/// </summary>
	[JsonPropertyName("createdOn")]
	public DateTimeOffset CreatedOn { get; set; }

	public ChatSession(string userId, string title)
	{
		Id = Guid.NewGuid().ToString();
		UserId = userId;
		Title = title;
		CreatedOn = DateTimeOffset.Now;
	}
}
