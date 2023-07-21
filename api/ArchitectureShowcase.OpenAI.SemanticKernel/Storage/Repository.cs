// Copyright (c) Microsoft. All rights reserved.

namespace ArchitectureShowcase.OpenAI.SemanticKernel.Storage;

/// <summary>
/// Defines the basic CRUD operations for a repository.
/// </summary>
public class Repository<TEntity> : IRepository<TEntity> where TEntity : IStoredEntity
{
	/// <summary>
	/// The storage context.
	/// </summary>
	protected IStorageContext<TEntity> StorageContext { get; set; }

	/// <summary>
	/// Initializes a new instance of the Repository class.
	/// </summary>
	public Repository(IStorageContext<TEntity> storageContext)
	{
		StorageContext = storageContext;
	}

	/// <inheritdoc/>
	public Task CreateAsync(TEntity entity)
	{
		if (string.IsNullOrWhiteSpace(entity.Id))
		{
			throw new ArgumentOutOfRangeException(nameof(entity.Id), "Entity Id cannot be null or empty.");
		}

		return StorageContext.CreateAsync(entity);
	}

	/// <inheritdoc/>
	public Task DeleteAsync(TEntity entity)
	{
		return StorageContext.DeleteAsync(entity);
	}

	/// <inheritdoc/>
	public Task<TEntity> FindByIdAsync(string id)
	{
		return StorageContext.ReadAsync(id);
	}

	public async Task<bool> TryFindByIdAsync(string id, Action<TEntity?> entity)
	{
		try
		{
			entity(await this.FindByIdAsync(id));
			return true;
		}
		catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is KeyNotFoundException)
		{
			entity(default);
			return false;
		}
	}


	/// <inheritdoc/>
	public Task UpsertAsync(TEntity entity)
	{
		return StorageContext.UpsertAsync(entity);
	}
}
