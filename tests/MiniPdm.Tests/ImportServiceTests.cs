using MiniPdm.Core.Cad;
using MiniPdm.Core.Calculations;
using MiniPdm.Core.Data;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Import;
using MiniPdm.Core.Services;
using static MiniPdm.Tests.Docs;

namespace MiniPdm.Tests;

public class ImportServiceTests : IAsyncLifetime
{
    private TestDb _db = null!;
    private readonly FakeCadReader _cad = new();

    public async Task InitializeAsync() => _db = await TestDb.CreateAsync();
    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    private Task<ImportReport> ImportAsync(CancellationToken ct = default) =>
        new ImportService(_cad, _db.Factory).ImportAsync("memory", null, ct);

    private static ImportReportEntry Row(ImportReport r, string file) => r.Entries.Single(e => e.FileName == file);

    [Fact]
    public async Task HappyPath_ImportsTreeAndCalculates()
    {
        _cad.Add(Part("Вал.m3d", "АБВГ.000001.001", 2m))
            .Add(Std("Болт.m3d", 0.1m))
            .Add(Asm("Узел.a3d", "АБВГ.000002.001", ("Вал.m3d", 1), ("Болт.m3d", 4)));

        var report = await ImportAsync();

        Assert.Equal(3, report.Accepted);
        Assert.Equal(0, report.Rejected);
        var asm = await _db.ObjectAsync("АБВГ.000002.001");
        var mass = await new PdmService(_db.Factory).CalculateMassAsync(asm.Id);
        Assert.Equal(2.4m, mass.TotalKg);
    }

    [Fact]
    public async Task DuplicateDesignation_RejectsAllFilesWithIt_AndTheirParents()
    {
        _cad.Add(Part("a.m3d", "АБВГ.000001.001")).Add(Part("b.m3d", "АБВГ.000001.001"))
            .Add(Part("c.m3d", "АБВГ.000001.002"))
            .Add(Asm("top.a3d", "АБВГ.000009.001", ("a.m3d", 1), ("c.m3d", 1)));

        var report = await ImportAsync();

        Assert.Equal(ReportSeverity.Error, Row(report, "a.m3d").Severity);
        Assert.Equal(ReportSeverity.Error, Row(report, "b.m3d").Severity);
        Assert.Equal(ReportSeverity.Error, Row(report, "top.a3d").Severity);
        Assert.Contains("a.m3d", Row(report, "top.a3d").Reason);
        Assert.NotEqual(ReportSeverity.Error, Row(report, "c.m3d").Severity);
        Assert.Equal((1, 3), (report.Accepted, report.Rejected));
    }

    [Fact]
    public async Task ZeroQuantity_RejectsAssemblyAndEverythingAboveIt()
    {
        _cad.Add(Part("p.m3d", "АБВГ.000001.001"))
            .Add(Asm("low.a3d", "АБВГ.000002.001", ("p.m3d", 0)))
            .Add(Asm("mid.a3d", "АБВГ.000003.001", ("low.a3d", 1)))
            .Add(Asm("top.a3d", "АБВГ.000004.001", ("mid.a3d", 1)));

        var report = await ImportAsync();

        Assert.All(new[] { "low.a3d", "mid.a3d", "top.a3d" }, f => Assert.Equal(ReportSeverity.Error, Row(report, f).Severity));
        Assert.Equal(1, report.Accepted);
        Assert.Equal(1, await _db.CountObjectsAsync());
    }

    [Fact]
    public async Task MissingReference_RejectsAssembly()
    {
        _cad.Add(Asm("top.a3d", "АБВГ.000004.001", ("нет.m3d", 1)));
        var report = await ImportAsync();
        Assert.Contains("отсутствующий файл «нет.m3d»", Row(report, "top.a3d").Reason);
    }

    [Fact]
    public async Task UnreadableFile_IsRejected_OthersContinue()
    {
        _cad.AddBroken("bad.m3d", "Некорректный JSON").Add(Part("ok.m3d", "АБВГ.000001.001"));
        var report = await ImportAsync();
        Assert.Equal(ReportSeverity.Error, Row(report, "bad.m3d").Severity);
        Assert.Equal(1, report.Accepted);
    }

    [Fact]
    public async Task PartWithoutMass_IsImportedWithWarning_AndMassCalculationNamesIt()
    {
        _cad.Add(Part("p.m3d", "АБВГ.000001.001", mass: null, name: "Крышка"))
            .Add(Asm("top.a3d", "АБВГ.000002.001", ("p.m3d", 2)));

        var report = await ImportAsync();

        Assert.Equal(ReportSeverity.Warning, Row(report, "p.m3d").Severity);
        Assert.Equal(1, report.WithWarnings);
        Assert.Equal(2, report.Accepted);

        var svc = new PdmService(_db.Factory);
        var top = await _db.ObjectAsync("АБВГ.000002.001");
        var mass = await svc.CalculateMassAsync(top.Id);
        Assert.Null(mass.TotalKg);
        Assert.Contains(mass.MissingMass, m => m.Contains("Крышка"));
        Assert.Null((await svc.GetBomAsync(top.Id)).Single().TotalMassKg);
    }

    [Fact]
    public async Task Cycle_RejectsAllAssembliesOnItAndTheirParents()
    {
        _cad.Add(Asm("a.a3d", "АБВГ.000001.001", ("b.a3d", 1)))
            .Add(Asm("b.a3d", "АБВГ.000002.001", ("a.a3d", 1)))
            .Add(Asm("top.a3d", "АБВГ.000003.001", ("a.a3d", 1)))
            .Add(Part("p.m3d", "АБВГ.000004.001"));

        var report = await ImportAsync();

        Assert.Contains("Циклическая", Row(report, "a.a3d").Reason);
        Assert.Contains("Циклическая", Row(report, "b.a3d").Reason);
        Assert.Equal(ReportSeverity.Error, Row(report, "top.a3d").Severity);
        Assert.Equal(1, report.Accepted);
    }

    [Fact]
    public async Task SelfReference_IsRejected()
    {
        _cad.Add(Asm("a.a3d", "АБВГ.000001.001", ("a.a3d", 1)));
        Assert.Equal(ReportSeverity.Error, Row(await ImportAsync(), "a.a3d").Severity);
    }

    [Fact]
    public async Task DuplicateStandardPartName_RejectsBoth()
    {
        _cad.Add(Std("Болт 1.m3d") with { Name = "Болт" }).Add(Std("Болт 2.m3d") with { Name = "Болт" });
        var report = await ImportAsync();
        Assert.Equal(2, report.Rejected);
    }

    [Fact]
    public async Task Reimport_Unchanged_DoesNothing()
    {
        _cad.Add(Part("p.m3d", "АБВГ.000001.001", 1m));
        await ImportAsync();
        var report = await ImportAsync();

        Assert.Contains("Без изменений", Row(report, "p.m3d").Action);
        var versions = await _db.VersionsAsync((await _db.ObjectAsync("АБВГ.000001.001")).Id);
        Assert.Single(versions);
    }

    [Fact]
    public async Task Reimport_Changed_InWork_UpdatesSameVersion()
    {
        _cad.Add(Part("p.m3d", "АБВГ.000001.001", 1m));
        await ImportAsync();
        _cad.Add(Part("p.m3d", "АБВГ.000001.001", 2m));
        await ImportAsync();

        var versions = await _db.VersionsAsync((await _db.ObjectAsync("АБВГ.000001.001")).Id);
        var v = Assert.Single(versions);
        Assert.Equal(2m, v.MassKg);
    }

    [Fact]
    public async Task Reimport_Changed_Approved_CreatesNewInWorkVersion_AndOldStaysIntact()
    {
        _cad.Add(Part("p.m3d", "АБВГ.000001.001", 1m));
        await ImportAsync();
        var obj = await _db.ObjectAsync("АБВГ.000001.001");
        var svc = new PdmService(_db.Factory);
        var v1 = (await _db.VersionsAsync(obj.Id)).Single();
        await svc.ChangeStateAsync(obj.Id, v1.Id, VersionState.Approved);

        _cad.Add(Part("p.m3d", "АБВГ.000001.001", 3m));
        await ImportAsync();

        var versions = await _db.VersionsAsync(obj.Id);
        Assert.Equal(2, versions.Count);
        Assert.Equal((VersionState.Approved, 1m), (versions[0].State, versions[0].MassKg!.Value));
        Assert.Equal((VersionState.InWork, 3m), (versions[1].State, versions[1].MassKg!.Value));
        Assert.Equal(versions[1].Id, (await _db.ObjectAsync("АБВГ.000001.001")).CurrentVersionId);
    }

    [Fact]
    public async Task Reimport_ChangedQuantity_CreatesNewVersionOfAssembly()
    {
        _cad.Add(Part("p.m3d", "АБВГ.000001.001")).Add(Asm("a.a3d", "АБВГ.000002.001", ("p.m3d", 1)));
        await ImportAsync();
        _cad.Add(Asm("a.a3d", "АБВГ.000002.001", ("p.m3d", 5)));
        var report = await ImportAsync();
        Assert.Contains("Обновлена", Row(report, "a.a3d").Action);
    }

    [Fact]
    public async Task Reimport_AfterAllVersionsAnnulled_CreatesNextVersionNumber()
    {
        _cad.Add(Part("p.m3d", "АБВГ.000001.001"));
        await ImportAsync();
        var obj = await _db.ObjectAsync("АБВГ.000001.001");
        var svc = new PdmService(_db.Factory);
        await svc.ChangeStateAsync(obj.Id, (await _db.VersionsAsync(obj.Id)).Single().Id, VersionState.Annulled);
        Assert.Null((await _db.ObjectAsync("АБВГ.000001.001")).CurrentVersionId);

        await ImportAsync();

        var versions = await _db.VersionsAsync(obj.Id);
        Assert.Equal(new[] { 1, 2 }, versions.Select(v => v.VersionNo));
        Assert.NotNull((await _db.ObjectAsync("АБВГ.000001.001")).CurrentVersionId);
    }

    [Fact]
    public async Task ExistingObjectOfOtherType_IsRejected()
    {
        _cad.Add(Part("p.m3d", "АБВГ.000001.001"));
        await ImportAsync();
        _cad.Remove("p.m3d").Add(Asm("a.a3d", "АБВГ.000001.001"));
        Assert.Equal(ReportSeverity.Error, Row(await ImportAsync(), "a.a3d").Severity);
    }

    [Fact]
    public async Task Cancellation_RollsBackEverything()
    {
        _cad.Add(Part("a.m3d", "АБВГ.000001.001")).Add(Part("b.m3d", "АБВГ.000001.002"));
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Stage == "Сохранение" && p.Done == 1) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ImportService(_cad, _db.Factory).ImportAsync("memory", progress, cts.Token));

        Assert.Equal(0, await _db.CountObjectsAsync());
    }

    [Fact]
    public async Task ImportReader_IsOnlyUsedThroughInterface_NoFileSystem()
    {
        // Папки "memory" не существует тест проходит, значит ImportService не обращается к файловой системе.
        _cad.Add(Part("p.m3d", "АБВГ.000001.001"));
        Assert.Equal(1, (await ImportAsync()).Accepted);
    }

    private sealed class SyncProgress(Action<ImportProgress> a) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => a(value);
    }
}

public class StoreAndServiceTests : IAsyncLifetime
{
    private TestDb _db = null!;
    public async Task InitializeAsync() => _db = await TestDb.CreateAsync();
    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task LoadTree_FlagsCycles_AndDoesNotLoopForever()
    {
        await using (var s = await _db.Factory.OpenAsync(true, default))
        {
            var a = await s.InsertObjectAsync(ObjectType.Assembly, "АБВГ.000001.001", "A", default);
            var b = await s.InsertObjectAsync(ObjectType.Assembly, "АБВГ.000002.001", "B", default);
            var va = await s.InsertVersionAsync(a, 1, VersionState.InWork, null, null, DateTime.UtcNow, default);
            var vb = await s.InsertVersionAsync(b, 1, VersionState.InWork, null, null, DateTime.UtcNow, default);
            await s.SetCurrentVersionAsync(a, va, default);
            await s.SetCurrentVersionAsync(b, vb, default);
            await s.ReplaceLinksAsync(va, [new LinkRecord(b, 1)], default);
            await s.ReplaceLinksAsync(vb, [new LinkRecord(a, 1)], default);
            await s.CommitAsync(default);
        }

        var root = await new PdmService(_db.Factory).GetTreeAsync((await _db.ObjectAsync("АБВГ.000001.001")).Id);

        Assert.NotNull(root);
        Assert.True(root!.Children.Single().Children.Single().IsCycle);
        Assert.Throws<CyclicReferenceException>(() => MassCalculator.Calculate(root));
    }

    [Fact]
    public async Task ChangeState_ForbiddenTransition_Throws_AndAnnulMovesCurrentToPreviousVersion()
    {
        long objectId;
        long v1, v2;
        await using (var s = await _db.Factory.OpenAsync(true, default))
        {
            objectId = await s.InsertObjectAsync(ObjectType.Part, "АБВГ.000001.001", "P", default);
            v1 = await s.InsertVersionAsync(objectId, 1, VersionState.Approved, null, 1m, DateTime.UtcNow, default);
            v2 = await s.InsertVersionAsync(objectId, 2, VersionState.InWork, null, 2m, DateTime.UtcNow, default);
            await s.SetCurrentVersionAsync(objectId, v2, default);
            await s.CommitAsync(default);
        }
        var svc = new PdmService(_db.Factory);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            svc.ChangeStateAsync(objectId, v1, VersionState.InWork));

        await svc.ChangeStateAsync(objectId, v2, VersionState.Annulled);
        Assert.Equal(v1, (await _db.ObjectAsync("АБВГ.000001.001")).CurrentVersionId);
    }

    [Fact]
    public async Task Search_IsCaseInsensitive_ForCyrillic_AndMatchesDesignation()
    {
        await using (var s = await _db.Factory.OpenAsync(true, default))
        {
            var id = await s.InsertObjectAsync(ObjectType.Part, "АБВГ.000001.001", "Колесо зубчатое", default);
            await s.SetCurrentVersionAsync(id, await s.InsertVersionAsync(id, 1, VersionState.InWork, null, 1m, DateTime.UtcNow, default), default);
            await s.CommitAsync(default);
        }
        var svc = new PdmService(_db.Factory);
        Assert.Single(await svc.SearchAsync("КОЛЕСО"));
        Assert.Single(await svc.SearchAsync("000001"));
        Assert.Empty(await svc.SearchAsync("вал"));
        Assert.Single(await svc.SearchAsync(""));
    }

    [Fact]
    public async Task Schema_RejectsDuplicateDesignation_AndNonPositiveQuantity()
    {
        await using var s = await _db.Factory.OpenAsync(true, default);
        var a = await s.InsertObjectAsync(ObjectType.Part, "АБВГ.000001.001", "P", default);
        await Assert.ThrowsAnyAsync<Exception>(() => s.InsertObjectAsync(ObjectType.Part, "АБВГ.000001.001", "Q", default));
        var v = await s.InsertVersionAsync(a, 1, VersionState.InWork, null, 1m, DateTime.UtcNow, default);
        await Assert.ThrowsAnyAsync<Exception>(() => s.ReplaceLinksAsync(v, [new LinkRecord(a, 0)], default));
    }
}
