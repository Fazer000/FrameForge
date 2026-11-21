// Читает каталог и заменяет файлы серий.
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FrameForge;

/// <summary>Описывает файл серии AnimeLib.</summary>
public sealed record LibraryVideo(string DirectoryPath, string FileName, string Title, string OriginalHash)
{
    public string SourcePath => Path.Combine(DirectoryPath, FileName);
    public string MarkerPath => SourcePath + ".frameforge.json";
    public string BackupPath => SourcePath + ".frameforge-original";
    /// <summary>Возвращает название серии.</summary>
    public override string ToString() => Title;
}

/// <summary>Хранит метаданные замены серии.</summary>
public sealed record EnhancementMarker(int Version, string State, string SourceFileName, string OriginalHash,
    string EnhancedHash, long EnhancedLength, string Profile, DateTimeOffset CompletedAt, int Width = 0, int Height = 0);

/// <summary>Читает каталог и заменяет файлы серий.</summary>
public static class LibraryIntegration
{
    /// <summary>Вычисляет SHA256 файла.</summary>
    public static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

    /// <summary>Возвращает доступные локальные серии.</summary>
    public static async Task<List<LibraryVideo>> ReadCatalogAsync(string directory, string? probe = null)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "index.json")));
        var result = new List<LibraryVideo>();
        foreach (var anime in doc.RootElement.GetProperty("anime").EnumerateArray())
            foreach (var episode in anime.GetProperty("episodes").EnumerateArray())
            {
                var name = episode.GetProperty("fileName").GetString()!;
                if (Path.GetFileName(name) != name || !File.Exists(Path.Combine(directory, name))) continue;
                if (episode.TryGetProperty("playlistFileName", out var playlist) && !string.IsNullOrEmpty(playlist.GetString())) continue;
                var quality = episode.GetProperty("quality").GetString();
                var video = new LibraryVideo(Path.GetFullPath(directory), name, "", "");
                var marker = ReadCompletedMarker(video);
                if (marker != null)
                {
                    var height = marker.Height > 0 ? marker.Height : await ReadLegacyHeightAsync(video, probe);
                    quality = height is > 0 ? $"{height}p (FF)" : "Улучшено (FF)";
                }
                var title = $"{anime.GetProperty("title").GetString()} · серия {episode.GetProperty("episodeNumber").GetString()} · {quality}";
                result.Add(video with { Title = title });
            }
        return result;
    }

    /// <summary>Читает отметку успешной замены текущего файла.</summary>
    private static EnhancementMarker? ReadCompletedMarker(LibraryVideo video)
    {
        if (!File.Exists(video.MarkerPath)) return null;
        try
        {
            var marker = JsonSerializer.Deserialize<EnhancementMarker>(File.ReadAllText(video.MarkerPath));
            return marker is { Version: 1, State: "Enhanced", EnhancedLength: > 0 } &&
                marker.SourceFileName == video.FileName &&
                marker.EnhancedHash is { Length: 64 } &&
                marker.EnhancedHash.All(Uri.IsHexDigit) &&
                marker.EnhancedLength == new FileInfo(video.SourcePath).Length
                ? marker : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Получает высоту кадра для метаданных прежней версии.</summary>
    private static async Task<int?> ReadLegacyHeightAsync(LibraryVideo video, string? probe)
    {
        if (!File.Exists(probe)) return null;
        try
        {
            return (await VideoProbe.ReadAsync(probe!, video.SourcePath, CancellationToken.None)).Height;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Атомарно записывает метаданные замены.</summary>
    private static void WriteMarker(LibraryVideo video, EnhancementMarker marker)
    {
        var temporary = video.MarkerPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, video.MarkerPath, overwrite: true);
    }

    /// <summary>Возвращает путь к временному MP4 попытки.</summary>
    internal static string GetPendingPath(LibraryVideo video, string resultDirectory) =>
        video.SourcePath + $".frameforge-pending-{Path.GetFileName(Path.TrimEndingDirectorySeparator(resultDirectory))}.mp4";

    /// <summary>Проверяет и публикует улучшенную серию.</summary>
    public static async Task PublishAsync(ProcessingRequest request, string resultDirectory, Action<string> log, CancellationToken token)
    {
        var video = request.Library ?? throw new ArgumentException("Нет привязки к библиотеке.");
        if (!request.FullVideo) throw new InvalidOperationException("В библиотеку можно записывать только полное видео.");
        if (File.Exists(video.BackupPath)) throw new InvalidOperationException("Осталась временная копия от предыдущей операции. Проверьте её перед повторной заменой.");
        if (Hash(video.SourcePath) != video.OriginalHash) throw new InvalidOperationException("Исходник библиотеки изменился после добавления задания. Замена отменена.");
        var temporary = GetPendingPath(video, resultDirectory);
        var stagedResult = Path.GetFullPath(Path.Combine(resultDirectory, "enhanced.mkv"));
        if (string.Equals(stagedResult, Path.GetFullPath(video.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Рабочий файл совпадает с файлом серии.");
        log("Подготовка совместимого с AnimeLib MP4. Оригинал ещё не изменён.");
        await ProcessRunner.RunAsync(request.FFmpeg,
            ["-nostdin", "-n", "-v", "warning", "-i", stagedResult,
             "-map", "0:v:0", "-map", "0:a?", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", temporary], log, token);
        await ProcessRunner.RunAsync(request.FFmpeg,
            ["-nostdin", "-v", "error", "-xerror", "-i", temporary, "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"], log, token);
        var enhanced = await VideoProbe.ReadAsync(request.FFprobe, temporary, token);
        var original = await VideoProbe.ReadAsync(request.FFprobe, video.SourcePath, token);
        if (enhanced.Codec != "h264" || Math.Abs(enhanced.Duration - original.Duration) > 1)
            throw new InvalidDataException("MP4 для библиотеки не прошёл проверку.");
        var enhancedHash = Hash(temporary);
        var marker = new EnhancementMarker(1, "Prepared", video.FileName, video.OriginalHash,
            enhancedHash, new FileInfo(temporary).Length, request.Profile.Name, DateTimeOffset.Now, enhanced.Width, enhanced.Height);
        using (var exclusive = new FileStream(video.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
        {
            if (Convert.ToHexString(SHA256.HashData(exclusive)) != video.OriginalHash)
                throw new IOException("Исходник изменился во время обработки.");
            token.ThrowIfCancellationRequested();
            WriteMarker(video, marker);
            File.Replace(temporary, video.SourcePath, video.BackupPath);
        }
        log("Проверка записанного файла серии перед удалением временного оригинала…");
        if (Hash(video.SourcePath) != enhancedHash || Hash(video.BackupPath) != video.OriginalHash)
            throw new IOException("Файлы после замены не прошли проверку SHA256. Временный оригинал сохранён: " + video.BackupPath);
        WriteMarker(video, marker with { State = "Enhanced" });
        try { File.Delete(video.BackupPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new IOException("Серия улучшена, но временный оригинал не удалось удалить: " + video.BackupPath, ex); }
        log("Серия заменена и проверена. Временный оригинал удалён.");

        if (request.KeepTemporaryFiles) return;
        try { File.Delete(stagedResult); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"Не удалось удалить рабочий файл {stagedResult}: {ex.Message}");
        }
    }

    /// <summary>Восстанавливает оригинал после сбоя замены.</summary>
    public static void Restore(LibraryVideo video)
    {
        var marker = JsonSerializer.Deserialize<EnhancementMarker>(File.ReadAllText(video.MarkerPath))
            ?? throw new InvalidDataException("Повреждена информация об улучшении.");
        if (marker.Version != 1 || marker.SourceFileName != video.FileName || Hash(video.BackupPath) != marker.OriginalHash)
            throw new InvalidDataException("Резервная копия не прошла проверку SHA256.");
        if (Hash(video.SourcePath) != marker.EnhancedHash) throw new IOException("Текущий файл серии изменён. Автоматическое восстановление отменено.");
        using var exclusive = new FileStream(video.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Delete);
        File.Replace(video.BackupPath, video.SourcePath, null);
        WriteMarker(video, marker with { State = "Restored" });
    }
}
