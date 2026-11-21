// Обрабатывает видео через Video2X и FFmpeg.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FrameForge;

/// <summary>Обрабатывает видео через Video2X и FFmpeg.</summary>
public sealed class Video2XProcessor : IVideoProcessor
{
    /// <summary>Формирует аргументы запуска Video2X.</summary>
    public static string[] BuildArguments(ProcessingProfile profile, int scale, string input, string output, bool lossless = false, int crf = 16)
    {
        if (scale is not (2 or 3 or 4)) throw new ArgumentException("Масштаб должен быть 2, 3 или 4.");
        if (profile.Model == "models-pro" && scale == 4)
            throw new ArgumentException("Real-CUGAN Pro поддерживает только 2× и 3×.");
        if (profile.Model == "realesrgan-plus" && scale != 4)
            throw new ArgumentException("Real-ESRGAN Plus требует масштаб 4×.");
        if (profile.Processor == "realcugan" && profile.Noise == 1 && scale != 2)
            throw new ArgumentException("Real-CUGAN SE с очисткой уровня 1 поддерживает в прототипе только 2×.");
        // Video2X 6.4.0 отклоняет явное значение -1, хотя использует его по умолчанию.
        string[] noiseArgs = profile.Noise == -1 ? [] : ["-n", profile.Noise.ToString(CultureInfo.InvariantCulture)];
        string[] common = ["-i", input, "-o", output, "-p", profile.Processor,
            $"--{profile.Processor}-model", profile.Model, "-s", scale.ToString(),
            ..noiseArgs, "-d", "0", "-c", lossless ? "ffv1" : "libx264", "--pix-fmt", "yuv420p", "--no-copy-streams"];
        return lossless ? common : [.. common, "-e", $"crf={crf.ToString(CultureInfo.InvariantCulture)}", "-e", "preset=medium"];
    }

    /// <summary>Убирает время из строки прогресса Video2X.</summary>
    public static string FormatProgressLog(string line)
    {
        const string elapsedMarker = "; elapsed=";
        var timeIndex = line.IndexOf(elapsedMarker, StringComparison.Ordinal);
        return line.AsSpan().TrimStart().StartsWith("frame=", StringComparison.Ordinal) && timeIndex >= 0
            ? line[..timeIndex]
            : line;
    }

    /// <summary>Выполняет обработку и возвращает папку результата.</summary>
    public async Task<string> ProcessAsync(ProcessingRequest r, Action<string> log,
        Action<double> progress, CancellationToken token, Action<string>? resultDirectoryCreated = null)
    {
        foreach (var file in new[] { r.Input, r.Video2X, r.FFmpeg, r.FFprobe })
            if (!File.Exists(file)) throw new FileNotFoundException("Файл не найден", file);
        if (r.Library != null && LibraryIntegration.Hash(r.Input) != r.Library.OriginalHash)
            throw new IOException("Файл серии изменился. Восстановите оригинал или добавьте серию заново.");
        if (!r.FullVideo && (!double.IsFinite(r.Start) || !double.IsFinite(r.Duration) || r.Start < 0 || r.Duration <= 0 || r.Duration > 300))
            throw new ArgumentException("Укажите начало ≥ 0 и длительность от 0 до 300 секунд.");
        var source = await VideoProbe.ReadAsync(r.FFprobe, r.Input, token);
        var (sourceStreams, chapterStream) = await VideoProbe.ReadTrackInventoryAsync(r.FFprobe, r.Input, token);
        var externalSubtitles = LibraryIntegration.ReadExternalSubtitles(r.Input);
        var expectedDuration = r.FullVideo ? source.Duration : r.Duration;
        if (!double.IsFinite(expectedDuration) || expectedDuration <= 0) throw new ArgumentException("Не удалось определить длительность видео.");
        var plan = ResolutionPlan.Create(source.Width, source.Height, r.TargetWidth, r.TargetHeight, r.Profile);
        if (!r.FullVideo && source.Duration > 0 && r.Start + r.Duration > source.Duration + 0.1)
            throw new ArgumentException("Фрагмент выходит за длительность видео.");
        var directory = Path.Combine(Path.GetFullPath(r.Library?.WorkRoot ?? r.OutputRoot),
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(directory);
        resultDirectoryCreated?.Invoke(directory);
        var clip = r.FullVideo ? r.Input : Path.Combine(directory, "reference.mkv");
        var partial = Path.Combine(directory, "enhanced.partial.mkv");
        var intermediate = Path.Combine(directory, "model-output.mkv");
        var output = Path.Combine(directory, "enhanced.mkv");
        var timer = Stopwatch.StartNew();
        var report = Path.Combine(directory, "report.json");
        string? warning = null;
        using var journal = new StreamWriter(Path.Combine(directory, "processing.log"), append: false, Encoding.UTF8) { AutoFlush = true };
        var notify = log;
        log = line => { lock (journal) journal.WriteLine($"{DateTimeOffset.Now:O} {line}"); notify(line); };
        try
        {
            if (!r.FullVideo)
            {
                log("Подготовка эталонного фрагмента без потерь (FFV1)…");
                await ProcessRunner.RunAsync(r.FFmpeg,
                    ["-nostdin", "-n", "-v", "warning", "-i", r.Input, "-ss", r.Start.ToString(CultureInfo.InvariantCulture),
                 "-t", r.Duration.ToString(CultureInfo.InvariantCulture), "-map", "0:v:0",
                 "-c:v", "ffv1", "-level", "3", clip], log, token);
            }
            else log("Обработка полного видео. Исходный файл читается напрямую.");
            log($"Обработка: {r.Profile.Name}. {source.Width}×{source.Height} → модель {plan.ModelScale}× → {plan.Width}×{plan.Height}. GPU 0.\nКаталог: {directory}");
            var args = BuildArguments(r.Profile, plan.ModelScale, clip, intermediate, plan.NeedsResize, r.EncodingCrf);
            try
            {
                await ProcessRunner.RunAsync(r.Video2X, args, line =>
                {
                    log(FormatProgressLog(line));
                    var match = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*%");
                    if (match.Success && double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var p))
                        progress(Math.Clamp(p, 0, 100) * 0.85);
                }, token);
            }
            catch (ExternalProcessException ex) when (ex.ExitCode == unchecked((int)0xC0000005) &&
                ex.Message.Contains("Video processed successfully", StringComparison.Ordinal) && File.Exists(intermediate))
            {
                warning = "Video2X завершился с 0xC0000005 после сообщения об успехе. Результат требует проверки; это сбой обработчика.";
                log("ПРЕДУПРЕЖДЕНИЕ: " + warning);
            }
            if (plan.NeedsResize)
                log($"Приведение к {plan.Width}×{plan.Height}, Lanczos. Промежуточный файл без потерь.");

            log("Сборка MKV с исходными дорожками без перекодирования.");
            string[] seek = r.FullVideo ? [] : ["-ss", r.Start.ToString(CultureInfo.InvariantCulture)];
            var clipLength = r.Duration.ToString(CultureInfo.InvariantCulture);
            string[] resize = plan.NeedsResize ?
                ["-filter:v:0", $"scale={plan.Width}:{plan.Height}:flags=lanczos,setsar=1",
                 "-c:v:0", "libx264", "-crf:v:0", r.EncodingCrf.ToString(CultureInfo.InvariantCulture),
                 "-preset:v:0", "medium", "-pix_fmt:v:0", "yuv420p"] : [];
            string[] chapterMapping = chapterStream is int chapterIndex ? ["-map", $"-1:{chapterIndex}"] : [];
            var subtitleInputs = externalSubtitles.SelectMany(path => seek.Concat(["-i", path])).ToArray();
            var subtitleMapping = externalSubtitles.SelectMany((_, index) => new[] { "-map", $"{index + 2}:s:0" }).ToArray();
            var expectedStreams = new List<PreservedStream>(sourceStreams);
            foreach (var subtitle in externalSubtitles)
                expectedStreams.AddRange(await VideoProbe.ReadPreservedStreamsAsync(r.FFprobe, subtitle, token));

            string[] clipTiming = r.FullVideo ? [] :
                ["-t", clipLength, "-copypriorss", "0", "-avoid_negative_ts", "disabled"];
            if (!r.FullVideo && expectedStreams.Any(stream => stream.Type == "subtitle"))
                clipTiming = [..clipTiming, "-bsf:s", $@"setts=duration=min(DURATION\,max(0\,{clipLength}/TB-PTS))"];

            await ProcessRunner.RunAsync(r.FFmpeg,
                ["-nostdin", "-n", "-v", "warning", "-xerror", "-i", intermediate, ..seek, "-i", r.Input, ..subtitleInputs,
                 "-map", "0:v:0", "-map", "1", "-map", "-1:v:0", ..chapterMapping, ..subtitleMapping, "-map_metadata", "1",
                 "-map_chapters", r.FullVideo ? "1" : "-1", "-c", "copy", ..resize, ..clipTiming, partial], log, token);

            var preserved = await VideoProbe.ReadPreservedStreamsAsync(r.FFprobe, partial, token);
            if (!expectedStreams.GroupBy(stream => stream.Type).OrderBy(group => group.Key).SelectMany(group => group)
                .SequenceEqual(preserved.GroupBy(stream => stream.Type).OrderBy(group => group.Key).SelectMany(group => group)))
                throw new InvalidDataException("Результат не сохранил все исходные дорожки или их оформление.");
            progress(95);
            log("Проверка: полное декодирование видео и аудио…");
            await ProcessRunner.RunAsync(r.FFmpeg,
                ["-nostdin", "-v", "error", "-xerror", "-i", partial, "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"], log, token);
            var result = await VideoProbe.ReadAsync(r.FFprobe, partial, token);
            if (result.Width != plan.Width || result.Height != plan.Height ||
                result.Duration <= 0 || Math.Abs(result.Duration - expectedDuration) > 1 || result.FrameRate != source.FrameRate)
                throw new InvalidOperationException(
                    $"Результат не прошёл проверку: получено {result.Width}×{result.Height}, {result.Duration:0.###} с, {result.FrameRate} кадр/с. " +
                    $"Ожидалось {plan.Width}×{plan.Height}, {expectedDuration:0.###} с, {source.FrameRate} кадр/с.");
            token.ThrowIfCancellationRequested();
            File.Move(partial, output, overwrite: false);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
            {
                Status = warning == null ? "Completed" : "CompletedWithWarning",
                Warning = warning,
                Request = r,
                Plan = plan,
                Source = source,
                Result = result,
                ElapsedSeconds = timer.Elapsed.TotalSeconds,
                OutputBytes = new FileInfo(output).Length,
                CompletedAt = DateTimeOffset.Now
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!r.KeepTemporaryFiles)
            {
                foreach (var temporary in new[] { r.FullVideo ? null : clip, intermediate })
                    if (temporary != null) try { File.Delete(temporary); } catch (IOException ex) { log("Не удалось удалить временный файл: " + ex.Message); }
            }
            progress(100);
            return directory;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
            {
                Status = token.IsCancellationRequested ? "Cancelled" : "Failed",
                Request = r,
                Error = ex.Message,
                ElapsedSeconds = timer.Elapsed.TotalSeconds
            }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
    }
}
