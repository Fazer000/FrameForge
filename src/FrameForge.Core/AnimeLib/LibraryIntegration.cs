// Читает каталог и заменяет файлы серий.
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FrameForge;

/// <summary>Описывает файл серии AnimeLib.</summary>
public sealed record LibraryVideo(string DirectoryPath, string FileName, string Title, string OriginalHash)
{
    public string SourcePath => Path.Combine(DirectoryPath, FileName);
    public string ServiceDirectory => Path.Combine(DirectoryPath, ".frameforge");
    public string WorkRoot => Path.Combine(ServiceDirectory, "jobs");
    public string MarkerPath => Path.Combine(ServiceDirectory, FileName + ".frameforge.json");
    public string BackupPath => Path.Combine(ServiceDirectory, FileName + ".frameforge-original");
    internal string LegacyMarkerPath => SourcePath + ".frameforge.json";
    private string LegacyBackupPath => SourcePath + ".frameforge-original";
    public string? ExistingBackupPath => File.Exists(BackupPath) ? BackupPath :
        File.Exists(LegacyBackupPath) ? LegacyBackupPath : null;
    internal string ReadableMarkerPath => File.Exists(MarkerPath) ? MarkerPath : LegacyMarkerPath;
    /// <summary>Возвращает название серии.</summary>
    public override string ToString() => Title;
}

/// <summary>Хранит метаданные замены серии.</summary>
public sealed record EnhancementMarker(int Version, string State, string SourceFileName, string OriginalHash,
    string EnhancedHash, long EnhancedLength, string Profile, DateTimeOffset CompletedAt, int Width = 0, int Height = 0);

/// <summary>Сообщает, почему проверенный MKV остался вне библиотеки.</summary>
public sealed class LibraryPublicationSkippedException(string message) : InvalidOperationException(message);

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

    /// <summary>Находит отдельные ASS и VTT, зарегистрированные для файла серии.</summary>
    internal static string[] ReadExternalSubtitles(string input)
    {
        var fullPath = Path.GetFullPath(input);
        var directory = Path.GetDirectoryName(fullPath)!;
        var index = Path.Combine(directory, "index.json");
        if (!File.Exists(index)) return [];

        JsonDocument indexDocument;
        try { indexDocument = JsonDocument.Parse(File.ReadAllText(index)); }
        catch (JsonException) { return []; }

        using var doc = indexDocument;
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("anime", out var anime) || anime.ValueKind != JsonValueKind.Array)
            return [];

        foreach (var title in anime.EnumerateArray())
        {
            if (title.ValueKind != JsonValueKind.Object || !title.TryGetProperty("episodes", out var episodes) || episodes.ValueKind != JsonValueKind.Array) continue;
            foreach (var episode in episodes.EnumerateArray())
            {
                if (!episode.TryGetProperty("fileName", out var name) ||
                    !string.Equals(name.GetString(), Path.GetFileName(fullPath), StringComparison.OrdinalIgnoreCase) ||
                    !episode.TryGetProperty("subtitles", out var subtitles) || subtitles.ValueKind != JsonValueKind.Array)
                    continue;

                var result = new List<string>();
                foreach (var subtitle in subtitles.EnumerateArray())
                {
                    var file = subtitle.GetProperty("fileName").GetString() ?? "";
                    if (Path.GetFileName(file) != file ||
                        Path.GetExtension(file).ToLowerInvariant() is not (".ass" or ".vtt" or ".srt"))
                        continue;
                    var path = Path.Combine(directory, file);
                    if (!File.Exists(path)) throw new FileNotFoundException("Файл субтитров серии не найден.", path);
                    result.Add(path);
                }
                return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        return [];
    }

    /// <summary>Читает отметку успешной замены текущего файла.</summary>
    private static EnhancementMarker? ReadCompletedMarker(LibraryVideo video)
    {
        var path = video.ReadableMarkerPath;
        if (!File.Exists(path)) return null;
        try
        {
            var marker = JsonSerializer.Deserialize<EnhancementMarker>(File.ReadAllText(path));
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
        Directory.CreateDirectory(video.ServiceDirectory);
        var temporary = video.MarkerPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, video.MarkerPath, overwrite: true);
    }

    /// <summary>Проверяет и публикует улучшенную серию.</summary>
    public static async Task PublishAsync(ProcessingRequest request, string resultDirectory, Action<string> log, CancellationToken token)
    {
        var video = request.Library ?? throw new ArgumentException("Нет привязки к библиотеке.");
        if (!request.FullVideo) throw new InvalidOperationException("В библиотеку можно записывать только полное видео.");
        if (video.ExistingBackupPath != null) throw new InvalidOperationException("Осталась временная копия от предыдущей операции. Проверьте её перед повторной заменой.");
        if (Hash(video.SourcePath) != video.OriginalHash) throw new InvalidOperationException("Исходник библиотеки изменился после добавления задания. Замена отменена.");
        var temporary = Path.Combine(resultDirectory, "publish.mp4");
        var stagedResult = Path.GetFullPath(Path.Combine(resultDirectory, "enhanced.mkv"));
        if (string.Equals(stagedResult, Path.GetFullPath(video.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Рабочий файл совпадает с файлом серии.");
        var sourceStreams = await VideoProbe.ReadPreservedStreamsAsync(request.FFprobe, video.SourcePath, token);
        if (sourceStreams.Any(stream => stream is not { Type: "audio", Codec: "aac" }))
            throw new LibraryPublicationSkippedException(
                "Серия оставлена в AnimeLib: совместимый MP4 не сохраняет все её дорожки. Улучшенный MKV доступен в результатах.");

        log("Подготовка совместимого с AnimeLib MP4. Оригинал ещё не изменён.");
        await ProcessRunner.RunAsync(request.FFmpeg,
            ["-nostdin", "-n", "-v", "warning", "-i", stagedResult, "-i", video.SourcePath,
             "-map", "0:v:0", "-map", "1:a?", "-map_metadata", "1", "-map_chapters", "1",
             "-c", "copy", "-movflags", "+faststart", temporary], log, token);
        var publishedStreams = await VideoProbe.ReadPreservedStreamsAsync(request.FFprobe, temporary, token);
        if (!sourceStreams.SequenceEqual(publishedStreams))
            throw new LibraryPublicationSkippedException(
                "Серия оставлена в AnimeLib: MP4 не прошёл проверку дорожек. Улучшенный MKV доступен в результатах.");
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
        string? installedHash = null;
        try
        {
            log("Проверка записанного файла серии перед удалением временного оригинала…");
            installedHash = Hash(video.SourcePath);
            if (installedHash != enhancedHash || Hash(video.BackupPath) != video.OriginalHash)
                throw new IOException("Файлы после замены не прошли проверку SHA256.");
            WriteMarker(video, marker with { State = "Enhanced" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                ReplaceWithOriginal(video, video.BackupPath, video.OriginalHash, installedHash ?? enhancedHash, temporary);
            }
            catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new IOException($"Не удалось вернуть оригинал автоматически. Копия оригинала: {video.BackupPath}",
                    new AggregateException(ex, restoreError));
            }

            try { WriteMarker(video, marker with { State = "Restored" }); }
            catch (Exception markerError) when (markerError is IOException or UnauthorizedAccessException)
            {
                log("Оригинал восстановлен. Не удалось обновить метку замены: " + markerError.Message);
            }

            throw new IOException("Финальная проверка не прошла. Оригинал возвращён в библиотеку AnimeLib.", ex);
        }
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
        var backup = video.ExistingBackupPath ?? throw new FileNotFoundException("Не найдена копия оригинала.");
        var marker = JsonSerializer.Deserialize<EnhancementMarker>(File.ReadAllText(video.ReadableMarkerPath))
            ?? throw new InvalidDataException("Повреждена информация об улучшении.");
        if (marker.Version != 1 || marker.SourceFileName != video.FileName)
            throw new InvalidDataException("Метка улучшения не соответствует серии.");

        ReplaceWithOriginal(video, backup, marker.OriginalHash, marker.EnhancedHash, null);
        WriteMarker(video, marker with { State = "Restored" });
    }

    /// <summary>Возвращает проверенный оригинал на место текущего файла серии.</summary>
    private static void ReplaceWithOriginal(LibraryVideo video, string backup, string originalHash,
        string currentHash, string? displacedFile)
    {
        if (Hash(backup) != originalHash)
            throw new InvalidDataException("Резервная копия не прошла проверку SHA256.");

        using var current = new FileStream(video.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Delete);
        if (Convert.ToHexString(SHA256.HashData(current)) != currentHash)
            throw new IOException("Текущий файл серии изменён. Восстановление остановлено.");

        if (displacedFile != null && File.Exists(displacedFile))
            throw new IOException("Место для сохранения заменяемого файла уже занято: " + displacedFile);
        File.Replace(backup, video.SourcePath, displacedFile);
    }
}
