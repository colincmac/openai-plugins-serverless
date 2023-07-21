namespace ArchitectureShowcase.OpenAI.SemanticKernel.Storage;
public interface IStorageContext<T> where T : IStoredEntity
{
	/// <summary>
	/// Query entities in the storage context.
	/// </summary>
	Task<IEnumerable<T>> QueryEntitiesAsync(Func<T, bool> predicate);

	/// <summary>
	/// Read an entity from the storage context by id.
	/// </summary>
	/// <param name="entityId">The entity id.</param>
	/// <returns>The entity.</returns>
	Task<T> ReadAsync(string entityId);

	/// <summary>
	/// Create an entity in the storage context.
	/// </summary>
	/// <param name="entity">The entity to be created in the context.</param>
	Task CreateAsync(T entity);

	/// <summary>
	/// Upsert an entity in the storage context.
	/// </summary>
	/// <param name="entity">The entity to be upserted in the context.</param>
	Task UpsertAsync(T entity);

	/// <summary>
	/// Delete an entity from the storage context.
	/// </summary>
	/// <param name="entity">The entity to be deleted from the context.</param>
	Task DeleteAsync(T entity);
}
