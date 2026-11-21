// Описывает модель и режим обработки.
namespace FrameForge;

/// <summary>Описывает модель и режим обработки.</summary>
public record ProcessingProfile(string Name, string Processor, string Model, int Noise)
{
    private static readonly ProcessingProfile RealCugan = new("Аниме · Высокое · Real-CUGAN", "realcugan", "models-se", -1);
    private static readonly ProcessingProfile RealCuganWithDenoise = new("Аниме · Высокое · Real-CUGAN + шумоподавление", "realcugan", "models-se", 1);
    private static readonly ProcessingProfile RealEsrganVideo = new("Аниме · Базовое · Real-ESRGAN Video v3", "realesrgan", "realesr-animevideov3", 0);
    private static readonly ProcessingProfile RealEsrganPlus = new("Кино · Высокое · Real-ESRGAN Plus", "realesrgan", "realesrgan-plus", 0);
    private static readonly ProcessingProfile RealCuganPro = new("Аниме · Высокое · Real-CUGAN Pro", "realcugan", "models-pro", 0);

    /// <summary>Хранит профили в порядке индексов сохранённых настроек.</summary>
    public static readonly ProcessingProfile[] All =
        [RealCugan, RealCuganWithDenoise, RealEsrganVideo, RealEsrganPlus, RealCuganPro];

    /// <summary>Возвращает профили в порядке меню.</summary>
    public static IReadOnlyList<ProcessingProfile> MenuOrder { get; } =
        [RealEsrganPlus, RealCuganPro, RealCuganWithDenoise, RealCugan, RealEsrganVideo];
}
