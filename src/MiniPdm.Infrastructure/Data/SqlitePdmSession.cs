using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using MiniPdm.Core.Data;
using MiniPdm.Core.Domain;

namespace MiniPdm.Infrastructure.Data;

/// <summary>
/// Весь SQL, зависящий от СУБД, находится в этом классе. Для другой СУБД (PostgreSQL, MS SQL)
/// достаточно написать ещё одну реализацию IPdmSession.
/// </summary>
internal sealed class SqlitePdmSession(SqliteConnection connection, SqliteTransaction? tx) : IPdmSession
{
    private bool _committed;

    // ---- строки результатов (SQLite отдаёт INTEGER как long, REAL как double) ----
    private sealed class ObjectRow
    {
        public long Id { get; set; }
        public string ObjectType { get; set; } = "";
        public string? Designation { get; set; }
        public string Name { get; set; } = "";
        public long? CurrentVersionId { get; set; }
    }

    private sealed class VersionRow
    {
        public long Id { get; set; }
        public long ObjectId { get; set; }
        public long VersionNo { get; set; }
        public string State { get; set; } = "";
        public string? Material { get; set; }
        public double? MassKg { get; set; }
        public string CreatedAt { get; set; } = "";
    }

    private sealed class LinkRow
    {
        public long ChildObjectId { get; set; }
        public long Quantity { get; set; }
    }

    private sealed class SummaryRow
    {
        public long Id { get; set; }
        public string ObjectType { get; set; } = "";
        public string? Designation { get; set; }
        public string Name { get; set; } = "";
        public long? VersionNo { get; set; }
        public string? State { get; set; }
    }

    private sealed class TreeRow
    {
        public string Path { get; set; } = "";
        public string? ParentPath { get; set; }
        public long ObjectId { get; set; }
        public long Quantity { get; set; }
        public long IsCycle { get; set; }
        public string ObjectType { get; set; } = "";
        public string? Designation { get; set; }
        public string Name { get; set; } = "";
        public long? VersionNo { get; set; }
        public string? State { get; set; }
        public string? Material { get; set; }
        public double? MassKg { get; set; }
    }

    private CommandDefinition Cmd(string sql, object? param, CancellationToken ct) =>
        new(sql, param, tx, cancellationToken: ct);

    private static ObjectRecord ToRecord(ObjectRow r) =>
        new(r.Id, Enum.Parse<ObjectType>(r.ObjectType), r.Designation, r.Name, r.CurrentVersionId);

    private static VersionRecord ToRecord(VersionRow r) =>
        new(r.Id, r.ObjectId, (int)r.VersionNo, Enum.Parse<VersionState>(r.State), r.Material,
            r.MassKg is null ? null : (decimal)r.MassKg.Value,
            DateTime.Parse(r.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private const string ObjectColumns =
        "id AS Id, object_type AS ObjectType, designation AS Designation, name AS Name, current_version_id AS CurrentVersionId";

    public async Task ExecuteScriptAsync(string script, CancellationToken ct) =>
        await connection.ExecuteAsync(Cmd(script, null, ct));

    public async Task<ObjectRecord?> FindByDesignationAsync(string designation, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ObjectRow>(
            Cmd($"SELECT {ObjectColumns} FROM pdm_object WHERE designation = @designation", new { designation }, ct));
        return row is null ? null : ToRecord(row);
    }

    public async Task<ObjectRecord?> FindStandardByNameAsync(string name, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ObjectRow>(
            Cmd($"SELECT {ObjectColumns} FROM pdm_object WHERE object_type = 'StandardPart' AND name = @name", new { name }, ct));
        return row is null ? null : ToRecord(row);
    }

    public async Task<ObjectRecord?> GetObjectAsync(long id, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ObjectRow>(
            Cmd($"SELECT {ObjectColumns} FROM pdm_object WHERE id = @id", new { id }, ct));
        return row is null ? null : ToRecord(row);
    }

    public Task<long> InsertObjectAsync(ObjectType type, string? designation, string name, CancellationToken ct) =>
        connection.ExecuteScalarAsync<long>(Cmd(
            "INSERT INTO pdm_object (object_type, designation, name) VALUES (@type, @designation, @name); SELECT last_insert_rowid();",
            new { type = type.ToString(), designation, name }, ct));

    public Task UpdateObjectNameAsync(long objectId, string name, CancellationToken ct) =>
        connection.ExecuteAsync(Cmd("UPDATE pdm_object SET name = @name WHERE id = @objectId", new { objectId, name }, ct));

    public async Task<IReadOnlyList<VersionRecord>> GetVersionsAsync(long objectId, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<VersionRow>(Cmd(
            @"SELECT id AS Id, object_id AS ObjectId, version_no AS VersionNo, state AS State,
                     material AS Material, mass_kg AS MassKg, created_at AS CreatedAt
              FROM object_version WHERE object_id = @objectId ORDER BY version_no", new { objectId }, ct));
        return rows.Select(ToRecord).ToList();
    }

    public Task<long> InsertVersionAsync(long objectId, int versionNo, VersionState state, string? material,
        decimal? massKg, DateTime createdAt, CancellationToken ct) =>
        connection.ExecuteScalarAsync<long>(Cmd(
            @"INSERT INTO object_version (object_id, version_no, state, material, mass_kg, created_at)
              VALUES (@objectId, @versionNo, @state, @material, @mass, @createdAt);
              SELECT last_insert_rowid();",
            new
            {
                objectId, versionNo, state = state.ToString(), material,
                mass = massKg is null ? (double?)null : (double)massKg.Value,
                createdAt = createdAt.ToString("O", CultureInfo.InvariantCulture)
            }, ct));

    public Task UpdateVersionDataAsync(long versionId, string? material, decimal? massKg, CancellationToken ct) =>
        connection.ExecuteAsync(Cmd(
            "UPDATE object_version SET material = @material, mass_kg = @mass WHERE id = @versionId",
            new { versionId, material, mass = massKg is null ? (double?)null : (double)massKg.Value }, ct));

    public Task SetVersionStateAsync(long versionId, VersionState state, CancellationToken ct) =>
        connection.ExecuteAsync(Cmd("UPDATE object_version SET state = @state WHERE id = @versionId",
            new { versionId, state = state.ToString() }, ct));

    public Task SetCurrentVersionAsync(long objectId, long? versionId, CancellationToken ct) =>
        connection.ExecuteAsync(Cmd("UPDATE pdm_object SET current_version_id = @versionId WHERE id = @objectId",
            new { objectId, versionId }, ct));

    public async Task<IReadOnlyList<LinkRecord>> GetLinksAsync(long versionId, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<LinkRow>(Cmd(
            "SELECT child_object_id AS ChildObjectId, quantity AS Quantity FROM bom_link WHERE parent_version_id = @versionId",
            new { versionId }, ct));
        return rows.Select(r => new LinkRecord(r.ChildObjectId, (int)r.Quantity)).ToList();
    }

    public async Task ReplaceLinksAsync(long versionId, IEnumerable<LinkRecord> links, CancellationToken ct)
    {
        await connection.ExecuteAsync(Cmd("DELETE FROM bom_link WHERE parent_version_id = @versionId", new { versionId }, ct));
        foreach (var l in links)
            await connection.ExecuteAsync(Cmd(
                "INSERT INTO bom_link (parent_version_id, child_object_id, quantity) VALUES (@versionId, @child, @qty)",
                new { versionId, child = l.ChildObjectId, qty = l.Quantity }, ct));
    }

    // path  список id предков через запятую («,1,5,9,»): по нему отсекаются циклы и собирается дерево.
    // Узел-повтор предка попадает в результат с is_cycle = 1, но дальше не разворачивается.
    private const string TreeSql = @"
WITH RECURSIVE tree (path, parent_path, object_id, quantity, depth, is_cycle) AS (
    SELECT ',' || o.id || ',', NULL, o.id, 1, 0, 0
    FROM pdm_object o
    WHERE o.id = @root
    UNION ALL
    SELECT t.path || l.child_object_id || ',', t.path, l.child_object_id, l.quantity, t.depth + 1,
           CASE WHEN instr(t.path, ',' || l.child_object_id || ',') > 0 THEN 1 ELSE 0 END
    FROM tree t
    JOIN pdm_object p ON p.id = t.object_id
    JOIN bom_link l   ON l.parent_version_id = p.current_version_id
    WHERE t.is_cycle = 0
)
SELECT t.path AS Path, t.parent_path AS ParentPath, t.object_id AS ObjectId, t.quantity AS Quantity,
       t.is_cycle AS IsCycle, o.object_type AS ObjectType, o.designation AS Designation, o.name AS Name,
       v.version_no AS VersionNo, v.state AS State, v.material AS Material, v.mass_kg AS MassKg
FROM tree t
JOIN pdm_object o ON o.id = t.object_id
LEFT JOIN object_version v ON v.id = o.current_version_id
ORDER BY t.depth, o.object_type, o.designation, o.name;";

    public async Task<TreeNode?> LoadTreeAsync(long rootObjectId, CancellationToken ct)
    {
        var rows = (await connection.QueryAsync<TreeRow>(Cmd(TreeSql, new { root = rootObjectId }, ct))).ToList();
        if (rows.Count == 0) return null;

        var nodes = new Dictionary<string, TreeNode>(StringComparer.Ordinal);
        TreeNode? root = null;
        foreach (var r in rows)   // строки идут по возрастанию глубины: родитель всегда уже создан
        {
            var node = new TreeNode
            {
                ObjectId = r.ObjectId,
                Type = Enum.Parse<ObjectType>(r.ObjectType),
                Designation = r.Designation,
                Name = r.Name,
                VersionNo = (int?)r.VersionNo,
                State = r.State is null ? null : Enum.Parse<VersionState>(r.State),
                Material = r.Material,
                MassKg = r.MassKg is null ? null : (decimal)r.MassKg.Value,
                Quantity = (int)r.Quantity,
                IsCycle = r.IsCycle != 0
            };
            nodes[r.Path] = node;
            if (r.ParentPath is null) root = node;
            else nodes[r.ParentPath].Children.Add(node);
        }
        return root;
    }

    public async Task<IReadOnlyList<ObjectSummary>> SearchAsync(string? text, CancellationToken ct)
    {
        var like = text is null ? null : "%" + text.ToLowerInvariant() + "%";
        var rows = await connection.QueryAsync<SummaryRow>(Cmd(@"
SELECT o.id AS Id, o.object_type AS ObjectType, o.designation AS Designation, o.name AS Name,
       v.version_no AS VersionNo, v.state AS State
FROM pdm_object o
LEFT JOIN object_version v ON v.id = o.current_version_id
WHERE @like IS NULL OR lower_u(o.designation) LIKE @like OR lower_u(o.name) LIKE @like
ORDER BY CASE o.object_type WHEN 'Assembly' THEN 0 WHEN 'Part' THEN 1 ELSE 2 END, o.designation, o.name
LIMIT 1000", new { like }, ct));

        return rows.Select(r => new ObjectSummary(r.Id, Enum.Parse<ObjectType>(r.ObjectType), r.Designation, r.Name,
            (int?)r.VersionNo, r.State is null ? null : Enum.Parse<VersionState>(r.State))).ToList();
    }

    public Task AddImportLogAsync(DateTime startedAt, string fileName, string severity, string reason, CancellationToken ct) =>
        connection.ExecuteAsync(Cmd(
            "INSERT INTO import_log (started_at, file_name, severity, reason) VALUES (@startedAt, @fileName, @severity, @reason)",
            new { startedAt = startedAt.ToString("O", CultureInfo.InvariantCulture), fileName, severity, reason }, ct));

    public async Task CommitAsync(CancellationToken ct)
    {
        if (tx is not null) await tx.CommitAsync(ct);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (tx is not null)
        {
            if (!_committed) await tx.RollbackAsync();   // отмена или ошибка откат
            await tx.DisposeAsync();
        }
        await connection.DisposeAsync();
    }
}
