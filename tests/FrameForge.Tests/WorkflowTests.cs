// Автоматические проверки Frame Forge.
using Xunit;

namespace FrameForge.Tests;

/// <summary>Проверяет обработку видео реальными инструментами.</summary>
public sealed class WorkflowTests
{
    /// <summary>Проверяет профили, публикацию и откат замены серии.</summary>
    [LocalToolsFact]
    [Trait("Category", "Integration")]
    public async Task ProcessesAndPublishesVideo()
    {
        var root = Environment.GetEnvironmentVariable("FRAMEFORGE_TEST_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(root), "Укажите FRAMEFORGE_TEST_ROOT с установленными инструментами.");
        await WorkflowSmoke.Run(root!);
    }
}

/// <summary>Включает проверку при указании папки с инструментами.</summary>
internal sealed class LocalToolsFactAttribute : FactAttribute
{
    /// <summary>Пропускает проверку вне настроенного окружения.</summary>
    public LocalToolsFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FRAMEFORGE_TEST_ROOT")))
            Skip = "Укажите FRAMEFORGE_TEST_ROOT с установленными инструментами.";
    }
}
