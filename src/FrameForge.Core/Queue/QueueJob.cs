// Хранит состояние задания очереди.
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FrameForge;

/// <summary>Хранит состояние задания очереди.</summary>
public sealed class QueueJob : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required ProcessingRequest Request
    {
        get;
        set
        {
            field = value;
            Changed();
            Changed(nameof(Name));
            Changed(nameof(Profile));
            Changed(nameof(Target));
            Changed(nameof(EncodingQuality));
            Changed(nameof(Range));
        }
    }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    private string status = "Ожидает";
    public string Status
    {
        get => status;
        set
        {
            status = value;
            Changed();
            Changed(nameof(CanEditSettings));
        }
    }

    /// <summary>Разрешает менять параметры до запуска или перед повтором.</summary>
    [JsonIgnore]
    public bool CanEditSettings => Status is "Ожидает" or "Ошибка" or "Отменено" or "Прервано";
    private double progress;
    public double Progress { get => progress; set { progress = value; Changed(); } }
    private string resultPath = "";
    public string ResultPath { get => resultPath; set { resultPath = value; Changed(); } }
    public string Error { get; set; } = "";
    public string Name => Path.GetFileName(Request.Input);
    public string Profile => Request.Profile.Name;
    public string Target => $"{Request.TargetWidth}×{Request.TargetHeight}";
    [JsonIgnore]
    public string EncodingQuality => $"CRF {Request.EncodingCrf}";
    public string Range => Request.FullVideo ? "Всё видео" : $"{Request.Start:g}–{Request.Start + Request.Duration:g} с";
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Уведомляет интерфейс об изменении свойства.</summary>
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
