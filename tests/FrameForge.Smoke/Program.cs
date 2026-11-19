using System.Security.Cryptography;
using System.Text.Json;
using FrameForge;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string Tool(string name) => Directory.EnumerateFiles(Path.Combine(root, "tools"), name, SearchOption.AllDirectories).First();
var input = Directory.EnumerateFiles(Path.Combine(root, "experiments", "sources"), "*.bin").First();
var before = SHA256.HashData(File.ReadAllBytes(input));
var ffmpeg = Tool("ffmpeg.exe");
var probe = Tool("ffprobe.exe");
var video2x = Tool("video2x.exe");
var processor = new Video2XProcessor();
var profiles = ProcessingProfile.All;
foreach (var profile in profiles)
{
    Console.WriteLine($"TEST {profile.Name}");
    var request = new ProcessingRequest(input, Path.Combine(root, "experiments", "smoke"), video2x, ffmpeg,
        probe, profile, profile.Model == "realesrgan-plus" ? 4 : 2, 60, 1);
    var result = await processor.ProcessAsync(request, Console.WriteLine, _ => {}, CancellationToken.None);
    if (!File.Exists(Path.Combine(result, "enhanced.mkv"))) throw new Exception("Missing output");
    using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(result, "report.json")));
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
