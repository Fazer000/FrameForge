// Параметры или операции обработки видео.
namespace FrameForge;

/// <summary>Последовательно выполняет ожидающие задания.</summary>
public sealed class ProcessingQueue(
    IVideoProcessor processor,
    Func<ProcessingRequest, string, Action<string>, CancellationToken, Task> publish)
{
    /// <summary>Выполняет очередь и сохраняет состояние после каждого задания.</summary>
    public async Task RunAsync(
        IEnumerable<QueueJob> jobs, Action save, Action<QueueJob> started,
        Action<string> log, Action<QueueJob, double> progress, CancellationToken token)
    {
        while (!token.IsCancellationRequested && jobs.FirstOrDefault(job => job.Status == "Ожидает") is { } job)
        {
            job.Status = "Обработка";
            job.Progress = 0;
            job.Error = "";
            job.ResultPath = "";
            save();
            started(job);

            try
            {
                job.ResultPath = await processor.ProcessAsync(job.Request, log,
                    value => progress(job, value), token, directory =>
                    {
                        job.ResultPath = directory;
                        save();
                    });
                if (job.Request.Library != null)
                    await publish(job.Request, job.ResultPath, log, token);

                using var report = System.Text.Json.JsonDocument.Parse(
                    await File.ReadAllTextAsync(Path.Combine(job.ResultPath, "report.json")));
                job.Status = report.RootElement.GetProperty("Status").GetString() == "CompletedWithWarning"
                    ? "С предупреждением" : "Готово";
                job.Progress = 100;
            }
            catch (OperationCanceledException)
            {
                if (job.ResultPath.Length > 0)
                    job.CancelledResultPaths.Add(job.ResultPath);
                job.Status = "Отменено";
                job.Error = "Отменено пользователем";
            }
            catch (Exception ex)
            {
                job.Status = "Ошибка";
                job.Error = ex.Message;
                log(ex.Message);
            }
            save();
        }
    }
}
