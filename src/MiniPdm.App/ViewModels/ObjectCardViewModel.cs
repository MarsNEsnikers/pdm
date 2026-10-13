using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniPdm.Core.Data;
using MiniPdm.Core.Domain;

namespace MiniPdm.App.ViewModels;

public sealed record VersionRow(long Id, int VersionNo, VersionState State, string StateText,
    string Material, string Mass, string CreatedAt, bool IsCurrent);

public sealed partial class ObjectCardViewModel : ObservableObject
{
    public ObjectCardViewModel(ObjectCard card)
    {
        ObjectId = card.Object.Id;
        TypeText = DisplayNames.Of(card.Object.Type);
        Designation = card.Object.Designation ?? "-";
        Name = card.Object.Name;
        Material = card.Current?.Material ?? "-";
        Mass = card.Object.Type == ObjectType.Assembly ? "вычисляется по составу"
            : card.Current?.MassKg is { } m ? $"{m:0.###} кг" : "не указана";
        CurrentVersionText = card.Current is null ? "нет действующей версии"
            : $"{card.Current.VersionNo} - {DisplayNames.Of(card.Current.State)}";
        Versions = new ObservableCollection<VersionRow>(card.Versions.OrderByDescending(v => v.VersionNo).Select(v =>
            new VersionRow(v.Id, v.VersionNo, v.State, DisplayNames.Of(v.State), v.Material ?? "",
                v.MassKg is { } mm ? $"{mm:0.###}" : "", v.CreatedAt.ToLocalTime().ToString("g"), v.Id == card.Current?.Id)));
        SelectedVersion = Versions.FirstOrDefault(v => v.IsCurrent) ?? Versions.FirstOrDefault();
    }

    public long ObjectId { get; }
    public string TypeText { get; }
    public string Designation { get; }
    public string Name { get; }
    public string Material { get; }
    public string Mass { get; }
    public string CurrentVersionText { get; }
    public ObservableCollection<VersionRow> Versions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApprove), nameof(CanAnnul))]
    private VersionRow? _selectedVersion;

    public bool CanApprove => SelectedVersion is { } v && VersionStateMachine.CanTransition(v.State, VersionState.Approved);
    public bool CanAnnul => SelectedVersion is { } v && VersionStateMachine.CanTransition(v.State, VersionState.Annulled);
}
