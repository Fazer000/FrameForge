namespace FrameForge;

public sealed record ResolutionPlan(int Width, int Height, int ModelScale, int IntermediateWidth, int IntermediateHeight)
{
    public bool NeedsResize => Width != IntermediateWidth || Height != IntermediateHeight;

    public static ResolutionPlan Create(int sourceWidth, int sourceHeight, int boxWidth, int boxHeight, ProcessingProfile profile)
    {
        if (sourceWidth < 2 || sourceHeight < 2 || boxWidth < 2 || boxHeight < 2 || boxWidth > 7680 || boxHeight > 7680)
            throw new ArgumentException("Размеры должны быть от 2 до 7680 пикселей (исходник — не меньше 2).");
        var ratio = Math.Min((double)boxWidth / sourceWidth, (double)boxHeight / sourceHeight);
        var width = Math.Max(2, (int)Math.Floor(sourceWidth * ratio / 2 + 0.000001) * 2);
        var height = Math.Max(2, (int)Math.Floor(sourceHeight * ratio / 2 + 0.000001) * 2);
        var scale = profile.Model == "realesrgan-plus" ? 4 : ratio <= 2 ? 2 : 4;
        if (ratio > scale)
            throw new ArgumentException("Выбранное разрешение превышает возможности модели (максимум 4×). Выберите меньший размер.");
        if (profile.Processor == "realcugan" && profile.Noise == 1 && scale > 2)
            throw new ArgumentException("Для Real-CUGAN с очисткой выберите разрешение не больше 2× от исходника.");
        return new(width, height, scale, checked(sourceWidth * scale), checked(sourceHeight * scale));
    }
}
