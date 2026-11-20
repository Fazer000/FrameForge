// Хранит настройки и очередь для восстановления.
namespace FrameForge;

/// <summary>Хранит настройки обработки и пути.</summary>
public sealed class UserSettings
{
    public string OutputPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Frame Forge");
    public string Video2X { get; set; } = "";
    public string FFmpeg { get; set; } = "";
    public string FFprobe { get; set; } = "";
    public string LibraryPath { get; set; } = "";
    public int ProfileIndex { get; set; }
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public bool FullVideo { get; set; } = true;
    public bool KeepTemporary { get; set; }
    public int EncodingCrf { get; set; } = 22;
}

/// <summary>Хранит настройки и очередь для восстановления.</summary>
public sealed class WorkspaceState
{
    public int Version { get; set; } = 1;
    public UserSettings Settings { get; set; } = new();
    public List<QueueJob> Jobs { get; set; } = [];
}
