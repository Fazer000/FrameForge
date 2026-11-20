// Читает и атомарно сохраняет рабочее состояние.
using System.Text.Json;

namespace FrameForge;

/// <summary>Читает и атомарно сохраняет рабочее состояние.</summary>
public sealed class WorkspaceStore(string directory)
{
    public string DirectoryPath => directory;
    private string StatePath => Path.Combine(directory, "workspace.json");
    /// <summary>Загружает очередь и отмечает прерванные задания.</summary>
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
    /// <summary>Сохраняет настройки и задания окна.</summary>
    public void Save(WorkspaceState state)
    {
        Directory.CreateDirectory(directory);
        var temporary = StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(StatePath)) File.Replace(temporary, StatePath, StatePath + ".bak");
        else File.Move(temporary, StatePath);
    }
}
