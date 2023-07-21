// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json;

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Storage;

/// <summary>
/// A storage context that stores entities on disk.
/// </summary>
public class FileSystemContext<T> : IStorageContext<T> where T : IStoredEntity
{
	/// <summary>
	/// Initializes a new instance of the OnDiskContext class and load the entities from disk.
	/// </summary>
	/// <param name="filePath">The file path to store and read entities on disk.</param>
	public FileSystemContext(FileInfo filePath)
	{
		_fileStorage = filePath;

		_entities = Load(_fileStorage);
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

		if (_entities.TryAdd(entity.Id, entity))
		{
			Save(_entities, _fileStorage);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task DeleteAsync(T entity)
	{
		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			throw new ArgumentOutOfRangeException(nameof(entity.Id), "Entity Id cannot be null or empty.");
		}

		if (_entities.TryRemove(entity.Id, out _))
		{
			Save(_entities, _fileStorage);
		}

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

		return Task.FromException<T>(new KeyNotFoundException($"Entity with id {entityId} not found."));
	}

	/// <inheritdoc/>
	public Task UpsertAsync(T entity)
	{
		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			throw new ArgumentOutOfRangeException(nameof(entity.Id), "Entity Id cannot be null or empty.");
		}

		if (_entities.AddOrUpdate(entity.Id, entity, (key, oldValue) => entity) != null)
		{
			Save(_entities, _fileStorage);
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// A concurrent dictionary to store entities in memory.
	/// </summary>
	private sealed class EntityDictionary : ConcurrentDictionary<string, T>
	{
	}

	/// <summary>
	/// Using a concurrent dictionary to store entities in memory.
	/// </summary>
	private readonly EntityDictionary _entities;

	/// <summary>
	/// The file path to store entities on disk.
	/// </summary>
	private readonly FileInfo _fileStorage;

	/// <summary>
	/// A lock object to prevent concurrent access to the file storage.
	/// </summary>
	private readonly object _fileStorageLock = new();

	/// <summary>
	/// Save the state of the entities to disk.
	/// </summary>
	private void Save(EntityDictionary entities, FileInfo fileInfo)
	{
		lock (_fileStorageLock)
		{
			if (!fileInfo.Exists)
			{
				fileInfo.Directory!.Create();
				File.WriteAllText(fileInfo.FullName, "{}");
			}

			using var fileStream = File.Open(
				path: fileInfo.FullName,
				mode: FileMode.OpenOrCreate,
				access: FileAccess.Write,
				share: FileShare.Read);

			JsonSerializer.Serialize(fileStream, entities);
		}
	}

	/// <summary>
	/// Load the state of entities from disk.
	/// </summary>
	private EntityDictionary Load(FileInfo fileInfo)
	{
		lock (_fileStorageLock)
		{
			if (!fileInfo.Exists)
			{
				fileInfo.Directory!.Create();
				File.WriteAllText(fileInfo.FullName, "{}");
			}

			using var fileStream = File.Open(
				path: fileInfo.FullName,
				mode: FileMode.OpenOrCreate,
				access: FileAccess.Read,
				share: FileShare.Read);

			return JsonSerializer.Deserialize<EntityDictionary>(fileStream) ?? new EntityDictionary();
		}
	}
}
