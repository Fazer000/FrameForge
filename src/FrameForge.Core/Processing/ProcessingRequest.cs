// Хранит параметры обработки видео.
namespace FrameForge;

/// <summary>Хранит параметры обработки видео.</summary>
public record ProcessingRequest(string Input, string OutputRoot, string Video2X, string FFmpeg,
    string FFprobe, ProcessingProfile Profile, int TargetWidth, int TargetHeight, double Start, double Duration,
    bool FullVideo = false, bool KeepTemporaryFiles = true, LibraryVideo? Library = null, int EncodingCrf = 16);

/// <summary>Определяет обработчик видео.</summary>
public interface IVideoProcessor
{
    /// <summary>Выполняет обработку и возвращает папку результата.</summary>
    Task<string> ProcessAsync(ProcessingRequest request, Action<string> log,
        Action<double> progress, CancellationToken token, Action<string>? resultDirectoryCreated = null);
}
