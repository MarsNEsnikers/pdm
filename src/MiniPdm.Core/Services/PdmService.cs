using MiniPdm.Core.Calculations;
using MiniPdm.Core.Data;
using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Services;

/// <summary>Сценарии, которые использует интерфейс: поиск, дерево, карточка, смена состояния, расчёты.</summary>
public sealed class PdmService(IPdmStoreFactory storeFactory)
{
    public async Task<IReadOnlyList<ObjectSummary>> SearchAsync(string? text, CancellationToken ct = default)
    {
        await using var s = await storeFactory.OpenAsync(false, ct);
        return await s.SearchAsync(string.IsNullOrWhiteSpace(text) ? null : text.Trim(), ct);
    }

    public async Task<TreeNode?> GetTreeAsync(long objectId, CancellationToken ct = default)
    {
        await using var s = await storeFactory.OpenAsync(false, ct);
        return await s.LoadTreeAsync(objectId, ct);
    }

    public async Task<ObjectCard?> GetCardAsync(long objectId, CancellationToken ct = default)
    {
        await using var s = await storeFactory.OpenAsync(false, ct);
        var obj = await s.GetObjectAsync(objectId, ct);
        if (obj is null) return null;
        var versions = await s.GetVersionsAsync(objectId, ct);
        return new ObjectCard(obj, versions, CurrentVersion.Pick(versions));
    }

    public async Task ChangeStateAsync(long objectId, long versionId, VersionState target, CancellationToken ct = default)
    {
        await using var s = await storeFactory.OpenAsync(true, ct);
        var versions = await s.GetVersionsAsync(objectId, ct);
        var version = versions.FirstOrDefault(v => v.Id == versionId)
                      ?? throw new InvalidOperationException("Версия не найдена");

        VersionStateMachine.EnsureTransition(version.State, target);
        await s.SetVersionStateAsync(versionId, target, ct);

        // current_version_id всегда указывает на последнюю неаннулированную версию
        var updated = versions.Select(v => v.Id == versionId ? v with { State = target } : v);
        await s.SetCurrentVersionAsync(objectId, CurrentVersion.Pick(updated)?.Id, ct);
        await s.CommitAsync(ct);
    }

    public async Task<MassResult> CalculateMassAsync(long rootId, CancellationToken ct = default)
    {
        var tree = await GetTreeAsync(rootId, ct) ?? throw new InvalidOperationException("Объект не найден");
        return MassCalculator.Calculate(tree);
    }

    public async Task<IReadOnlyList<BomLine>> GetBomAsync(long rootId, CancellationToken ct = default)
    {
        var tree = await GetTreeAsync(rootId, ct) ?? throw new InvalidOperationException("Объект не найден");
        return BomBuilder.Build(tree);
    }
}
