using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FrameForge;

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
}

public sealed class QueueJob : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required ProcessingRequest Request { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    private string status = "Ожидает";
    public string Status { get => status; set { status = value; Changed(); } }
    private double progress;
    public double Progress { get => progress; set { progress = value; Changed(); } }
    private string resultPath = "";
    public string ResultPath { get => resultPath; set { resultPath = value; Changed(); } }
    public string Error { get; set; } = "";
    public string Name => Path.GetFileName(Request.Input);
    public string Profile => Request.Profile.Name;
    public string Target => $"{Request.TargetWidth}×{Request.TargetHeight}";
    public string Range => Request.FullVideo ? "Всё видео" : $"{Request.Start:g}–{Request.Start + Request.Duration:g} с";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class WorkspaceState
{
    public int Version { get; set; } = 1;
    public UserSettings Settings { get; set; } = new();
    public List<QueueJob> Jobs { get; set; } = [];
}

public sealed class WorkspaceStore(string directory)
{
    public string DirectoryPath => directory;
    private string StatePath => Path.Combine(directory, "workspace.json");
    public WorkspaceState Load()
    {
        if (!File.Exists(StatePath)) return new();
        WorkspaceState? state = null;
        Exception? failure = null;
        foreach (var path in new[] { StatePath, StatePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try { state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(path)); if (state != null) break; }
            catch (Exception ex) when (ex is IOException or JsonException) { failure = ex; }
        }
        if (state == null) throw new InvalidDataException("Не удалось прочитать очередь. Файлы сохранены: " + StatePath, failure);
        if (state.Version != 1 || state.Settings == null || state.Jobs == null) throw new InvalidDataException("Неизвестный формат очереди.");
        foreach (var job in state.Jobs.Where(j => j.Status == "Обработка"))
        { job.Status = "Прервано"; job.Progress = 0; job.Error = "Приложение завершилось во время задания. Можно повторить с начала."; }
        return state;
    }
    public void Save(WorkspaceState state)
    {
        Directory.CreateDirectory(directory);
        var temporary = StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(StatePath)) File.Replace(temporary, StatePath, StatePath + ".bak");
        else File.Move(temporary, StatePath);
    }
}
