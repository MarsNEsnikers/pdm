using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Data;

/// <summary>Узел дерева состава (по текущей версии объекта). Для корня Quantity = 1.</summary>
public sealed class TreeNode
{
    public required long ObjectId { get; init; }
    public required ObjectType Type { get; init; }
    public string? Designation { get; init; }
    public required string Name { get; init; }
    public int? VersionNo { get; init; }
    public VersionState? State { get; init; }
    public string? Material { get; init; }
    public decimal? MassKg { get; init; }
    public int Quantity { get; init; } = 1;
    /// <summary>Узел повторяет предка циклическая ссылка; потомки не разворачиваются.</summary>
    public bool IsCycle { get; init; }
    public List<TreeNode> Children { get; } = new();

    public bool HasCurrentVersion => VersionNo is not null;
    public string DisplayName => Designation is null ? Name : $"{Designation} {Name}";
}
