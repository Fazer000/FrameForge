// Параметры или операции обработки видео.
using System.Globalization;

namespace FrameForge;

/// <summary>Разбирает параметры задания, введённые пользователем.</summary>
public static class ProcessingInput
{
    /// <summary>Читает размеры целевого кадра в пикселях.</summary>
    public static (int Width, int Height) ReadSize(string width, string height)
    {
        if (!int.TryParse(width, out var w) || !int.TryParse(height, out var h) ||
            w is < 2 or > 7680 || h is < 2 or > 7680)
            throw new ArgumentException("Укажите разрешение от 2 до 7680 пикселей.");
        return (w, h);
    }

    /// <summary>Читает диапазон обработки в секундах.</summary>
    public static (double Start, double Duration) ReadRange(bool full, string start, string duration)
    {
        if (full) return (0, 0);
        if (!TrySeconds(start, out var offset) || !TrySeconds(duration, out var length) ||
            offset < 0 || length is <= 0 or > 300)
            throw new ArgumentException("Укажите начало ≥ 0 и длину фрагмента до 300 секунд.");
        return (offset, length);
    }

    /// <summary>Читает конечное число с точкой или запятой.</summary>
    private static bool TrySeconds(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value);
}
