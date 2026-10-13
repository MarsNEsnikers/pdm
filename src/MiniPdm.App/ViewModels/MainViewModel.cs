using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniPdm.App.Services;
using MiniPdm.Core.Calculations;
using MiniPdm.Core.Data;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Import;
using MiniPdm.Core.Services;

namespace MiniPdm.App.ViewModels;

public sealed partial class MainViewModel(PdmService pdm, ImportService import, IDialogService dialogs) : ObservableObject
{
    public ObservableCollection<ObjectSummary> Objects { get; } = new();
    public ObservableCollection<TreeNodeViewModel> TreeRoots { get; } = new();
    public ObservableCollection<BomLine> BomLines { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObjectSummary? _selectedObject;
    [ObservableProperty] private TreeNodeViewModel? _selectedNode;
    [ObservableProperty] private ObjectCardViewModel? _card;
    [ObservableProperty] private string _massText = "";
    [ObservableProperty] private string _bomText = "";
    [ObservableProperty] private string _statusText = "Готово";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowLastReportCommand))]
    private ImportReport? _lastReport;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private double _progressMax = 1;

    public Task InitializeAsync() => SearchAsync();

    partial void OnSearchTextChanged(string value) => _ = SearchAsync();

    partial void OnSelectedObjectChanged(ObjectSummary? value) => _ = RunSafeAsync(() => LoadTreeAsync(value));

    // ---- поиск и дерево -------------------------------------------------------------------------------

    [RelayCommand]
    private async Task SearchAsync()
    {
        await RunSafeAsync(async () =>
        {
            var found = await pdm.SearchAsync(SearchText);
            Objects.Clear();
            foreach (var o in found) Objects.Add(o);
            StatusText = $"Найдено объектов: {found.Count}";
        });
    }

    private async Task LoadTreeAsync(ObjectSummary? obj)
    {
        TreeRoots.Clear();
        BomLines.Clear();
        MassText = BomText = "";
        Card = null;
        SelectedNode = null;
        if (obj is null) return;

        var tree = await pdm.GetTreeAsync(obj.Id);
        if (tree is null) return;
        var root = new TreeNodeViewModel(tree, n => SelectedNode = n);
        TreeRoots.Add(root);
        await LoadCardAsync(obj.Id);
    }

    partial void OnSelectedNodeChanged(TreeNodeViewModel? value)
    {
        if (value is not null) _ = RunSafeAsync(() => LoadCardAsync(value.Node.ObjectId));
    }

    private async Task LoadCardAsync(long objectId)
    {
        var card = await pdm.GetCardAsync(objectId);
        Card = card is null ? null : new ObjectCardViewModel(card);
    }

    // ---- смена состояния ------------------------------------------------------------------------------

    [RelayCommand]
    private Task ApproveAsync() => ChangeStateAsync(VersionState.Approved);

    [RelayCommand]
    private Task AnnulAsync() => ChangeStateAsync(VersionState.Annulled);

    private async Task ChangeStateAsync(VersionState target)
    {
        if (Card?.SelectedVersion is not { } version) return;
        var objectId = Card.ObjectId;
        await RunSafeAsync(async () =>
        {
            await pdm.ChangeStateAsync(objectId, version.Id, target);
            await LoadCardAsync(objectId);
            await SearchAsync();   // у объекта могла смениться текущая версия
            StatusText = $"Версия {version.VersionNo}: «{DisplayNames.Of(target)}»";
        });
    }

    // ---- расчёты --------------------------------------------------------------------------------------

    private static readonly IReadOnlySet<long> NoIds = new HashSet<long>();

    private void HighlightMissingMass(IReadOnlySet<long> ids)
    {
        foreach (var root in TreeRoots) root.MarkMissingMass(ids);
    }

    /// <summary>Расчёт относится к выбранной в дереве сборке, а если выбрана не сборка к корню дерева.</summary>
    private long? TargetAssemblyId =>
        SelectedNode is { Node.Type: ObjectType.Assembly } n ? n.Node.ObjectId
        : SelectedObject is { Type: ObjectType.Assembly } o ? o.Id
        : null;

    [RelayCommand]
    private async Task CalculateMassAsync()
    {
        if (TargetAssemblyId is not { } id) { MassText = "Выберите сборку"; return; }
        await RunSafeAsync(async () =>
        {
            try
            {
                var r = await pdm.CalculateMassAsync(id);
                HighlightMissingMass(r.MissingObjectIds);
                MassText = r.TotalKg is { } m
                    ? $"Масса сборки: {m:0.###} кг"
                    : "Массу рассчитать нельзя нет массы у: " + string.Join("; ", r.MissingMass);
            }
            catch (CyclicReferenceException ex) { HighlightMissingMass(NoIds); MassText = ex.Message; }
        });
    }

    [RelayCommand]
    private async Task ShowBomAsync()
    {
        if (TargetAssemblyId is not { } id) { BomText = "Выберите сборку"; return; }
        await RunSafeAsync(async () =>
        {
            try
            {
                var bom = await pdm.GetBomAsync(id);
                BomLines.Clear();
                foreach (var l in bom) BomLines.Add(l);
                BomText = $"Позиций: {bom.Count}";
            }
            catch (CyclicReferenceException ex) { BomText = ex.Message; }
        });
    }

    // ---- импорт ---------------------------------------------------------------------------------------

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanImport))]
    private async Task ImportAsync(CancellationToken ct)
    {
        var folder = dialogs.PickFolder();
        if (folder is null) return;

        IsBusy = true;
        ImportCommand.NotifyCanExecuteChanged();
        var progress = new Progress<ImportProgress>(p =>
        {
            ProgressMax = Math.Max(p.Total, 1);
            ProgressValue = p.Done;
            StatusText = $"{p.Stage}: {p.Done} из {p.Total}";
        });

        try
        {
            var report = await Task.Run(() => import.ImportAsync(folder, progress, ct), ct);
            LastReport = report;
            StatusText = $"Импорт завершён. Принято: {report.Accepted}, отклонено: {report.Rejected}";
            await SearchAsync();
            dialogs.ShowReport(report);
        }
        catch (OperationCanceledException) { StatusText = "Импорт отменён, изменения не сохранены"; }
        catch (Exception ex) { dialogs.ShowError(ex.Message); StatusText = "Ошибка импорта"; }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ImportCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanImport() => !IsBusy;

    [RelayCommand(CanExecute = nameof(HasReport))]
    private void ShowLastReport() => dialogs.ShowReport(LastReport!);

    private bool HasReport() => LastReport is not null;

    private async Task RunSafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { StatusText = "Ошибка: " + ex.Message; dialogs.ShowError(ex.Message); }
    }
}
