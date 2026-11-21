// Автоматические проверки Frame Forge.
using Xunit;

namespace FrameForge.Tests;

/// <summary>Проверяет параметры обработки и метаданные видео.</summary>
public sealed class ProcessingTests
{
    /// <summary>Проверяет допустимые размеры кадра.</summary>
    [Theory]
    [InlineData("2", "7680", true)]
    [InlineData("1920", "1080", true)]
    [InlineData("1", "1080", false)]
    [InlineData("1920", "7681", false)]
    [InlineData("x", "1080", false)]
    [InlineData("1920", "x", false)]
    public void ParsesSize(string width, string height, bool valid)
    {
        if (valid) Assert.Equal((int.Parse(width), int.Parse(height)), ProcessingInput.ReadSize(width, height));
        else Assert.Throws<ArgumentException>(() => ProcessingInput.ReadSize(width, height));
    }

    /// <summary>Проверяет дробные секунды и ограничения диапазона.</summary>
    [Theory]
    [InlineData("1,5", "2.25", true)]
    [InlineData("0", "300", true)]
    [InlineData("-1", "1", false)]
    [InlineData("0", "0", false)]
    [InlineData("0", "301", false)]
    [InlineData("NaN", "1", false)]
    [InlineData("1", "Infinity", false)]
    [InlineData("1", "oops", false)]
    public void ParsesRange(string start, string duration, bool valid)
    {
        if (valid)
        {
            var range = ProcessingInput.ReadRange(false, start, duration);
            Assert.Equal(start == "0" ? 0 : 1.5, range.Start);
            Assert.Equal(duration == "300" ? 300 : 2.25, range.Duration);
        }
        else Assert.Throws<ArgumentException>(() => ProcessingInput.ReadRange(false, start, duration));
        Assert.Equal((0d, 0d), ProcessingInput.ReadRange(true, "invalid", "invalid"));
    }

    /// <summary>Проверяет вписывание и выбор масштаба модели.</summary>
    [Theory]
    [InlineData(1280, 720, 1920, 1080, 0, 1920, 1080, 2)]
    [InlineData(1920, 800, 1920, 1080, 0, 1920, 800, 2)]
    [InlineData(720, 1280, 1920, 1080, 0, 606, 1080, 2)]
    [InlineData(1280, 720, 3840, 2160, 4, 3840, 2160, 3)]
    [InlineData(1280, 720, 2560, 1440, 3, 2560, 1440, 4)]
    public void PlansResolution(int sourceWidth, int sourceHeight, int width, int height,
        int profile, int resultWidth, int resultHeight, int scale)
    {
        var plan = ResolutionPlan.Create(sourceWidth, sourceHeight, width, height, ProcessingProfile.All[profile]);
        Assert.Equal((resultWidth, resultHeight, scale), (plan.Width, plan.Height, plan.ModelScale));
        Assert.Equal((resultWidth, resultHeight) != (sourceWidth * scale, sourceHeight * scale), plan.NeedsResize);
    }

    /// <summary>Проверяет отклонение неподдерживаемого разрешения.</summary>
    [Theory]
    [InlineData(0, 720, 1920, 1080, 0)]
    [InlineData(1280, 720, 9000, 1080, 0)]
    [InlineData(1280, 720, 5120, 2880, 4)]
    [InlineData(1280, 720, 3840, 2160, 1)]
    public void RejectsUnsupportedResolution(int sw, int sh, int w, int h, int profile) =>
        Assert.Throws<ArgumentException>(() => ResolutionPlan.Create(sw, sh, w, h, ProcessingProfile.All[profile]));

    /// <summary>Проверяет совместимость аргументов с выбранным профилем.</summary>
    [Fact]
    public void BuildsVideo2XArguments()
    {
        var profiles = ProcessingProfile.All;
        var planar = Video2XProcessor.BuildArguments(profiles[0], 2, "in file.mkv", "out.mkv", true);
        Assert.Contains("--no-copy-streams", planar);
        Assert.DoesNotContain("-n", planar);
        Assert.Contains("in file.mkv", planar);
        Assert.Contains("ffv1", planar);
        Assert.DoesNotContain("crf=16", planar);
        var cleaned = Video2XProcessor.BuildArguments(profiles[1], 2, "in", "out");
        Assert.Equal("1", cleaned[Array.IndexOf(cleaned, "-n") + 1]);
        Assert.Contains("libx264", cleaned);
        Assert.Contains("crf=16", cleaned);
        Assert.Contains("crf=22", Video2XProcessor.BuildArguments(profiles[0], 2, "in", "out", crf: 22));
        Assert.DoesNotContain("crf=20", Video2XProcessor.BuildArguments(profiles[0], 2, "in", "out", lossless: true, crf: 20));
        Assert.Throws<ArgumentException>(() => Video2XProcessor.BuildArguments(profiles[0], 1, "in", "out"));
        Assert.Throws<ArgumentException>(() => Video2XProcessor.BuildArguments(profiles[1], 3, "in", "out"));
        Assert.Throws<ArgumentException>(() => Video2XProcessor.BuildArguments(profiles[3], 2, "in", "out"));
        Assert.Throws<ArgumentException>(() => Video2XProcessor.BuildArguments(profiles[4], 4, "in", "out"));
    }

    /// <summary>Проверяет выбор видеодорожки и чтение длительности.</summary>
    [Fact]
    public void ParsesProbe()
    {
        var data = VideoProbe.Parse("""
            {"streams":[{"codec_type":"audio"},{"codec_type":"video","width":1280,"height":720,"avg_frame_rate":"24/1","codec_name":"h264"}],"format":{"duration":"42.5"}}
            """);
        Assert.Equal(new VideoDetails(1280, 720, 42.5, "24/1", "h264"), data);
        Assert.Throws<InvalidOperationException>(() => VideoProbe.Parse("""{"streams":[{"codec_type":"audio"}]}"""));
    }

    /// <summary>Отличает служебную дорожку глав от обычных потоков данных.</summary>
    [Fact]
    public void FindsChapterDataInMp4()
    {
        const string json = """
            {"streams":[
                {"index":0,"codec_type":"video"},
                {"index":1,"codec_type":"data","codec_name":"bin_data","codec_tag_string":"tmcd"},
                {"index":2,"codec_type":"data","codec_name":"bin_data","codec_tag_string":"text"}
            ],"chapters":[{"id":0}],"format":{"format_name":"mov,mp4"}}
            """;
        Assert.Equal(2, VideoProbe.FindChapterStreamCandidate(json));
        Assert.Null(VideoProbe.FindChapterStreamCandidate(json.Replace("""[{"id":0}]""", "[]")));
        Assert.Equal(2, VideoProbe.ParsePreservedStreams(json).Length);
        Assert.Single(VideoProbe.ParsePreservedStreams(json, 2));
    }

    /// <summary>Сравнивает дорожки с учётом оформления и вложений.</summary>
    [Fact]
    public void ParsesPreservedStreamInventory()
    {
        var tracks = VideoProbe.ParsePreservedStreams("""
            {"streams":[
              {"codec_type":"audio","codec_name":"aac","extradata_hash":"ignored"},
              {"codec_type":"video","codec_name":"h264"},
              {"codec_type":"subtitle","codec_name":"ass","extradata_hash":"styles"},
              {"codec_type":"subtitle","codec_name":"webvtt","extradata_hash":"regions"},
              {"codec_type":"attachment","extradata_hash":"font"},
              {"codec_type":"video","codec_name":"png"}
            ]}
            """);

        Assert.Equal(new PreservedStream[]
        {
            new("audio", "aac"),
            new("subtitle", "ass", "styles"),
            new("subtitle", "webvtt", "regions"),
            new("attachment", "", "font"),
            new("video", "png")
        }, tracks);
    }
}
