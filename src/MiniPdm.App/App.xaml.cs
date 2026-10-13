using System.IO;
using System.Windows;
using MiniPdm.App.Services;
using MiniPdm.App.ViewModels;
using MiniPdm.App.Views;
using MiniPdm.Core.Import;
using MiniPdm.Core.Services;
using MiniPdm.Infrastructure.Cad;
using MiniPdm.Infrastructure.Data;

namespace MiniPdm.App;

public partial class App : Application
{
    /// <summary>Единственное место композиции: все зависимости создаются здесь и передаются через конструкторы.</summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var dbPath = Environment.GetEnvironmentVariable("MINIPDM_DB") ?? DefaultDbPath();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

            var storeFactory = new SqlitePdmStoreFactory($"Data Source={dbPath}");
            await storeFactory.InitializeAsync();

            var pdm = new PdmService(storeFactory);
            var import = new ImportService(new JsonCadDocumentReader(), storeFactory);
            var viewModel = new MainViewModel(pdm, import, new DialogService());

            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            await viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string DefaultDbPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniPdm", "minipdm.db");
}
