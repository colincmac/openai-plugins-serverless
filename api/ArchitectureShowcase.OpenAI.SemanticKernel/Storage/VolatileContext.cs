// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Storage;

/// <summary>
/// A storage context that stores entities in memory.
/// </summary>
[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
public class VolatileContext<T> : IStorageContext<T> where T : IStoredEntity
{
	/// <summary>
	/// Using a concurrent dictionary to store entities in memory.
	/// </summary>
	private readonly ConcurrentDictionary<string, T> _entities;

	/// <summary>
	/// Initializes a new instance of the InMemoryContext class.
	/// </summary>
	public VolatileContext()
	{
		_entities = new ConcurrentDictionary<string, T>();
	}

	/// <inheritdoc/>
	public Task<IEnumerable<T>> QueryEntitiesAsync(Func<T, bool> predicate)
	{
		return Task.FromResult(_entities.Values.Where(predicate));
	}

	/// <inheritdoc/>
	public Task CreateAsync(T entity)
	{
		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			throw new ArgumentOutOfRangeException(nameof(entity.Id), "Entity Id cannot be null or empty.");
		}

		_entities.TryAdd(entity.Id, entity);

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task DeleteAsync(T entity)
	{
		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			throw new ArgumentOutOfRangeException(nameof(entity.Id), "Entity Id cannot be null or empty.");
		}

		_entities.TryRemove(entity.Id, out _);

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task<T> ReadAsync(string entityId)
	{
		if (string.IsNullOrWhiteSpace(entityId))
		{
			throw new ArgumentOutOfRangeException(nameof(entityId), "Entity Id cannot be null or empty.");
		}

		if (_entities.TryGetValue(entityId, out var entity))
		{
			return Task.FromResult(entity);
		}

		throw new KeyNotFoundException($"Entity with id {entityId} not found.");
	}

	/// <inheritdoc/>
	public Task UpsertAsync(T entity)
	{
		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			throw new ArgumentOutOfRangeException(nameof(entity.Id), "Entity Id cannot be null or empty.");
		}

		_entities.AddOrUpdate(entity.Id, entity, (key, oldValue) => entity);

		return Task.CompletedTask;
	}

	private string GetDebuggerDisplay()
	{
		return ToString() ?? string.Empty;
	}
}
