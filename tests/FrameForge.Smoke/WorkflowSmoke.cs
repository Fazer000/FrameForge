// Сквозные проверки обработки и библиотеки.
using System.Text.Json;
using FrameForge;

/// <summary>Проверяет обработку и замену серии на синтетических файлах.</summary>
public static class WorkflowSmoke
{
    /// <summary>Запускает сквозную проверку с установленными инструментами.</summary>
    public static async Task Run(string root, bool allProfiles = true)
    {
        /// <summary>Находит исполняемый файл для проверки.</summary>
        string Tool(string name) => Directory.EnumerateFiles(Path.Combine(root, "tools"), name, SearchOption.AllDirectories).First();
        var ffmpeg = Tool("ffmpeg.exe"); var probe = Tool("ffprobe.exe"); var engine = Tool("video2x.exe");
        var directory = Path.Combine(root, "experiments", "workflow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "episode.mkv");
        var subtitle = Path.Combine(directory, "sub.srt"); await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:00,900\nFrame Forge regression\n");
        await ProcessRunner.RunAsync(ffmpeg, ["-nostdin", "-n", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24000/1001", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-i", subtitle, "-t", "1", "-map", "0:v:0", "-map", "1:a:0", "-map", "2:s:0", "-c:v", "libx264", "-c:a", "aac", "-c:s", "srt", source], null, CancellationToken.None);
        var hash = LibraryIntegration.Hash(source);
        await File.WriteAllTextAsync(Path.Combine(directory, "index.json"),
            """{"anime":[{"title":"Проверка","episodes":[{"fileName":"episode.mkv","episodeNumber":"1","quality":"180p"},{"fileName":"episode.mkv","playlistFileName":"stream.m3u8"},{"fileName":"missing.mp4"}]}]}""");
        var originalEntry = (await LibraryIntegration.ReadCatalogAsync(directory)).Single();
        if (originalEntry.SourcePath != source || !originalEntry.Title.EndsWith("180p"))
            throw new Exception("Неверный список серий библиотеки");
        var originalBytes = File.ReadAllBytes(source);
        var processor = new Video2XProcessor(); string? firstResult = null; ProcessingRequest? firstRequest = null;
        foreach (var profile in allProfiles ? ProcessingProfile.All : ProcessingProfile.All.Take(1))
        {
            var (width, height) = profile.Noise == 1 ? (640, 360) : (480, 270);
            var request = new ProcessingRequest(source, directory, engine, ffmpeg, probe, profile, width, height, 0, 0, FullVideo: true, KeepTemporaryFiles: false, EncodingCrf: 22);
            var result = await processor.ProcessAsync(request, Console.WriteLine, _ => { }, CancellationToken.None);
            var json = await ProcessRunner.RunAsync(probe, ["-v", "error", "-show_streams", "-of", "json", Path.Combine(result, "enhanced.mkv")], null, CancellationToken.None);
            using var doc = JsonDocument.Parse(json);
            var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (!streams.Any(s => s.GetProperty("codec_type").GetString() == "subtitle") || !streams.Any(s => s.GetProperty("codec_type").GetString() == "audio")) throw new Exception("Full-video stream lost");
            if (File.Exists(Path.Combine(result, "model-output.mkv")) || File.Exists(Path.Combine(result, "reference.mkv"))) throw new Exception("Temporary files retained");
            if (LibraryIntegration.Hash(source) != hash) throw new Exception("Source modified");
            firstResult ??= result; firstRequest ??= request;
            Console.WriteLine("PASS full video, audio, subtitles, FPS, cleanup: " + profile.Name);
        }
        var store = new WorkspaceStore(Path.Combine(directory, "state"));
        var job = new QueueJob { Request = firstRequest!, Status = "Обработка" };
        var state = new WorkspaceState { Jobs = [job] }; store.Save(state); store.Save(state);
        if (store.Load().Jobs.Single().Status != "Прервано") throw new Exception("Interrupted job not recovered");
        await File.WriteAllTextAsync(Path.Combine(store.DirectoryPath, "workspace.json"), "{bad-json");
        if (store.Load().Jobs.Single().Request.Profile != firstRequest!.Profile) throw new Exception("State backup not recovered");
        Console.WriteLine("PASS queue roundtrip, interrupted recovery and corrupt-primary backup");
        var video = new LibraryVideo(directory, "episode.mkv", "Test episode", hash);
        var publishRequest = firstRequest with { Library = video };
        var stagedResult = Path.Combine(firstResult!, "enhanced.mkv");
        var recoveryDirectory = Directory.CreateDirectory(Path.Combine(directory, "recovery")).FullName;
        var recoveryResult = Path.Combine(recoveryDirectory, "enhanced.mkv");
        File.Copy(stagedResult, recoveryResult);
        await LibraryIntegration.PublishAsync(publishRequest, firstResult!, Console.WriteLine, CancellationToken.None);
        if (File.Exists(stagedResult)) throw new Exception("Staged result retained after successful publication");
        if (!File.Exists(Path.Combine(firstResult!, "processing.log")) || !File.Exists(Path.Combine(firstResult!, "report.json")))
            throw new Exception("Job diagnostics lost after cleanup");
        if (File.Exists(video.BackupPath) || LibraryIntegration.Hash(source) == hash) throw new Exception("Temporary original not removed");
        var completedMarker = JsonSerializer.Deserialize<EnhancementMarker>(File.ReadAllText(video.MarkerPath));
        if (completedMarker?.State != "Enhanced" || completedMarker.EnhancedHash != LibraryIntegration.Hash(source)) throw new Exception("Published hash mismatch");
        if (completedMarker.Width != 480 || completedMarker.Height != 270) throw new Exception("Published resolution missing");
        if (!(await LibraryIntegration.ReadCatalogAsync(directory)).Single().Title.EndsWith("270p (FF)"))
            throw new Exception("Catalog did not show enhanced resolution");
        await File.WriteAllTextAsync(video.MarkerPath, JsonSerializer.Serialize(completedMarker with { Width = 0, Height = 0 }));
        if (!(await LibraryIntegration.ReadCatalogAsync(directory, probe)).Single().Title.EndsWith("270p (FF)"))
            throw new Exception("Catalog did not discover legacy resolution");
        if (!(await LibraryIntegration.ReadCatalogAsync(directory)).Single().Title.EndsWith("Улучшено (FF)"))
            throw new Exception("Legacy marker lost without ffprobe");
        await File.WriteAllTextAsync(video.MarkerPath, JsonSerializer.Serialize(completedMarker with { EnhancedLength = 1 }));
        if (!(await LibraryIntegration.ReadCatalogAsync(directory)).Single().Title.EndsWith("180p"))
            throw new Exception("Catalog accepted a marker for a different file size");
        await File.WriteAllTextAsync(video.MarkerPath, JsonSerializer.Serialize(completedMarker));
        try { await LibraryIntegration.PublishAsync(publishRequest, firstResult!, _ => { }, CancellationToken.None); throw new Exception("Repeated replacement allowed"); }
        catch (InvalidOperationException) { }
        Console.WriteLine("PASS successful replacement: original deleted, Enhanced marker matches destination SHA256");
        var failedVideo = new LibraryVideo(directory, "cleanup-failure.mkv", "Cleanup failure", hash);
        File.WriteAllBytes(failedVideo.SourcePath, originalBytes);
        FileStream? lockedBackup = null;
        try
        {
            await LibraryIntegration.PublishAsync(publishRequest with { Library = failedVideo }, recoveryDirectory, line =>
            {
                if (line.StartsWith("Проверка записанного", StringComparison.Ordinal))
                    lockedBackup = new FileStream(failedVideo.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }, CancellationToken.None);
            throw new Exception("Expected cleanup failure");
        }
        catch (IOException) { if (LibraryIntegration.Hash(failedVideo.BackupPath) != hash) throw new Exception("Original lost on failure"); }
        finally { lockedBackup?.Dispose(); }
        if (!File.Exists(recoveryResult)) throw new Exception("Staged result lost after failed publication");
        var savedEnhanced = File.ReadAllBytes(failedVideo.SourcePath); File.AppendAllText(failedVideo.SourcePath, "changed");
        try { LibraryIntegration.Restore(failedVideo); throw new Exception("Changed source was overwritten"); } catch (IOException) { }
        File.WriteAllBytes(failedVideo.SourcePath, savedEnhanced); LibraryIntegration.Restore(failedVideo);
        if (LibraryIntegration.Hash(failedVideo.SourcePath) != hash) throw new Exception("Failure recovery failed");
        var marker = JsonSerializer.Deserialize<EnhancementMarker>(File.ReadAllText(failedVideo.MarkerPath));
        if (marker?.State != "Restored") throw new Exception("Marker state not restored");
        Console.WriteLine("PASS cleanup failure retains original; tamper refused; failure recovery preserves original SHA256");
        await LibraryIntegration.PublishAsync(publishRequest with { Library = failedVideo, KeepTemporaryFiles = true },
            recoveryDirectory, Console.WriteLine, CancellationToken.None);
        if (!File.Exists(recoveryResult) || File.Exists(failedVideo.BackupPath))
            throw new Exception("Keeping temporary files did not preserve the staged result or retained the original");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await ProcessRunner.RunAsync(ffmpeg,
                ["-nostdin", "-re", "-f", "lavfi", "-i", "testsrc=size=64x64:rate=1", "-t", "30", "-f", "null", "-"],
                null, cancellation.Token);
            throw new Exception("Процесс не отменён");
        }
        catch (OperationCanceledException) { }
    }
}
