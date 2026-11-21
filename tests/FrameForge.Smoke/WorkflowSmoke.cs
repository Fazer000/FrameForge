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
        var source = Path.Combine(directory, "episode.mp4");
        var richSource = Path.Combine(directory, "source-with-tracks.mkv");
        var ass = Path.Combine(directory, "sub.ass");
        var vtt = Path.Combine(directory, "sub.vtt");
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        await File.WriteAllTextAsync(ass, """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 320
            PlayResY: 180
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,20,&H0000FFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,10,1
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.20,0:00:00.90,Default,,0,0,0,,{\pos(160,60)}Frame Forge ASS
            """);
        await File.WriteAllTextAsync(vtt, """
            WEBVTT

            00:00:00.200 --> 00:00:00.900 position:25% align:start
            <i>Frame Forge VTT</i>
            """);
        await ProcessRunner.RunAsync(ffmpeg,
            ["-nostdin", "-n", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24000/1001",
             "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000",
             "-i", ass, "-i", vtt, "-t", "1", "-map", "0:v:0", "-map", "1:a:0", "-map", "2:a:0", "-map", "3:s:0", "-map", "4:s:0",
             "-c:v", "libx264", "-c:a:0", "aac", "-c:a:1", "flac", "-c:s", "copy", "-attach", font,
             "-metadata:s:t:0", "mimetype=font/ttf", richSource], null, CancellationToken.None);
        var chapterFile = Path.Combine(directory, "chapters.txt");
        await File.WriteAllTextAsync(chapterFile, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1000\ntitle=Intro\n");
        await ProcessRunner.RunAsync(ffmpeg,
            ["-nostdin", "-n", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24",
             "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-i", chapterFile,
             "-map", "0:v:0", "-map", "1:a:0", "-map_chapters", "2", "-t", "1", "-c:v", "libx264", "-c:a", "aac", source],
            null, CancellationToken.None);
        var hash = LibraryIntegration.Hash(source);
        await File.WriteAllTextAsync(Path.Combine(directory, "index.json"),
            """{"anime":[{"title":"Проверка","episodes":[{"fileName":"episode.mp4","episodeNumber":"1","quality":"180p","subtitles":[{"fileName":"sub.ass"},{"fileName":"sub.vtt"}]},{"fileName":"episode.mp4","playlistFileName":"stream.m3u8"},{"fileName":"missing.mp4"}]}]}""");
        var originalEntry = (await LibraryIntegration.ReadCatalogAsync(directory)).Single();
        if (originalEntry.SourcePath != source || !originalEntry.Title.EndsWith("180p"))
            throw new Exception("Неверный список серий библиотеки");
        var originalBytes = File.ReadAllBytes(source);
        var richHash = LibraryIntegration.Hash(richSource);
        var originalTracks = await VideoProbe.ReadPreservedStreamsAsync(probe, richSource, CancellationToken.None);
        if (originalTracks.Count(stream => stream.Type == "audio") != 2 ||
            !originalTracks.Any(stream => stream.Codec == "ass" && stream.ConfigurationHash != null) ||
            !originalTracks.Any(stream => stream.Codec == "webvtt") ||
            !originalTracks.Any(stream => stream.Type == "attachment" && stream.ConfigurationHash != null))
            throw new Exception("Missing tracks in the regression fixture");
        var originalVtt = await ExtractVtt(ffmpeg, richSource);
        if (!originalVtt.Contains("position:25% align:start", StringComparison.Ordinal))
            throw new Exception("Missing WebVTT positioning in the regression fixture");
        var processor = new Video2XProcessor(); string? firstResult = null; ProcessingRequest? firstRequest = null;
        foreach (var profile in allProfiles ? ProcessingProfile.All : ProcessingProfile.All.Take(1))
        {
            var (width, height) = profile.Noise == 1 ? (640, 360) : (480, 270);
            var request = new ProcessingRequest(richSource, directory, engine, ffmpeg, probe, profile, width, height, 0, 0, FullVideo: true, KeepTemporaryFiles: false, EncodingCrf: 22);
            var result = await processor.ProcessAsync(request, Console.WriteLine, _ => { }, CancellationToken.None);
            var enhanced = Path.Combine(result, "enhanced.mkv");
            var preserved = await VideoProbe.ReadPreservedStreamsAsync(probe, enhanced, CancellationToken.None);
            if (!originalTracks.SequenceEqual(preserved)) throw new Exception("Original tracks lost");
            if (await StreamHashes(ffmpeg, richSource) != await StreamHashes(ffmpeg, enhanced))
                throw new Exception("Audio or subtitle packet contents changed");
            if (originalVtt != await ExtractVtt(ffmpeg, enhanced))
                throw new Exception("WebVTT timing or formatting changed");
            if (File.Exists(Path.Combine(result, "model-output.mkv")) || File.Exists(Path.Combine(result, "reference.mkv"))) throw new Exception("Temporary files retained");
            if (LibraryIntegration.Hash(richSource) != richHash) throw new Exception("Source modified");
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
        var video = new LibraryVideo(directory, "episode.mp4", "Test episode", hash);
        var richEpisode = new LibraryVideo(directory, Path.GetFileName(richSource), "Дорожки", richHash);
        var guarded = new QueueJob { Request = firstRequest with { Library = richEpisode } };
        await new ProcessingQueue(processor, LibraryIntegration.PublishAsync).RunAsync(
            [guarded], () => { }, _ => { }, Console.WriteLine, (_, _) => { }, CancellationToken.None);
        if (guarded.Status != "С предупреждением" || !File.Exists(Path.Combine(guarded.ResultPath, "enhanced.mkv")) ||
            File.Exists(richEpisode.MarkerPath) || LibraryIntegration.Hash(richSource) != richHash)
            throw new Exception("AnimeLib publication did not preserve the original and MKV fallback");

        var clip = await processor.ProcessAsync(firstRequest with { FullVideo = false, Start = 0.1, Duration = 0.5 },
            Console.WriteLine, _ => { }, CancellationToken.None);
        var clipTracks = await VideoProbe.ReadPreservedStreamsAsync(probe, Path.Combine(clip, "enhanced.mkv"),
            CancellationToken.None);
        if (!originalTracks.SequenceEqual(clipTracks))
            throw new Exception("Clip did not retain the original track types");

        var clipDetails = await VideoProbe.ReadAsync(probe, Path.Combine(clip, "enhanced.mkv"), CancellationToken.None);
        if (Math.Abs(clipDetails.Duration - 0.5) > 0.1)
            throw new Exception("Packets before the cut or subtitle tails extended the clip");
        var clipVtt = await ExtractVtt(ffmpeg, Path.Combine(clip, "enhanced.mkv"));
        if (!clipVtt.Contains("00:00.100 --> 00:00.500 position:25% align:start", StringComparison.Ordinal))
            throw new Exception("The clip did not retain the overlapping WebVTT formatting within the cut");

        var publishRequest = firstRequest with { Input = source, Library = video };
        var libraryResult = await processor.ProcessAsync(publishRequest, Console.WriteLine, _ => { }, CancellationToken.None);
        if (Path.GetDirectoryName(libraryResult) != video.WorkRoot)
            throw new Exception("Library processing wrote files outside the service directory");
        var stagedResult = Path.Combine(libraryResult, "enhanced.mkv");
        var libraryTracks = await VideoProbe.ReadPreservedStreamsAsync(probe, stagedResult, CancellationToken.None);
        if (!libraryTracks.Select(track => track.Codec).SequenceEqual(new[] { "aac", "ass", "webvtt" }))
            throw new Exception("MP4 with chapters did not preserve the registered external subtitles in MKV");
        var originalAudio = await StreamHashes(ffmpeg, source, audioOnly: true);
        var recoveryDirectory = Directory.CreateDirectory(Path.Combine(video.WorkRoot, "recovery")).FullName;
        var recoveryResult = Path.Combine(recoveryDirectory, "enhanced.mkv");
        File.Copy(stagedResult, recoveryResult);
        await LibraryIntegration.PublishAsync(publishRequest, libraryResult, Console.WriteLine, CancellationToken.None);
        if (File.Exists(stagedResult)) throw new Exception("Staged result retained after successful publication");
        if (originalAudio != await StreamHashes(ffmpeg, source, audioOnly: true))
            throw new Exception("AnimeLib publication changed the audio packets");
        if (!File.Exists(Path.Combine(libraryResult, "processing.log")) || !File.Exists(Path.Combine(libraryResult, "report.json")))
            throw new Exception("Job diagnostics lost after cleanup");
        if (File.Exists(video.BackupPath) || LibraryIntegration.Hash(source) == hash) throw new Exception("Temporary original not removed");
        var chapterNames = await ProcessRunner.RunAsync(probe,
            ["-v", "error", "-show_entries", "chapter_tags=title", "-of", "compact", source],
            null, CancellationToken.None);
        if (!chapterNames.Contains("Intro", StringComparison.Ordinal))
            throw new Exception("MP4 publication lost the original chapters");

        var legacyMarkerPath = source + ".frameforge.json";
        if (File.Exists(legacyMarkerPath) || File.Exists(source + ".frameforge-original") ||
            Directory.EnumerateFiles(directory, "*.frameforge-pending-*.mp4").Any())
            throw new Exception("Frame Forge left service files in the library root");
        var completedMarker = JsonSerializer.Deserialize<EnhancementMarker>(File.ReadAllText(video.MarkerPath));
        if (completedMarker?.State != "Enhanced" || completedMarker.EnhancedHash != LibraryIntegration.Hash(source)) throw new Exception("Published hash mismatch");
        if (completedMarker.Width != 480 || completedMarker.Height != 270) throw new Exception("Published resolution missing");
        if (!(await LibraryIntegration.ReadCatalogAsync(directory)).Single().Title.EndsWith("270p (FF)"))
            throw new Exception("Catalog did not show enhanced resolution");
        File.Move(video.MarkerPath, legacyMarkerPath);
        if (!(await LibraryIntegration.ReadCatalogAsync(directory)).Single().Title.EndsWith("270p (FF)"))
            throw new Exception("Catalog did not read the marker from the old layout");
        File.Move(legacyMarkerPath, video.MarkerPath);
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

    /// <summary>Извлекает WebVTT вместе с таймингом и положением текста.</summary>
    private static Task<string> ExtractVtt(string ffmpeg, string source) =>
        ProcessRunner.RunAsync(ffmpeg,
            ["-nostdin", "-v", "error", "-i", source, "-map", "0:s:1", "-c", "copy", "-f", "webvtt", "-"],
            null, CancellationToken.None);

    /// <summary>Хеширует исходные пакеты аудио и субтитров.</summary>
    private static Task<string> StreamHashes(string ffmpeg, string source, bool audioOnly = false)
    {
        string[] selection = audioOnly ? [] : ["-map", "0:s"];
        return ProcessRunner.RunAsync(ffmpeg,
            ["-nostdin", "-v", "error", "-i", source, "-map", "0:a", ..selection,
             "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-"],
            null, CancellationToken.None);
    }
}
