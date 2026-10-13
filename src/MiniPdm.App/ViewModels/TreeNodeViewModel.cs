using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniPdm.Core.Data;

namespace MiniPdm.App.ViewModels;

public sealed class TreeNodeViewModel : ObservableObject
{
    private readonly Action<TreeNodeViewModel> _onSelected;
    private bool _isSelected;
    private bool _isExpanded = true;
    private bool _hasMissingMass;

    public TreeNodeViewModel(TreeNode node, Action<TreeNodeViewModel> onSelected)
    {
        Node = node;
        _onSelected = onSelected;
        Children = new ObservableCollection<TreeNodeViewModel>(node.Children.Select(c => new TreeNodeViewModel(c, onSelected)));
    }

    public TreeNode Node { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; }

    public string Header =>
        $"{(Node.Designation is null ? "" : Node.Designation + "  ")}{Node.Name}" +
        (Node.Quantity > 1 ? $"  × {Node.Quantity}" : "") +
        (Node.IsCycle ? "  ⟲ циклическая ссылка" : "") +
        (Node.HasCurrentVersion ? "" : "  (нет действующей версии)") +
        (HasMissingMass ? "  ⚠ нет массы" : "");

    /// <summary>Подсветка узла, у которого нет массы (по результату последнего расчёта).</summary>
    public bool HasMissingMass
    {
        get => _hasMissingMass;
        private set { if (SetProperty(ref _hasMissingMass, value)) OnPropertyChanged(nameof(Header)); }
    }

    /// <summary>Выставляет подсветку во всём поддереве; узлы вне набора подсветку теряют.</summary>
    public void MarkMissingMass(IReadOnlySet<long> objectIds)
    {
        HasMissingMass = objectIds.Contains(Node.ObjectId);
        foreach (var child in Children) child.MarkMissingMass(objectIds);
    }

    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value) && value) _onSelected(this); }
    }
}
