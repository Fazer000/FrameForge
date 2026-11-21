// Удаляет рабочие файлы отменённых попыток.
namespace FrameForge;

/// <summary>Очищает файлы, созданные отменёнными попытками задания.</summary>
public static class CancelledJobCleanup
{
    private static readonly string[] ArtifactNames =
        ["reference.mkv", "model-output.mkv", "enhanced.partial.mkv", "enhanced.mkv", "processing.log", "report.json"];

    /// <summary>Удаляет файлы отменённых попыток и пустые папки результатов.</summary>
    public static void DeleteArtifacts(QueueJob job)
    {
        IEnumerable<string> directories = job.CancelledResultPaths;
        if (job.Status == "Отменено" && job.ResultPath.Length > 0)
            directories = directories.Append(job.ResultPath);

        foreach (var path in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.Request.OutputRoot));
            if (!string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase) ||
                !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(directory), @"^\d{8}-\d{6}-[0-9a-f]{8}$") ||
                string.Equals(Path.GetDirectoryName(Path.GetFullPath(job.Request.Input)), directory, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Не удалось подтвердить папку отменённой попытки: {directory}");

            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Папка отменённой попытки перенаправлена: {directory}");

            if (job.Request.Library is { } video && Directory.Exists(video.DirectoryPath))
                File.Delete(LibraryIntegration.GetPendingPath(video, directory));

            if (!Directory.Exists(directory)) continue;

            foreach (var name in ArtifactNames)
                File.Delete(Path.Combine(directory, name));
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }
}
