using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using MiniPdm.Core.Data;

namespace MiniPdm.Infrastructure.Data;

public sealed class SqlitePdmStoreFactory : IPdmStoreFactory
{
    private readonly string _connectionString;

    static SqlitePdmStoreFactory() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    public SqlitePdmStoreFactory(string connectionString) => _connectionString = connectionString;

    /// <summary>Создаёт таблицы из скрипта schema.sqlite.sql (встроен в сборку).</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var session = (SqlitePdmSession)await OpenAsync(true, ct);
        var script = ReadSchemaScript();
        await session.ExecuteScriptAsync(script, ct);
        await session.CommitAsync(ct);
    }

    public async Task<IPdmSession> OpenAsync(bool transactional, CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        // LIKE в SQLite нечувствителен к регистру только для ASCII для кириллицы нужна своя функция
        connection.CreateFunction<string?, string?>("lower_u", s => s?.ToLowerInvariant());

        var tx = transactional ? (SqliteTransaction)await connection.BeginTransactionAsync(ct) : null;
        return new SqlitePdmSession(connection, tx);
    }

    private static string ReadSchemaScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("schema.sqlite.sql")
                           ?? throw new InvalidOperationException("Скрипт схемы не найден в ресурсах сборки");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
