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

    /// <summary>Подменяет запуск обработчика заданным сценарием.</summary>
    private sealed class StubProcessor(
        Func<ProcessingRequest, Action<string>, Action<double>, CancellationToken, Task<string>> run) : IVideoProcessor
    {
        /// <summary>Выполняет сценарий теста.</summary>
        public Task<string> ProcessAsync(ProcessingRequest request, Action<string> log,
            Action<double> progress, CancellationToken token) => run(request, log, progress, token);
    }
}
