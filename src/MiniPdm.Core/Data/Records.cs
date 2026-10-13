using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Data;

public sealed record ObjectRecord(long Id, ObjectType Type, string? Designation, string Name, long? CurrentVersionId);

public sealed record VersionRecord(
    long Id, long ObjectId, int VersionNo, VersionState State,
    string? Material, decimal? MassKg, DateTime CreatedAt);

/// <summary>Связь «входит в»: ссылка на объект (не на версию) и количество.</summary>
public sealed record LinkRecord(long ChildObjectId, int Quantity);

public sealed record ObjectSummary(
    long Id, ObjectType Type, string? Designation, string Name, int? VersionNo, VersionState? State)
{
    public string Title => Designation is null ? Name : $"{Designation}  {Name}";
}

public sealed record ObjectCard(ObjectRecord Object, IReadOnlyList<VersionRecord> Versions, VersionRecord? Current);

public static class CurrentVersion
{
    /// <summary>Текущая версия последняя неаннулированная.</summary>
    public static VersionRecord? Pick(IEnumerable<VersionRecord> versions) =>
        versions.Where(v => v.State != VersionState.Annulled).MaxBy(v => v.VersionNo);
}
