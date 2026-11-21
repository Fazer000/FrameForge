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


    /// <summary>Разрешает редактирование только до запуска или перед повтором.</summary>
    [Theory]
    [InlineData("Ожидает", true)]
    [InlineData("Ошибка", true)]
    [InlineData("Отменено", true)]
    [InlineData("Прервано", true)]
    [InlineData("Обработка", false)]
    [InlineData("Готово", false)]
    [InlineData("С предупреждением", false)]
    public void ChecksJobSettingsAvailability(string status, bool expected)
    {
        var job = NewJob("input.mkv");
        var changed = new List<string?>();
        job.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        job.Status = status;

        Assert.Equal(expected, job.CanEditSettings);
        Assert.Contains(nameof(QueueJob.CanEditSettings), changed);
    }

    /// <summary>Обновляет строку очереди и передаёт сохранённые параметры обработчику.</summary>
    [Fact]
    public async Task RunsWithEditedJobSettings()
    {
        var job = NewJob("input.mkv");
        var other = NewJob("other.mkv");
        var original = job.Request;
        var unchanged = other.Request;
        var edited = original with
        {
            Profile = ProcessingProfile.All[4], TargetWidth = 2560, TargetHeight = 1440,
            EncodingCrf = 20, Start = 5, Duration = 30, KeepTemporaryFiles = false
        };
        var changed = new List<string?>();
        job.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        job.Request = edited;

        Assert.Equal("2560×1440", job.Target);
        Assert.Equal("CRF 20", job.EncodingQuality);
        Assert.Equal("5–35 с", job.Range);
        Assert.Equal(edited.Profile.Name, job.Profile);
        Assert.Contains(nameof(QueueJob.Target), changed);
        Assert.Contains(nameof(QueueJob.EncodingQuality), changed);
        Assert.Contains(nameof(QueueJob.Range), changed);
        Assert.Contains(nameof(QueueJob.Profile), changed);

        var store = new WorkspaceStore(Path.Combine(directory, "state"));
        store.Save(new WorkspaceState { Jobs = [job, other] });
        var loaded = store.Load();
        Assert.Equal(job.Id, loaded.Jobs[0].Id);
        Assert.Equal(original.Input, loaded.Jobs[0].Request.Input);
        Assert.Equal(edited, loaded.Jobs[0].Request);
        Assert.Equal(unchanged, loaded.Jobs[1].Request);

        await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), """{"Status":"Completed"}""");
        var handled = new List<ProcessingRequest>();
        var queue = new ProcessingQueue(new StubProcessor((request, _, _, _) =>
        {
            handled.Add(request);
            return Task.FromResult(directory);
        }), (_, _, _, _) => Task.CompletedTask);
        await queue.RunAsync(loaded.Jobs, () => { }, _ => { }, _ => { }, (_, _) => { }, CancellationToken.None);
        Assert.Equal([edited, unchanged], handled);
    }

    /// <summary>Показывает CRF стандартных и ранее сохранённых настроек.</summary>
    [Theory]
    [InlineData(16, "CRF 16")]
    [InlineData(20, "CRF 20")]
    [InlineData(22, "CRF 22")]
    [InlineData(18, "CRF 18")]
    public void DisplaysJobEncodingQuality(int crf, string expected)
    {
        var job = NewJob("input.mkv");
        job.Request = job.Request with { EncodingCrf = crf };

        Assert.Equal(expected, job.EncodingQuality);
    }

    /// <summary>Сохраняет путь к рабочей папке после отмены.</summary>
    [Fact]
    public async Task PersistsCancelledResultPath()
    {
        var job = NewJob("input.mkv");
        var resultPath = Path.Combine(directory, "attempt");
        var store = new WorkspaceStore(Path.Combine(directory, "state"));
        var state = new WorkspaceState { Jobs = [job] };
        var queue = new ProcessingQueue(new DirectoryReportingProcessor(resultPath),
            (_, _, _, _) => throw new InvalidOperationException());

        await queue.RunAsync(state.Jobs, () => store.Save(state), _ => { }, _ => { }, (_, _) => { }, CancellationToken.None);

        var saved = Assert.Single(store.Load().Jobs);
        Assert.Equal("Отменено", saved.Status);
        Assert.Equal(resultPath, saved.ResultPath);
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
