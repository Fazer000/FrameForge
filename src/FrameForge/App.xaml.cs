using System.Windows;
namespace FrameForge;
public partial class App : Application
{
    private System.Threading.Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        instance = new System.Threading.Mutex(true, "Local\\FrameForge.Desktop", out var created);
        if (!created) { MessageBox.Show("Frame Forge уже запущен."); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    { instance?.Dispose(); base.OnExit(e); }
}
