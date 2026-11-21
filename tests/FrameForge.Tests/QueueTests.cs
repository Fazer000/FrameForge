// Автоматические проверки Frame Forge.
using Xunit;

namespace FrameForge.Tests;

/// <summary>Проверяет выполнение очереди и её состояние на диске.</summary>
public sealed class QueueTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("forge-queue-").FullName;

    /// <summary>Проверяет продолжение очереди после ошибки и публикацию серии.</summary>
    [Fact]
    public async Task ContinuesAfterFailure()
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), """{"Status":"CompletedWithWarning"}""");
        var first = NewJob("bad");
        var second = NewJob("good", new(directory, "good", "Серия", "hash"));
        var handled = new List<QueueJob>();
        var published = new List<string>();
        var snapshots = new List<string[]>();
        var queue = new ProcessingQueue(new StubProcessor((request, _, progress, _) =>
        {
            if (request.Input == "bad") throw new IOException("Ошибка входного файла");
            progress(50);
            return Task.FromResult(directory);
        }), (request, _, _, _) => { published.Add(request.Input); return Task.CompletedTask; });
        QueueJob[] jobs = [first, second];

        await queue.RunAsync(jobs, () => snapshots.Add(jobs.Select(job => job.Status).ToArray()),
            handled.Add, _ => { }, (job, value) => job.Progress = value, CancellationToken.None);

        Assert.Equal(jobs, handled);
        Assert.Equal(["good"], published);
        Assert.Equal(["Обработка", "Ожидает"], snapshots[0]);
        Assert.Equal(["Ошибка", "С предупреждением"], snapshots[^1]);
        Assert.Equal("Ошибка входного файла", first.Error);
        Assert.Equal(100, second.Progress);
        Assert.Equal(directory, second.ResultPath);
    }

    /// <summary>Проверяет остановку до следующего задания.</summary>
    [Fact]
    public async Task CancelsCurrentJobOnly()
    {
        using var cancellation = new CancellationTokenSource();
        QueueJob[] jobs = [NewJob("first"), NewJob("next")];
        var queue = new ProcessingQueue(new StubProcessor((_, _, _, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<string>(token);
        }), (_, _, _, _) => throw new InvalidOperationException());

        await queue.RunAsync(jobs, () => { }, _ => { }, _ => { }, (_, _) => { }, cancellation.Token);

        Assert.Equal("Отменено", jobs[0].Status);
        Assert.Equal("Ожидает", jobs[1].Status);
    }

    /// <summary>Учитывает текущие задания и исключает историю очереди.</summary>
    [Fact]
    public void EstimatesCurrentRun()
    {
        var done = NewJob("done");
        done.Status = "Готово";
        var active = NewJob("active");
        active.Status = "Обработка";
        active.Progress = 50;
        var waiting = NewJob("waiting");
        var history = NewJob("history");
        history.Status = "Готово";
        QueueJob[] jobs = [active, waiting, history];

        var result = QueueRunProgress.Calculate([done, active], jobs, TimeSpan.FromMinutes(2));

        Assert.Equal(50, result.Percent);
        Assert.Equal(TimeSpan.FromMinutes(2), result.Remaining);
    }

    /// <summary>Проверяет прогресс до начала и после завершения заданий.</summary>
    [Fact]
    public void HandlesQueueBoundaries()
    {
        var job = NewJob("job");
        var elapsed = TimeSpan.FromMinutes(1);
        Assert.Equal(new QueueRunProgress(0, null), QueueRunProgress.Calculate([], [job], elapsed));

        job.Status = "Обработка";
        job.Progress = 100;
        Assert.Equal(99, QueueRunProgress.Calculate([job], [job], elapsed).Percent);

        job.Status = "Отменено";
        job.Progress = 50;
        Assert.Equal(50, QueueRunProgress.Calculate([job], [job], elapsed).Percent);

        job.Status = "Ошибка";
        Assert.Equal(new QueueRunProgress(100, TimeSpan.Zero), QueueRunProgress.Calculate([job], [job], elapsed));
    }

    /// <summary>Сохраняет полное число часов в длительности очереди.</summary>
    [Fact]
    public void FormatsLongDuration() =>
        Assert.Equal("25:01:02", QueueRunProgress.FormatDuration(new TimeSpan(1, 1, 1, 2)));

    /// <summary>Оставляет в строках Video2X кадры, проценты и FPS.</summary>
    [Theory]
    [InlineData("frame=2519/35450 (7.11%); fps=17.99; elapsed=00:02:20; remaining=00:30:30", "frame=2519/35450 (7.11%); fps=17.99")]
    [InlineData("frame=0/35450 (0.00%); fps=-nan(ind); elapsed=00:00:00; remaining=00:00:00", "frame=0/35450 (0.00%); fps=-nan(ind)")]
    [InlineData("Warning; elapsed=00:02:20", "Warning; elapsed=00:02:20")]
    public void ShortensFrameLog(string line, string expected) =>
        Assert.Equal(expected, Video2XProcessor.FormatProgressLog(line));


    /// <summary>Удаляет файлы отменённой попытки после повторной загрузки очереди.</summary>
    [Fact]
    public async Task RemovesCancelledArtifacts()
    {
        const string attempt = "20261121-120000-a841a751";
        var series = new LibraryVideo(directory, "episode.bin", "Серия", "hash");
        var job = NewJob(series.SourcePath, series);
        job.Request = job.Request with { OutputRoot = directory };
        var resultPath = Directory.CreateDirectory(Path.Combine(directory, attempt)).FullName;
        var pending = series.SourcePath + $".frameforge-pending-{attempt}.mp4";
        var artifacts = new[] { Path.Combine(resultPath, "enhanced.partial.mkv"), Path.Combine(resultPath, "report.json"), pending };
        foreach (var file in artifacts) File.WriteAllText(file, "temporary");
        File.WriteAllText(series.SourcePath, "source");
        File.WriteAllText(series.BackupPath, "backup");
        var otherResult = Path.Combine(directory, "enhanced.mkv");
        File.WriteAllText(otherResult, "other result");

        var store = new WorkspaceStore(Path.Combine(directory, "state"));
        var state = new WorkspaceState { Jobs = [job] };
        var queue = new ProcessingQueue(new DirectoryReportingProcessor(resultPath),
            (_, _, _, _) => throw new InvalidOperationException());
        await queue.RunAsync(state.Jobs, () => store.Save(state), _ => { }, _ => { }, (_, _) => { }, CancellationToken.None);
        var saved = Assert.Single(store.Load().Jobs);
        Assert.Equal("Отменено", saved.Status);
        Assert.Equal(resultPath, saved.ResultPath);
        Assert.Equal(resultPath, Assert.Single(saved.CancelledResultPaths));

        CancelledJobCleanup.DeleteArtifacts(saved);
        CancelledJobCleanup.DeleteArtifacts(saved);

        Assert.False(Directory.Exists(resultPath));
        Assert.All(artifacts, file => Assert.False(File.Exists(file)));
        Assert.Equal("source", File.ReadAllText(series.SourcePath));
        Assert.Equal("backup", File.ReadAllText(series.BackupPath));
        Assert.Equal("other result", File.ReadAllText(otherResult));
    }

    /// <summary>Сохраняет результат успешного повтора при очистке отменённой попытки.</summary>
    [Fact]
    public void KeepsSuccessfulRetryResult()
    {
        var past = Directory.CreateDirectory(Path.Combine(directory, "20261121-120000-a841a751")).FullName;
        var current = Directory.CreateDirectory(Path.Combine(directory, "20261121-120001-a841a752")).FullName;
        var currentVideo = Path.Combine(current, "enhanced.mkv");
        File.WriteAllText(currentVideo, "final");
        var job = NewJob("input.mkv");
        job.Request = job.Request with { OutputRoot = directory };
        job.CancelledResultPaths.Add(past);
        job.ResultPath = current;
        job.Status = "Готово";

        CancelledJobCleanup.DeleteArtifacts(job);

        Assert.False(Directory.Exists(past));
        Assert.Equal("final", File.ReadAllText(currentVideo));
    }

    /// <summary>Проверяет восстановление очереди и запасной файл состояния.</summary>
    [Fact]
    public void LoadsWorkspace()
    {
        var store = new WorkspaceStore(directory);
        Assert.Empty(store.Load().Jobs);
        var job = NewJob("input.mkv");
        job.Status = "Обработка";
        job.Progress = 70;
        var state = new WorkspaceState { Jobs = [job] };
        store.Save(state);
        store.Save(state);
        File.WriteAllText(Path.Combine(directory, "workspace.json"), "{");

        var saved = Assert.Single(store.Load().Jobs);
        Assert.Equal("Прервано", saved.Status);
        Assert.Equal(0, saved.Progress);
        Assert.Equal(job.Request, saved.Request);
        File.WriteAllText(Path.Combine(directory, "workspace.json.bak"), "{");
        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    /// <summary>Создаёт задание с заданным источником.</summary>
    private static QueueJob NewJob(string input, LibraryVideo? library = null) => new()
    {
        Request = new(input, "", "", "", "", ProcessingProfile.All[0], 1920, 1080, 0, 10, Library: library)
    };

    /// <summary>Удаляет временное состояние теста.</summary>
    public void Dispose() => Directory.Delete(directory, recursive: true);

    /// <summary>Сообщает о папке попытки и отменяет обработку.</summary>
    private sealed class DirectoryReportingProcessor(string path) : IVideoProcessor
    {
        /// <summary>Имитирует отмену после создания папки.</summary>
        public Task<string> ProcessAsync(ProcessingRequest request, Action<string> log, Action<double> progress,
            CancellationToken token, Action<string>? resultDirectoryCreated = null)
        {
            resultDirectoryCreated?.Invoke(path);
            return Task.FromCanceled<string>(new CancellationToken(canceled: true));
        }
    }

    /// <summary>Подменяет запуск обработчика заданным сценарием.</summary>
    private sealed class StubProcessor(
        Func<ProcessingRequest, Action<string>, Action<double>, CancellationToken, Task<string>> run) : IVideoProcessor
    {
        /// <summary>Выполняет сценарий теста.</summary>
        public Task<string> ProcessAsync(ProcessingRequest request, Action<string> log,
            Action<double> progress, CancellationToken token, Action<string>? resultDirectoryCreated = null) => run(request, log, progress, token);
    }
}
