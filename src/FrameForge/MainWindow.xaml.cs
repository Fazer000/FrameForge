using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FrameForge;

public partial class MainWindow : Window
{
    private CancellationTokenSource? cancellation;
    private readonly string root;
    private readonly IVideoProcessor processor = new Video2XProcessor();
    public MainWindow()
    {
        InitializeComponent();
        root = FindRoot();
        OutputPath.Text = Path.Combine(root, "experiments", "results");
        var toolRoot = Path.Combine(root, "tools");
        Video2XPath.Text = FindTool(toolRoot, "video2x.exe");
        FFmpegPath.Text = FindTool(toolRoot, "ffmpeg.exe");
        FFprobePath.Text = FindTool(toolRoot, "ffprobe.exe");
        ProfileBox.ItemsSource = ProcessingProfile.All;
        ProfileBox.SelectedIndex = 0;
        var samples = Path.Combine(root, "experiments", "sources");
        if (Directory.Exists(samples)) InputPath.Text = Directory.EnumerateFiles(samples).FirstOrDefault(p => !p.EndsWith(".json")) ?? "";
    }
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "frame-forge.sln"))) return dir.FullName;
        return AppContext.BaseDirectory;
    }
    private static string FindTool(string dir, string name) => Directory.Exists(dir)
        ? Directory.EnumerateFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault() ?? "" : "";
    private static string? Pick(string filter) { var dialog = new OpenFileDialog { Filter = filter }; return dialog.ShowDialog() == true ? dialog.FileName : null; }
    private async void ChooseInput(object sender, RoutedEventArgs e)
    {
        var file = Pick("Видео|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.ts;*.m4v;*.bin|Все файлы|*.*");
        if (file == null) return;
        InputPath.Text = file;
        SettingsPanel.IsEnabled = false;
        RunButton.IsEnabled = false;
        try
        {
            var info = await VideoProbe.ReadAsync(FFprobePath.Text, file, CancellationToken.None);
            VideoInfo.Text = $"{info.Width} × {info.Height} · {info.Codec} · FPS {info.FrameRate} · {TimeSpan.FromSeconds(info.Duration):hh\\:mm\\:ss}";
        }
        catch (Exception ex) { VideoInfo.Text = "Не удалось прочитать видео: " + ex.Message; }
        finally { SettingsPanel.IsEnabled = true; RunButton.IsEnabled = true; }
    }
    private void ChooseOutput(object sender, RoutedEventArgs e)
    { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog() == true) OutputPath.Text = dialog.FolderName; }
    private void ChooseVideo2X(object sender, RoutedEventArgs e) { if (Pick("Video2X|video2x.exe") is { } p) Video2XPath.Text = p; }
    private void ChooseFFmpeg(object sender, RoutedEventArgs e) { if (Pick("FFmpeg|ffmpeg.exe") is { } p) FFmpegPath.Text = p; }
    private void ChooseFFprobe(object sender, RoutedEventArgs e) { if (Pick("ffprobe|ffprobe.exe") is { } p) FFprobePath.Text = p; }
    private void Log(string line) => Dispatcher.InvokeAsync(() => {
        if (LogBox.Text.Length > 100_000) LogBox.Text = LogBox.Text[^50_000..];
        LogBox.AppendText(line + Environment.NewLine); LogBox.ScrollToEnd();
    });
    private async void Run(object sender, RoutedEventArgs e)
    {
        if (!TrySeconds(StartSeconds.Text, out var start) || !TrySeconds(DurationSeconds.Text, out var duration))
        { MessageBox.Show("Начало и длительность должны быть числами в секундах."); return; }
        var scale = int.Parse(((ComboBoxItem)ScaleBox.SelectedItem).Tag.ToString()!);
        var request = new ProcessingRequest(InputPath.Text, OutputPath.Text, Video2XPath.Text, FFmpegPath.Text,
            FFprobePath.Text, (ProcessingProfile)ProfileBox.SelectedItem, scale, start, duration);
        cancellation = new CancellationTokenSource();
        SettingsPanel.IsEnabled = false; RunButton.IsEnabled = false; CancelButton.IsEnabled = true;
        Progress.Value = 0; Progress.IsIndeterminate = true; LogBox.Clear(); StatusText.Text = "Подготовка и обработка…";
        try
        {
            var result = await processor.ProcessAsync(request, Log, p => Dispatcher.InvokeAsync(() => {
                Progress.IsIndeterminate = false; Progress.Value = p; StatusText.Text = $"Обработка: {p:F0}%";
            }), cancellation.Token);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result, "report.json")));
            var warned = report.RootElement.GetProperty("Status").GetString() == "CompletedWithWarning";
            Progress.IsIndeterminate = false; Progress.Value = 100;
            StatusText.Text = warned ? "Результат проверен, но Video2X завершился со сбоем. См. журнал и report.json." : "Готово. Результат проверен.";
            Log("Готово: " + result);
        }
        catch (OperationCanceledException) { StatusText.Text = "Отменено. Незавершённые файлы остаются в папке задания."; }
        catch (Exception ex) { StatusText.Text = "Ошибка обработки — подробности в журнале"; Log(ex.Message); }
        finally
        {
            Progress.IsIndeterminate = false; SettingsPanel.IsEnabled = true; RunButton.IsEnabled = true;
            CancelButton.IsEnabled = false; cancellation.Dispose(); cancellation = null;
        }
    }
    private static bool TrySeconds(string s, out double value) => double.TryParse(s.Replace(',', '.'), CultureInfo.InvariantCulture, out value);
    private void Cancel(object sender, RoutedEventArgs e) { cancellation?.Cancel(); CancelButton.IsEnabled = false; }
    private void OpenResults(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(OutputPath.Text); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { OutputPath.Text }, UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (cancellation == null) return;
        e.Cancel = true;
        cancellation.Cancel();
        StatusText.Text = "Отменяем обработку. После остановки окно можно закрыть.";
    }
}
