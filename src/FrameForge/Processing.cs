using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FrameForge;

public record ProcessingProfile(string Name, string Processor, string Model, int Noise)
{
    public static readonly ProcessingProfile[] All = [
        new("Аниме · Real-CUGAN SE", "realcugan", "models-se", 0),
        new("Аниме · Real-CUGAN SE / очистка", "realcugan", "models-se", 1),
        new("Аниме · Real-ESRGAN Video v3", "realesrgan", "realesr-animevideov3", 0),
        new("Обычное видео · Real-ESRGAN Plus", "realesrgan", "realesrgan-plus", 0)
    ];
}

public sealed class ExternalProcessException(string tool, int exitCode, string details)
    : InvalidOperationException($"{tool}: код {exitCode}\n{details}")
{
    public int ExitCode { get; } = exitCode;
}

public static class ProcessRunner
{
    public static async Task<string> RunAsync(string executable, IEnumerable<string> args,
        Action<string>? onLine, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))! };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        process.Start();
        using var registration = token.Register(() => {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var output = new StringBuilder();
        var tail = new Queue<string>();
        async Task Read(StreamReader reader, bool capture)
        {
            // Progress bars may use carriage returns without newlines.
            var buffer = new char[2048];
            var pending = new StringBuilder();
            void Emit()
            {
                if (pending.Length == 0) return;
                var line = Regex.Replace(pending.ToString(), @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                pending.Clear();
                lock (tail) { tail.Enqueue(line); while (tail.Count > 25) tail.Dequeue(); }
                onLine?.Invoke(line);
            }
            int count;
            while ((count = await reader.ReadAsync(buffer)) > 0)
            {
                if (capture) output.Append(buffer, 0, count);
                for (var i = 0; i < count; i++)
                    if (buffer[i] is '\r' or '\n') Emit();
                    else { pending.Append(buffer[i]); if (pending.Length > 8192) Emit(); }
            }
            Emit();
        }
        await Task.WhenAll(Read(process.StandardOutput, true), Read(process.StandardError, false), process.WaitForExitAsync());
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new ExternalProcessException(Path.GetFileName(executable), process.ExitCode, string.Join('\n', tail));
        return output.ToString();
    }
}

public sealed record VideoDetails(int Width, int Height, double Duration, string FrameRate, string Codec);

public static class VideoProbe
{
    public static async Task<VideoDetails> ReadAsync(string probe, string input, CancellationToken token)
    {
        var json = await ProcessRunner.RunAsync(probe,
            ["-v", "error", "-show_streams", "-show_format", "-of", "json", input], null, token);
        using var doc = JsonDocument.Parse(json);
        var video = doc.RootElement.GetProperty("streams").EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
        if (video.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("В файле нет видеопотока.");
        double duration = 0;
        if (doc.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var d))
            double.TryParse(d.GetString(), CultureInfo.InvariantCulture, out duration);
        return new(video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(), duration,
            video.GetProperty("avg_frame_rate").GetString() ?? "?", video.GetProperty("codec_name").GetString() ?? "?");
    }
}

public record ProcessingRequest(string Input, string OutputRoot, string Video2X, string FFmpeg,
    string FFprobe, ProcessingProfile Profile, int Scale, double Start, double Duration);

public interface IVideoProcessor
{
    Task<string> ProcessAsync(ProcessingRequest request, Action<string> log,
        Action<double> progress, CancellationToken token);
}

public sealed class Video2XProcessor : IVideoProcessor
{
    public static string[] BuildArguments(ProcessingProfile profile, int scale, string input, string output)
    {
        if (scale is not (2 or 4)) throw new ArgumentException("Масштаб должен быть 2 или 4.");
        if (profile.Model == "realesrgan-plus" && scale != 4)
            throw new ArgumentException("Real-ESRGAN Plus требует масштаб 4×.");
        if (profile.Processor == "realcugan" && profile.Noise == 1 && scale != 2)
            throw new ArgumentException("Real-CUGAN SE с очисткой уровня 1 поддерживает в прототипе только 2×.");
        return ["-i", input, "-o", output, "-p", profile.Processor,
            $"--{profile.Processor}-model", profile.Model, "-s", scale.ToString(),
            "-n", profile.Noise.ToString(), "-d", "0", "-c", "libx264", "--pix-fmt", "yuv420p",
            "-e", "crf=16", "-e", "preset=medium"];
    }

    public async Task<string> ProcessAsync(ProcessingRequest r, Action<string> log,
        Action<double> progress, CancellationToken token)
    {
        foreach (var file in new[] { r.Input, r.Video2X, r.FFmpeg, r.FFprobe })
            if (!File.Exists(file)) throw new FileNotFoundException("Файл не найден", file);
        if (!double.IsFinite(r.Start) || !double.IsFinite(r.Duration) || r.Start < 0 || r.Duration <= 0 || r.Duration > 300)
            throw new ArgumentException("Укажите начало ≥ 0 и длительность от 0 до 300 секунд.");
        _ = BuildArguments(r.Profile, r.Scale, "input", "output");
        var source = await VideoProbe.ReadAsync(r.FFprobe, r.Input, token);
        if (source.Duration > 0 && r.Start + r.Duration > source.Duration + 0.1)
            throw new ArgumentException("Фрагмент выходит за длительность видео.");
        // A fresh job directory prevents collisions and any input overwrite.
        var directory = Path.Combine(Path.GetFullPath(r.OutputRoot),
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(directory);
        var clip = Path.Combine(directory, "reference.mkv");
        var partial = Path.Combine(directory, "enhanced.partial.mkv");
        var output = Path.Combine(directory, "enhanced.mkv");
        var timer = Stopwatch.StartNew();
        var report = Path.Combine(directory, "report.json");
        string? warning = null;
        try
        {
            log("Подготовка эталонного фрагмента без потерь (FFV1)…");
            await ProcessRunner.RunAsync(r.FFmpeg,
                ["-nostdin", "-n", "-v", "warning", "-i", r.Input, "-ss", r.Start.ToString(CultureInfo.InvariantCulture),
                 "-t", r.Duration.ToString(CultureInfo.InvariantCulture), "-map", "0:v:0", "-map", "0:a?",
                 "-c:v", "ffv1", "-level", "3", "-c:a", "flac", clip], log, token);
            log($"Обработка: {r.Profile.Name}, {r.Scale}×. GPU 0.\nКаталог: {directory}");
            var args = BuildArguments(r.Profile, r.Scale, clip, partial);
            try { await ProcessRunner.RunAsync(r.Video2X, args, line => {
                log(line);
                var match = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*%");
                if (match.Success && double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var p))
                    progress(Math.Clamp(p, 0, 100));
            }, token); }
            catch (ExternalProcessException ex) when (ex.ExitCode == unchecked((int)0xC0000005) &&
                ex.Message.Contains("Video processed successfully", StringComparison.Ordinal) && File.Exists(partial))
            {
                warning = "Video2X завершился с 0xC0000005 после сообщения об успехе. Результат требует проверки; это сбой обработчика.";
                log("ПРЕДУПРЕЖДЕНИЕ: " + warning);
            }
            log("Проверка: полное декодирование видео и аудио…");
            await ProcessRunner.RunAsync(r.FFmpeg,
                ["-nostdin", "-v", "error", "-xerror", "-i", partial, "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"], log, token);
            var result = await VideoProbe.ReadAsync(r.FFprobe, partial, token);
            if (result.Width != source.Width * r.Scale || result.Height != source.Height * r.Scale ||
                result.Duration <= 0 || Math.Abs(result.Duration - r.Duration) > 1)
                throw new InvalidOperationException("Результат не прошёл проверку размера или длительности.");
            token.ThrowIfCancellationRequested();
            File.Move(partial, output, overwrite: false);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new {
                Status = warning == null ? "Completed" : "CompletedWithWarning", Warning = warning, Request = r, Source = source, Result = result,
                ElapsedSeconds = timer.Elapsed.TotalSeconds, OutputBytes = new FileInfo(output).Length,
                CompletedAt = DateTimeOffset.Now
            }, new JsonSerializerOptions { WriteIndented = true }));
            progress(100);
            return directory;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new {
                Status = token.IsCancellationRequested ? "Cancelled" : "Failed", Request = r,
                Error = ex.Message, ElapsedSeconds = timer.Elapsed.TotalSeconds
            }, new JsonSerializerOptions { WriteIndented = true }));
            // Keep partial files for diagnostics; never present them as completed output.
            throw;
        }
    }
}
