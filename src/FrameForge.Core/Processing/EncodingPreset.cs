// Подписи качества кодирования H.264.
namespace FrameForge;

/// <summary>Хранит подписи пресетов кодирования H.264.</summary>
public static class EncodingPreset
{
    /// <summary>Возвращает подпись пресета или сохранённое значение CRF.</summary>
    public static string Describe(int crf) => crf switch
    {
        22 => "Компактное (CRF 22)",
        20 => "Сбалансированное (CRF 20)",
        16 => "Высокое (CRF 16)",
        _ => $"CRF {crf}"
    };
}
