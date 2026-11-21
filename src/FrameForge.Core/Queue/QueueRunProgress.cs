// Рассчитывает прогресс и оставшееся время очереди.
namespace FrameForge;

/// <summary>Содержит показатели текущего запуска очереди.</summary>
public readonly record struct QueueRunProgress(double Percent, TimeSpan? Remaining)
{
    /// <summary>Оценивает остаток времени по среднему темпу заданий.</summary>
    public static QueueRunProgress Calculate(IEnumerable<QueueJob> started, IEnumerable<QueueJob> jobs, TimeSpan elapsed)
    {
        var batch = started.Union(jobs.Where(job => job.Status == "Ожидает")).ToArray();
        if (batch.Length == 0) return new(0, null);

        var percent = batch.Average(job => job.Status switch
        {
            "Готово" or "С предупреждением" or "Ошибка" => 100,
            "Обработка" or "Отменено" => Math.Clamp(job.Progress, 0, 99),
            _ => 0
        });
        if (percent >= 100) return new(100, TimeSpan.Zero);
        if (percent <= 0 || elapsed <= TimeSpan.Zero) return new(percent, null);

        var seconds = elapsed.TotalSeconds * (100 - percent) / percent;
        return new(percent, seconds < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(seconds) : null);
    }

    /// <summary>Форматирует длительность без сброса часов после суток.</summary>
    public static string FormatDuration(TimeSpan duration) =>
        $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
}
