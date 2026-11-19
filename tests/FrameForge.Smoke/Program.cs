using System.Security.Cryptography;
using System.Text.Json;
using FrameForge;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string Tool(string name) => Directory.EnumerateFiles(Path.Combine(root, "tools"), name, SearchOption.AllDirectories).First();
var audioRegression = args.Contains("--audio-regression");
var input = audioRegression
    ? Path.Combine(root, "experiments", "sources", "1b8fb6b9-a1b5-48b2-bba4-9398cc49087b.bin")
    : Directory.EnumerateFiles(Path.Combine(root, "experiments", "sources"), "*.bin").First();
var before = SHA256.HashData(File.ReadAllBytes(input));
var ffmpeg = Tool("ffmpeg.exe");
var probe = Tool("ffprobe.exe");
var video2x = Tool("video2x.exe");
var processor = new Video2XProcessor();
var profiles = ProcessingProfile.All;
var wide = ResolutionPlan.Create(1920, 800, 1920, 1080, profiles[0]);
if (wide.Width != 1920 || wide.Height != 800) throw new Exception("Aspect ratio not preserved");
var portrait = ResolutionPlan.Create(720, 1280, 1920, 1080, profiles[0]);
if (portrait.Width != 606 || portrait.Height != 1080) throw new Exception("Portrait bounds failed");
var plus = ResolutionPlan.Create(1280, 720, 1920, 1080, profiles[3]);
if (plus.ModelScale != 4 || plus.Width != 1920 || !plus.NeedsResize) throw new Exception("Plus plan failed");
try { ResolutionPlan.Create(320, 180, 7680, 4320, profiles[0]); throw new Exception("Expected scale rejection"); }
catch (ArgumentException) { }
Console.WriteLine("PASS resolution planning: wide / portrait / Plus / excessive scale");
foreach (var profile in audioRegression ? profiles.Take(1) : profiles)
{
    Console.WriteLine($"TEST {profile.Name}");
    var request = new ProcessingRequest(input, Path.Combine(root, "experiments", "smoke"), video2x, ffmpeg,
        probe, profile, audioRegression ? 2560 : 1920, audioRegression ? 1440 : 1080,
        audioRegression ? 30 : 60, audioRegression ? 30 : 1);
    var result = await processor.ProcessAsync(request, Console.WriteLine, _ => {}, CancellationToken.None);
    if (!File.Exists(Path.Combine(result, "enhanced.mkv"))) throw new Exception("Missing output");
    using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(result, "report.json")));
    var actual = report.RootElement.GetProperty("Result");
    if (actual.GetProperty("Width").GetInt32() != request.TargetWidth || actual.GetProperty("Height").GetInt32() != request.TargetHeight)
        throw new Exception("Wrong final resolution");
    if (audioRegression)
    {
        var audioJson = await ProcessRunner.RunAsync(probe,
            ["-v", "error", "-select_streams", "a", "-show_streams", "-of", "json", Path.Combine(result, "enhanced.mkv")], null, CancellationToken.None);
        using var audio = JsonDocument.Parse(audioJson);
        var stream = audio.RootElement.GetProperty("streams").EnumerateArray().Single();
        if (stream.GetProperty("codec_name").GetString() != "pcm_f32le" ||
            stream.GetProperty("sample_rate").GetString() != "44100" || stream.GetProperty("channels").GetInt32() != 2)
            throw new Exception("Audio regression: codec, rate or channels changed");
        if (Math.Abs(actual.GetProperty("Duration").GetDouble() - 30) > 0.1)
            throw new Exception("Audio regression: wrong duration");
        Console.WriteLine("PASS trimmed AAC → PCM: 30 s, stereo / 44100 Hz, full output decoded");
    }
    Console.WriteLine($"PASS {profile.Name}: {result}");
}
// Cancellation exercises the actual process runner, not a mocked token.
using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
{
    try
    {
        await ProcessRunner.RunAsync(ffmpeg,
            ["-nostdin", "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc=size=64x64:rate=1", "-t", "30", "-f", "null", "-"], null, cancellation.Token);
        throw new Exception("Cancellation was ignored");
    }
    catch (OperationCanceledException) { Console.WriteLine("PASS process cancellation"); }
}
try
{
    await ProcessRunner.RunAsync(ffmpeg, ["-frame-forge-invalid-option"], null, CancellationToken.None);
    throw new Exception("Expected process failure");
}
catch (InvalidOperationException) { Console.WriteLine("PASS failure reporting"); }
if (!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input)))) throw new Exception("Input changed");
Console.WriteLine("PASS source hash unchanged");
