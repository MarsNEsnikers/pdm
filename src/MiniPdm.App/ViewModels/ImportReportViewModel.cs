using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniPdm.App.Services;
using MiniPdm.Core.Import;

namespace MiniPdm.App.ViewModels;

public sealed record ReportRow(string File, string Result, string Reason, string Action);

public sealed partial class ImportReportViewModel : ObservableObject
{
    private const string All = "все";
    private const string Errors = "ошибки";
    private const string Warnings = "предупреждения";
    private const string Accepted = "принятые";

    private readonly ImportReport _report;
    private readonly IDialogService _dialogs;

    public ImportReportViewModel(ImportReport report, IDialogService dialogs)
    {
        _report = report;
        _dialogs = dialogs;
        ApplyFilter();
    }

    public string Folder => string.IsNullOrEmpty(_report.Location) ? "" : "Папка: " + _report.Location;
    public int AcceptedCount => _report.Accepted;
    public int RejectedCount => _report.Rejected;
    public int WarningCount => _report.WithWarnings;

    public IReadOnlyList<string> Filters { get; } = [All, Errors, Warnings, Accepted];
    public ObservableCollection<ReportRow> Rows { get; } = new();

    [ObservableProperty] private string _selectedFilter = All;

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var entries = SelectedFilter switch
        {
            Errors => _report.Entries.Where(e => e.Severity == ReportSeverity.Error),
            Warnings => _report.Entries.Where(e => e.Severity == ReportSeverity.Warning),
            Accepted => _report.Entries.Where(e => e.Severity != ReportSeverity.Error),
            _ => _report.Entries
        };
        Rows.Clear();
        foreach (var e in entries)
            Rows.Add(new ReportRow(e.FileName, e.Severity.ToDisplay(), e.Reason, e.Action ?? ""));
    }

    [RelayCommand]
    private void Save()
    {
        var path = _dialogs.PickSaveFile("Отчёт об импорте.csv");
        if (path is null) return;
        try
        {
            File.WriteAllText(path, ImportReportFormatter.ToCsv(_report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) { _dialogs.ShowError("Не удалось сохранить файл: " + ex.Message); }
    }
}
