// Описывает модель и режим обработки.
namespace FrameForge;

/// <summary>Описывает модель и режим обработки.</summary>
public record ProcessingProfile(string Name, string Processor, string Model, int Noise)
{
    public static readonly ProcessingProfile[] All = [
        new("Аниме · Высокое / без шумоподавления · Real-CUGAN", "realcugan", "models-se", -1),
        new("Аниме · Высокое + очистка · Real-CUGAN", "realcugan", "models-se", 1),
        new("Аниме · Базовое · Real-ESRGAN Video v3", "realesrgan", "realesr-animevideov3", 0),
        new("Обычное видео · Real-ESRGAN Plus", "realesrgan", "realesrgan-plus", 0),
        new("Аниме · Real-CUGAN Pro / консервативный", "realcugan", "models-pro", 0)
    ];
}
