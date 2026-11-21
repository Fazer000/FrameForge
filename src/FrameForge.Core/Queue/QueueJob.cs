// Хранит состояние задания очереди.
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrameForge;

/// <summary>Хранит состояние задания очереди.</summary>
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
    public List<string> CancelledResultPaths { get; init; } = [];
    public string Error { get; set; } = "";
    public string Name => Path.GetFileName(Request.Input);
    public string Profile => Request.Profile.Name;
    public string Target => $"{Request.TargetWidth}×{Request.TargetHeight}";
    public string Range => Request.FullVideo ? "Всё видео" : $"{Request.Start:g}–{Request.Start + Request.Duration:g} с";
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Уведомляет интерфейс об изменении свойства.</summary>
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
