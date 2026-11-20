// Главное окно Frame Forge.
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace FrameForge;

/// <summary>Отображает настройки и очередь обработки.</summary>
public partial class MainWindow : Window
{
    public ObservableCollection<QueueJob> Jobs { get; } = [];
    private CancellationTokenSource? cancellation;
    private bool ready, closeAfterStop;

    /// <summary>Возвращает CRF выбранного профиля кодирования.</summary>
    private int SelectedEncodingCrf => EncodingBox.SelectedItem is ComboBoxItem { Tag: string value }
        ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 22;

    private readonly WorkspaceStore store;
    private WorkspaceState state;
    private readonly ProcessingQueue queue = new(new Video2XProcessor(), LibraryIntegration.PublishAsync);
    /// <summary>Отображает настройки и очередь обработки.</summary>
    public MainWindow()
    {
        InitializeComponent();
        store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameForge"));
        try { state = store.Load(); }
        catch (Exception ex) { MessageBox.Show(ex.Message); state = new(); Application.Current.Shutdown(); return; }
        foreach (var job in state.Jobs) Jobs.Add(job);
        DataContext = this;
        var settings = state.Settings; var tools = Path.Combine(FindRoot(), "tools");
        OutputPath.Text = settings.OutputPath;
        Video2XPath.Text = Discover(settings.Video2X, tools, "video2x.exe");
        FFmpegPath.Text = Discover(settings.FFmpeg, tools, "ffmpeg.exe");
        FFprobePath.Text = Discover(settings.FFprobe, tools, "ffprobe.exe");
        LibraryPath.Text = settings.LibraryPath;
        if (string.IsNullOrEmpty(LibraryPath.Text)) try
            { using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "animelib-desktop", "offline-settings.json"))); LibraryPath.Text = config.RootElement.GetProperty("downloadsPath").GetString(); }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        ProfileBox.ItemsSource = ProcessingProfile.All;
        ProfileBox.SelectedIndex = Math.Clamp(settings.ProfileIndex, 0, ProcessingProfile.All.Length - 1);
        ResolutionBox.SelectedIndex = (settings.Width, settings.Height) switch { (1920, 1080) => 0, (2560, 1440) => 1, (3840, 2160) => 2, _ => 3 };
        TargetWidthBox.Text = settings.Width.ToString(); TargetHeightBox.Text = settings.Height.ToString();
        EncodingBox.SelectedIndex = settings.EncodingCrf switch { 20 => 1, 16 => 2, _ => 0 };
        FullVideoBox.IsChecked = settings.FullVideo; KeepTemporaryBox.IsChecked = settings.KeepTemporary; RangePanel.IsEnabled = !settings.FullVideo;
        ready = true; Log("Очередь: " + store.DirectoryPath + ". Прерванные задания повторяются с начала вручную.");
    }
    /// <summary>Находит корневую папку решения.</summary>
    private static string FindRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "frame-forge.sln"))) return d.FullName; return AppContext.BaseDirectory; }
    /// <summary>Находит исполняемый файл инструмента.</summary>
    private static string Discover(string current, string root, string name) => File.Exists(current) ? current : Directory.Exists(root) ? Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault() ?? "" : "";
    /// <summary>Открывает диалог выбора файла.</summary>
    private static string? Pick(string filter) { var d = new OpenFileDialog { Filter = filter }; return d.ShowDialog() == true ? d.FileName : null; }
    /// <summary>Сохраняет настройки и задания окна.</summary>
    private void Save()
    {
        state.Settings = new()
        {
            OutputPath = OutputPath.Text,
            Video2X = Video2XPath.Text,
            FFmpeg = FFmpegPath.Text,
            FFprobe = FFprobePath.Text,
            LibraryPath = LibraryPath.Text,
            ProfileIndex = ProfileBox.SelectedIndex,
            Width = int.TryParse(TargetWidthBox.Text, out var w) ? w : 1920,
            Height = int.TryParse(TargetHeightBox.Text, out var h) ? h : 1080,
            FullVideo = FullVideoBox.IsChecked == true,
            EncodingCrf = SelectedEncodingCrf,
            KeepTemporary = KeepTemporaryBox.IsChecked == true
        };
        state.Jobs = Jobs.ToList(); store.Save(state);
    }
    /// <summary>Собирает параметры нового задания.</summary>
    private ProcessingRequest Request(string input, LibraryVideo? library = null)
    {
        var (w, h) = ProcessingInput.ReadSize(TargetWidthBox.Text, TargetHeightBox.Text);
        var full = FullVideoBox.IsChecked == true;
        var (start, duration) = ProcessingInput.ReadRange(full, StartSeconds.Text, DurationSeconds.Text);
        if (library != null && !full) throw new ArgumentException("Для замены серии выберите обработку видео целиком.");
        foreach (var tool in new[] { Video2XPath.Text, FFmpegPath.Text, FFprobePath.Text }) if (!File.Exists(tool)) throw new FileNotFoundException("Выберите инструменты в настройках.", tool);
        return new(input, OutputPath.Text, Video2XPath.Text, FFmpegPath.Text, FFprobePath.Text, (ProcessingProfile)ProfileBox.SelectedItem, w, h, start, duration, full, KeepTemporaryBox.IsChecked == true, library, SelectedEncodingCrf);
    }
    /// <summary>Добавляет выбранные файлы в очередь.</summary>
    private void AddFiles(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Multiselect = true, Filter = "Видео|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.ts;*.m4v;*.bin|Все файлы|*.*" }; if (d.ShowDialog() != true) return;
        try { var requests = d.FileNames.Select(f => Request(f)).ToArray(); foreach (var r in requests) Jobs.Add(new() { Request = r }); Save(); } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
    /// <summary>Добавляет выбранные серии AnimeLib.</summary>
    private async void AddLibrary(object sender, RoutedEventArgs e)
    {
        var trigger = (Button)sender;
        trigger.IsEnabled = false;
        try
        {
            var videos = await LibraryIntegration.ReadCatalogAsync(LibraryPath.Text, FFprobePath.Text);
            var list = new ListBox { ItemsSource = videos, SelectionMode = SelectionMode.Extended, Margin = new Thickness(12) };
            var button = new Button { Content = "Добавить выбранные", Margin = new Thickness(12) }; var panel = new DockPanel(); DockPanel.SetDock(button, Dock.Bottom); panel.Children.Add(button); panel.Children.Add(list);
            var dialog = new Window { Title = "Серии AnimeLib", Owner = this, Icon = Icon, Width = 850, Height = 550, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            List<LibraryVideo> chosen = []; button.Click += (_, _) => { chosen = list.SelectedItems.Cast<LibraryVideo>().ToList(); if (chosen.Count > 0) dialog.DialogResult = true; };
            if (dialog.ShowDialog() != true) return;
            SettingsPanel.IsEnabled = false;
            try
            {
                var requests = new List<ProcessingRequest>();
                foreach (var video in chosen)
                {
                    if (File.Exists(video.BackupPath)) throw new IOException("Серия уже заменена или требует восстановления: " + video.Title);
                    var hash = await Task.Run(() => LibraryIntegration.Hash(video.SourcePath)); requests.Add(Request(video.SourcePath, video with { OriginalHash = hash }));
                }
                foreach (var r in requests) Jobs.Add(new() { Request = r }); Save();
            }
            finally { SettingsPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Библиотека AnimeLib"); }
        finally { trigger.IsEnabled = true; }
    }
    /// <summary>Запускает очередь и обновляет окно.</summary>
    private async void Run(object sender, RoutedEventArgs e)
    {
        if (cancellation != null) return;
        cancellation = new(); RunButton.IsEnabled = false; CancelButton.IsEnabled = true;
        try
        {
            Save();
            await queue.RunAsync(Jobs, Save, job =>
            {
                JobsGrid.SelectedItem = job;
                LogBox.Clear();
                StatusText.Text = job.Name;
                Progress.IsIndeterminate = true;
            }, Log, (job, value) => Dispatcher.InvokeAsync(() =>
            {
                job.Progress = value;
                Progress.IsIndeterminate = false;
                Progress.Value = value;
            }), cancellation.Token);
            StatusText.Text = cancellation.IsCancellationRequested ? "Очередь остановлена. Ожидающие задания сохранены." : "Очередь завершена";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Очередь остановлена"); }
        finally { cancellation.Dispose(); cancellation = null; RunButton.IsEnabled = true; CancelButton.IsEnabled = false; Progress.IsIndeterminate = false; if (closeAfterStop) Close(); }
    }
    /// <summary>Возвращает прерванное задание в очередь.</summary>
    private void Retry(object sender, RoutedEventArgs e) { if (JobsGrid.SelectedItem is QueueJob job && job.Status is "Ошибка" or "Отменено" or "Прервано") { job.Status = "Ожидает"; job.Progress = 0; TrySave(); } }
    /// <summary>Убирает выбранное задание из списка.</summary>
    private void Remove(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not QueueJob job || job.Status == "Обработка") return;
        var index = Jobs.IndexOf(job);
        Jobs.RemoveAt(index);
        try { Save(); }
        catch (Exception ex)
        {
            Jobs.Insert(index, job);
            JobsGrid.SelectedItem = job;
            MessageBox.Show("Не удалось убрать задание: " + ex.Message);
            return;
        }

        if (job.Request.Library is { } video && File.Exists(video.BackupPath))
            Log("Задание убрано из очереди. Временный оригинал сохранён: " + video.BackupPath);
    }
    /// <summary>Сохраняет изменения и показывает ошибку.</summary>
    private void TrySave() { try { Save(); } catch (Exception ex) { MessageBox.Show(ex.Message); } }
    /// <summary>Показывает журнал выбранного задания.</summary>
    private void JobSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || cancellation != null || JobsGrid.SelectedItem is not QueueJob job) return;
        StatusText.Text = job.Profile + " · " + job.Range; LogBox.Text = job.Error; var file = Path.Combine(job.ResultPath, "processing.log");
        if (File.Exists(file)) try { var text = File.ReadAllText(file); LogBox.Text += "\n" + text[^Math.Min(text.Length, 50_000)..]; } catch (IOException) { }
    }
    /// <summary>Выбирает папку результатов.</summary>
    private void ChooseOutput(object s, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) OutputPath.Text = d.FolderName; }
    /// <summary>Выбирает папку библиотеки.</summary>
    private void ChooseLibrary(object s, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) LibraryPath.Text = d.FolderName; }
    /// <summary>Выбирает исполняемый файл Video2X.</summary>
    private void ChooseVideo2X(object s, RoutedEventArgs e) { if (Pick("Video2X|video2x.exe") is { } p) Video2XPath.Text = p; }
    /// <summary>Выбирает исполняемый файл FFmpeg.</summary>
    private void ChooseFFmpeg(object s, RoutedEventArgs e) { if (Pick("FFmpeg|ffmpeg.exe") is { } p) FFmpegPath.Text = p; }
    /// <summary>Выбирает исполняемый файл ffprobe.</summary>
    private void ChooseFFprobe(object s, RoutedEventArgs e) { if (Pick("ffprobe|ffprobe.exe") is { } p) FFprobePath.Text = p; }
    /// <summary>Применяет выбранный размер кадра.</summary>
    private void ResolutionChanged(object s, SelectionChangedEventArgs e) { if (TargetWidthBox == null || TargetHeightBox == null || ResolutionBox.SelectedItem is not ComboBoxItem item) return; var tag = item.Tag.ToString()!; TargetWidthBox.IsEnabled = TargetHeightBox.IsEnabled = tag == "custom"; if (tag != "custom") { var sizes = tag.Split(','); TargetWidthBox.Text = sizes[0]; TargetHeightBox.Text = sizes[1]; } }
    /// <summary>Переключает поля диапазона.</summary>
    private void RangeChanged(object s, RoutedEventArgs e) { if (RangePanel != null) RangePanel.IsEnabled = FullVideoBox.IsChecked != true; }
    /// <summary>Запрашивает остановку очереди.</summary>
    private void Cancel(object s, RoutedEventArgs e) { cancellation?.Cancel(); CancelButton.IsEnabled = false; }
    /// <summary>Добавляет строку в журнал окна.</summary>
    private void Log(string line) => Dispatcher.InvokeAsync(() => { if (LogBox.Text.Length > 100_000) LogBox.Text = LogBox.Text[^50_000..]; LogBox.AppendText(line + Environment.NewLine); LogBox.ScrollToEnd(); });
    /// <summary>Открывает папку результатов.</summary>
    private void OpenResults(object s, RoutedEventArgs e) { try { var path = JobsGrid.SelectedItem is QueueJob { ResultPath.Length: > 0 } job ? job.ResultPath : OutputPath.Text; Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(ex.Message); } }
    /// <summary>Останавливает обработку перед закрытием.</summary>
    private void OnClosing(object? s, CancelEventArgs e)
    {
        if (!ready) return;
        if (cancellation != null) { e.Cancel = true; closeAfterStop = true; Cancel(this, new()); StatusText.Text = "Останавливаем процесс и сохраняем очередь…"; return; }
        try { Save(); } catch (Exception ex) { e.Cancel = true; closeAfterStop = false; MessageBox.Show("Не удалось сохранить очередь: " + ex.Message); }
    }
}
