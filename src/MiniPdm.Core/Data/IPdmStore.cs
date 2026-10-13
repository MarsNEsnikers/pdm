using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Data;

/// <summary>Операции над хранилищем. Не знает о конкретной СУБД.</summary>
public interface IPdmStore
{
    Task<ObjectRecord?> FindByDesignationAsync(string designation, CancellationToken ct);
    Task<ObjectRecord?> FindStandardByNameAsync(string name, CancellationToken ct);
    Task<ObjectRecord?> GetObjectAsync(long id, CancellationToken ct);
    Task<long> InsertObjectAsync(ObjectType type, string? designation, string name, CancellationToken ct);
    Task UpdateObjectNameAsync(long objectId, string name, CancellationToken ct);

    Task<IReadOnlyList<VersionRecord>> GetVersionsAsync(long objectId, CancellationToken ct);
    Task<long> InsertVersionAsync(long objectId, int versionNo, VersionState state, string? material,
        decimal? massKg, DateTime createdAt, CancellationToken ct);
    Task UpdateVersionDataAsync(long versionId, string? material, decimal? massKg, CancellationToken ct);
    Task SetVersionStateAsync(long versionId, VersionState state, CancellationToken ct);
    Task SetCurrentVersionAsync(long objectId, long? versionId, CancellationToken ct);

    Task<IReadOnlyList<LinkRecord>> GetLinksAsync(long versionId, CancellationToken ct);
    Task ReplaceLinksAsync(long versionId, IEnumerable<LinkRecord> links, CancellationToken ct);

    /// <summary>Дерево состава одним рекурсивным запросом (CTE).</summary>
    Task<TreeNode?> LoadTreeAsync(long rootObjectId, CancellationToken ct);
    Task<IReadOnlyList<ObjectSummary>> SearchAsync(string? text, CancellationToken ct);

    Task AddImportLogAsync(DateTime startedAt, string fileName, string severity, string reason, CancellationToken ct);
}

public interface IPdmSession : IPdmStore, IAsyncDisposable
{
    /// <summary>Фиксирует транзакцию. Без вызова Commit при Dispose выполняется откат.</summary>
    Task CommitAsync(CancellationToken ct);
}

public interface IPdmStoreFactory
{
    Task<IPdmSession> OpenAsync(bool transactional, CancellationToken ct);
}
