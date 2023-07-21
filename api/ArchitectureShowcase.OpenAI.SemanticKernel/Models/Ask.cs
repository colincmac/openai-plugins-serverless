﻿namespace ArchitectureShowcase.OpenAI.SemanticKernel.Models;
public class Ask
{
	public string Input { get; set; } = string.Empty;

	public IEnumerable<KeyValuePair<string, string>> Variables { get; set; } = Enumerable.Empty<KeyValuePair<string, string>>();
}