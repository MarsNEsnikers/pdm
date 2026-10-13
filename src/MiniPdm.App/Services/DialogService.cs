using System.Windows;
using Microsoft.Win32;
using MiniPdm.App.ViewModels;
using MiniPdm.App.Views;
using MiniPdm.Core.Import;

namespace MiniPdm.App.Services;

public interface IDialogService
{
    string? PickFolder();
    string? PickSaveFile(string defaultFileName);
    void ShowReport(ImportReport report);
    void ShowError(string message);
}

public sealed class DialogService : IDialogService
{
    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Папка с CAD-документами" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string defaultFileName)
    {
        var dialog = new SaveFileDialog { FileName = defaultFileName, Filter = "CSV (*.csv)|*.csv" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowReport(ImportReport report) =>
        new ImportReportWindow { DataContext = new ImportReportViewModel(report, this), Owner = Application.Current.MainWindow }
            .ShowDialog();

    public void ShowError(string message) =>
        MessageBox.Show(message, "Mini-PDM", MessageBoxButton.OK, MessageBoxImage.Error);
}
