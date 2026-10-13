using MiniPdm.Core.Calculations;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Data;
using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Import;

/// <summary>
/// Импорт набора CAD-документов в PDM. Работает в одной транзакции: любое исключение или отмена
/// откатывают все изменения. Ошибочные файлы пропускаются и попадают в отчёт.
/// </summary>
public sealed class ImportService(ICadDocumentReader reader, IPdmStoreFactory storeFactory, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private sealed class Entry
    {
        public required string FileName { get; init; }
        public CadDocument? Doc { get; set; }
        public string? Error { get; set; }
        public List<string> Warnings { get; } = new();
        public ObjectRecord? Existing { get; set; }
        public long? ObjectId { get; set; }
        public string? Action { get; set; }
        public bool IsRejected => Error is not null;
    }

    public async Task<ImportReport> ImportAsync(string location, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var startedAt = _clock.GetUtcNow().UtcDateTime;

        // 1. Чтение
        var ids = await reader.ListDocumentsAsync(location, ct);
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ImportProgress("Чтение файлов", i, ids.Count));
            var id = ids[i];
            var entry = new Entry { FileName = id };
            try
            {
                var doc = await reader.ReadAsync(location, id, ct);
                entry.Doc = Normalize(doc with { FileName = id });
            }
            catch (CadDocumentException ex) { entry.Error = ex.Message; }
            entries[id] = entry;
        }
        progress?.Report(new ImportProgress("Чтение файлов", ids.Count, ids.Count));

        // 2. Проверки отдельных документов и набора в целом
        foreach (var e in entries.Values.Where(e => e.Doc is not null))
        {
            var v = DocumentValidator.Validate(e.Doc!);
            e.Error ??= v.Error;
            e.Warnings.AddRange(v.Warnings);
        }
        RejectDuplicates(entries);

        await using var session = await storeFactory.OpenAsync(transactional: true, ct);

        await RejectTypeMismatchesAsync(session, entries, ct);
        PropagateRejections(entries);
        RejectCycles(entries);
        PropagateRejections(entries);

        // 3. Сохранение: от листьев к корням, чтобы у родителя уже были id дочерних объектов
        var order = TopologicalOrder(entries);
        for (var i = 0; i < order.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ImportProgress("Сохранение", i, order.Count));
            await PersistAsync(session, entries, order[i], ct);
        }
        progress?.Report(new ImportProgress("Сохранение", order.Count, order.Count));

        ct.ThrowIfCancellationRequested();
        var report = BuildReport(entries, location);
        foreach (var r in report.Entries.Where(r => r.Severity != ReportSeverity.Info))
            await session.AddImportLogAsync(startedAt, r.FileName, r.Severity.ToString(), r.Reason, ct);

        await session.CommitAsync(ct);
        return report;
    }

    private static CadDocument Normalize(CadDocument d) => d with
    {
        Name = d.Name?.Trim() ?? "",
        Designation = Clean(d.Designation),
        Material = Clean(d.Material),
        Components = d.Components ?? Array.Empty<CadComponent>()
    };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void RejectDuplicates(Dictionary<string, Entry> entries)
    {
        var byDesignation = entries.Values
            .Where(e => e.Doc is { Type: not ObjectType.StandardPart, Designation: not null })
            .GroupBy(e => e.Doc!.Designation!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);
        foreach (var g in byDesignation)
        {
            var files = string.Join(", ", g.Select(e => $"«{e.FileName}»"));
            foreach (var e in g) e.Error ??= $"Обозначение {g.Key} повторяется в файлах: {files}";
        }

        var byName = entries.Values
            .Where(e => e.Doc is { Type: ObjectType.StandardPart })
            .GroupBy(e => e.Doc!.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);
        foreach (var g in byName)
        {
            var files = string.Join(", ", g.Select(e => $"«{e.FileName}»"));
            foreach (var e in g) e.Error ??= $"Наименование стандартного изделия «{g.Key}» повторяется в файлах: {files}";
        }
    }

    private static async Task RejectTypeMismatchesAsync(IPdmStore store, Dictionary<string, Entry> entries, CancellationToken ct)
    {
        foreach (var e in entries.Values.Where(e => !e.IsRejected))
        {
            var d = e.Doc!;
            e.Existing = d.Type == ObjectType.StandardPart
                ? await store.FindStandardByNameAsync(d.Name, ct)
                : await store.FindByDesignationAsync(d.Designation!, ct);
            if (e.Existing is not null && e.Existing.Type != d.Type)
                e.Error = $"Обозначение {d.Designation} уже занято объектом типа «{DisplayNames.Of(e.Existing.Type)}»";
        }
    }

    /// <summary>Сборка со ссылкой на отсутствующий или отклонённый файл отклоняется; повторяем до фиксированной точки.</summary>
    private static void PropagateRejections(Dictionary<string, Entry> entries)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var e in entries.Values.Where(e => !e.IsRejected && e.Doc!.Type == ObjectType.Assembly))
            {
                foreach (var c in e.Doc!.Components)
                {
                    if (!entries.TryGetValue(c.File, out var child))
                        e.Error = $"Ссылка на отсутствующий файл «{c.File}»";
                    else if (child.IsRejected)
                        e.Error = $"Компонент «{c.File}» отклонён";
                    else continue;
                    changed = true;
                    break;
                }
            }
        } while (changed);
    }

    private static void RejectCycles(Dictionary<string, Entry> entries)
    {
        var graph = entries.Values
            .Where(e => !e.IsRejected && e.Doc!.Type == ObjectType.Assembly)
            .ToDictionary(
                e => e.FileName,
                e => (IReadOnlyCollection<string>)e.Doc!.Components.Select(c => c.File).ToList(),
                StringComparer.Ordinal);

        foreach (var file in CycleDetector.FindNodesInCycles(graph))
            entries[file].Error = "Циклическая ссылка в составе изделия";
    }

    private static List<Entry> TopologicalOrder(Dictionary<string, Entry> entries)
    {
        var result = new List<Entry>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(Entry e)
        {
            if (!visited.Add(e.FileName)) return;
            foreach (var c in e.Doc!.Components) Visit(entries[c.File]);
            result.Add(e);
        }

        foreach (var e in entries.Values.Where(e => !e.IsRejected).OrderBy(e => e.FileName, StringComparer.Ordinal))
            Visit(e);
        return result;
    }

    private async Task PersistAsync(IPdmStore store, Dictionary<string, Entry> entries, Entry e, CancellationToken ct)
    {
        var d = e.Doc!;
        var now = _clock.GetUtcNow().UtcDateTime;
        var mass = d.Type == ObjectType.Assembly ? null : d.Mass;
        var links = d.Components
            .Select(c => new LinkRecord(entries[c.File].ObjectId!.Value, c.Count))
            .OrderBy(l => l.ChildObjectId)
            .ToList();

        if (e.Existing is null)
        {
            var id = await store.InsertObjectAsync(d.Type, d.Type == ObjectType.StandardPart ? null : d.Designation, d.Name, ct);
            var vid = await store.InsertVersionAsync(id, 1, VersionState.InWork, d.Material, mass, now, ct);
            await store.ReplaceLinksAsync(vid, links, ct);
            await store.SetCurrentVersionAsync(id, vid, ct);
            e.ObjectId = id;
            e.Action = "Создан объект, версия 1";
            return;
        }

        var existing = e.Existing;
        e.ObjectId = existing.Id;
        var versions = await store.GetVersionsAsync(existing.Id, ct);
        var current = CurrentVersion.Pick(versions);

        if (current is null)
        {
            var no = versions.Max(v => v.VersionNo) + 1;
            await CreateVersionAsync(store, existing, no, d, mass, links, now, ct);
            e.Action = $"Все версии были аннулированы, создана версия {no}";
            return;
        }

        var currentLinks = await store.GetLinksAsync(current.Id, ct);
        var same = existing.Name == d.Name
                   && current.Material == d.Material
                   && current.MassKg == mass
                   && currentLinks.OrderBy(l => l.ChildObjectId).SequenceEqual(links);
        if (same)
        {
            e.Action = $"Без изменений (версия {current.VersionNo})";
        }
        else if (VersionStateMachine.CanModify(current.State))
        {
            if (existing.Name != d.Name) await store.UpdateObjectNameAsync(existing.Id, d.Name, ct);
            await store.UpdateVersionDataAsync(current.Id, d.Material, mass, ct);
            await store.ReplaceLinksAsync(current.Id, links, ct);
            e.Action = $"Обновлена версия {current.VersionNo} (в работе)";
        }
        else
        {
            var no = versions.Max(v => v.VersionNo) + 1;
            await CreateVersionAsync(store, existing, no, d, mass, links, now, ct);
            e.Action = $"Версия {current.VersionNo} утверждена создана версия {no}";
        }
    }

    private static async Task CreateVersionAsync(IPdmStore store, ObjectRecord obj, int versionNo, CadDocument d,
        decimal? mass, List<LinkRecord> links, DateTime now, CancellationToken ct)
    {
        if (obj.Name != d.Name) await store.UpdateObjectNameAsync(obj.Id, d.Name, ct);
        var vid = await store.InsertVersionAsync(obj.Id, versionNo, VersionState.InWork, d.Material, mass, now, ct);
        await store.ReplaceLinksAsync(vid, links, ct);
        await store.SetCurrentVersionAsync(obj.Id, vid, ct);
    }

    private static ImportReport BuildReport(Dictionary<string, Entry> entries, string location)
    {
        var rows = entries.Values
            .OrderBy(e => e.FileName, StringComparer.Ordinal)
            .Select(e =>
            {
                if (e.IsRejected) return new ImportReportEntry(e.FileName, ReportSeverity.Error, e.Error!, null);
                if (e.Warnings.Count > 0)
                    return new ImportReportEntry(e.FileName, ReportSeverity.Warning, string.Join("; ", e.Warnings), e.Action);
                return new ImportReportEntry(e.FileName, ReportSeverity.Info, "", e.Action);
            })
            .ToList();
        return new ImportReport(rows, location);
    }
}
