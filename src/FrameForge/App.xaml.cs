// Точка входа приложения.
using System.Windows;
namespace FrameForge;
/// <summary>Управляет запуском единственного экземпляра.</summary>
public partial class App : Application
{
    private System.Threading.Mutex? instance;
    /// <summary>Проверяет наличие запущенного экземпляра.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        instance = new System.Threading.Mutex(true, "Local\\FrameForge.Desktop", out var created);
        if (!created) { MessageBox.Show("Frame Forge уже запущен."); Shutdown(); return; }
        base.OnStartup(e);
    }
    /// <summary>Освобождает блокировку экземпляра.</summary>
    protected override void OnExit(ExitEventArgs e)
    { instance?.Dispose(); base.OnExit(e); }
}
