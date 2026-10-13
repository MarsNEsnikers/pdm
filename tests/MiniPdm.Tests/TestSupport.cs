using Microsoft.Data.Sqlite;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Data;
using MiniPdm.Core.Domain;
using MiniPdm.Infrastructure.Data;

namespace MiniPdm.Tests;

/// <summary>Подмена CAD-системы: документы лежат в памяти, файловая система не используется.</summary>
public sealed class FakeCadReader : ICadDocumentReader
{
    private readonly Dictionary<string, object> _docs = new(StringComparer.Ordinal);

    public FakeCadReader Add(CadDocument doc) { _docs[doc.FileName] = doc; return this; }
    public FakeCadReader AddBroken(string file, string message) { _docs[file] = new CadDocumentException(message); return this; }
    public FakeCadReader Remove(string file) { _docs.Remove(file); return this; }

    public Task<IReadOnlyList<string>> ListDocumentsAsync(string location, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(_docs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());

    public Task<CadDocument> ReadAsync(string location, string documentId, CancellationToken ct) =>
        _docs[documentId] switch
        {
            CadDocumentException ex => Task.FromException<CadDocument>(ex),
            CadDocument d => Task.FromResult(d),
            _ => throw new InvalidOperationException()
        };
}

public static class Docs
{
    public static CadDocument Part(string file, string designation, decimal? mass = 1m, string name = "")
        => new(file, ObjectType.Part, designation, name == "" ? file : name, "Сталь", mass, []);

    public static CadDocument Std(string file, decimal? mass = 0.1m)
        => new(file, ObjectType.StandardPart, null, file, null, mass, []);

    public static CadDocument Asm(string file, string designation, params (string File, int Count)[] components)
        => new(file, ObjectType.Assembly, designation, file, null, null,
            components.Select(c => new CadComponent(c.File, c.Count)).ToList());
}

/// <summary>База SQLite в памяти; keeper-соединение держит её живой между сессиями.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _keeper;
    public SqlitePdmStoreFactory Factory { get; }

    private TestDb(string cs)
    {
        _keeper = new SqliteConnection(cs);
        _keeper.Open();
        Factory = new SqlitePdmStoreFactory(cs);
    }

    public static async Task<TestDb> CreateAsync()
    {
        var db = new TestDb($"Data Source=pdm{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await db.Factory.InitializeAsync();
        return db;
    }

    public void Dispose() => _keeper.Dispose();

    public async Task<ObjectRecord> ObjectAsync(string designation)
    {
        await using var s = await Factory.OpenAsync(false, default);
        return (await s.FindByDesignationAsync(designation, default))!;
    }

    public async Task<IReadOnlyList<VersionRecord>> VersionsAsync(long objectId)
    {
        await using var s = await Factory.OpenAsync(false, default);
        return await s.GetVersionsAsync(objectId, default);
    }

    public async Task<int> CountObjectsAsync()
    {
        await using var s = await Factory.OpenAsync(false, default);
        return (await s.SearchAsync(null, default)).Count;
    }
}

public static class TreeBuilder
{
    public static TreeNode Node(string name, ObjectType type = ObjectType.Part, decimal? mass = null,
        int qty = 1, long? id = null, bool hasVersion = true) => new()
    {
        ObjectId = id ?? Math.Abs(name.GetHashCode()),
        Type = type,
        Name = name,
        Designation = null,
        VersionNo = hasVersion ? 1 : null,
        State = hasVersion ? VersionState.InWork : null,
        MassKg = mass,
        Quantity = qty
    };

    public static TreeNode Asm(string name, long id, params TreeNode[] children) => Sub(name, id, 1, children);

    public static TreeNode Sub(string name, long id, int qty, params TreeNode[] children)
    {
        var n = Node(name, ObjectType.Assembly, null, qty, id);
        n.Children.AddRange(children);
        return n;
    }
}
