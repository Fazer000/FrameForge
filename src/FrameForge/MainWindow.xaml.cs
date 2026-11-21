// Главное окно Frame Forge.
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    private readonly string toolsDirectory = Path.Combine(FindRoot(), "tools");
    private WorkspaceState state;
    private readonly ProcessingQueue queue = new(new Video2XProcessor(), LibraryIntegration.PublishAsync);
    /// <summary>Отображает настройки и очередь обработки.</summary>
    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        VersionText.Text = version?.Split('+', 2)[0] ?? "";
        store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameForge"));
        try { state = store.Load(); }
        catch (Exception ex) { MessageBox.Show(ex.Message); state = new(); Application.Current.Shutdown(); return; }
        foreach (var job in state.Jobs) Jobs.Add(job);
        DataContext = this;
        var settings = state.Settings;
        OutputPath.Text = settings.OutputPath;
        LibraryPath.Text = settings.LibraryPath;
        if (string.IsNullOrEmpty(LibraryPath.Text)) try
            { using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "animelib-desktop", "offline-settings.json"))); LibraryPath.Text = config.RootElement.GetProperty("downloadsPath").GetString(); }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        ProfileBox.ItemsSource = ProcessingProfile.MenuOrder;
        ProfileBox.SelectedItem = ProcessingProfile.All[Math.Clamp(settings.ProfileIndex, 0, ProcessingProfile.All.Length - 1)];
        ResolutionBox.SelectedIndex = (settings.Width, settings.Height) switch { (1920, 1080) => 0, (2560, 1440) => 1, (3840, 2160) => 2, _ => 3 };
        TargetWidthBox.Text = settings.Width.ToString(); TargetHeightBox.Text = settings.Height.ToString();
        EncodingBox.SelectedIndex = settings.EncodingCrf switch { 20 => 1, 16 => 2, _ => 0 };
        FullVideoBox.IsChecked = settings.FullVideo; KeepTemporaryBox.IsChecked = settings.KeepTemporary; RangePanel.IsEnabled = !settings.FullVideo;
        ready = true; Log("Очередь: " + store.DirectoryPath + ". Прерванные задания повторяются с начала вручную.");
    }
    /// <summary>Находит корневую папку решения.</summary>
    private static string FindRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "frame-forge.sln"))) return d.FullName; return AppContext.BaseDirectory; }
    /// <summary>Находит исполняемый файл инструмента.</summary>
    private static string Discover(string current, string root, string name)
    {
        var folder = name == "video2x.exe" ? "video2x" : "ffmpeg";
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", folder, name);
        if (File.Exists(bundled)) return bundled;
        if (File.Exists(current)) return current;
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault() ?? ""
            : "";
    }
    /// <summary>Сохраняет настройки и задания окна.</summary>
    private void Save()
    {
        state.Settings = new()
        {
            OutputPath = OutputPath.Text,
            Video2X = state.Settings.Video2X,
            FFmpeg = state.Settings.FFmpeg,
            FFprobe = state.Settings.FFprobe,
            LibraryPath = LibraryPath.Text,
            ProfileIndex = Array.IndexOf(ProcessingProfile.All, (ProcessingProfile)ProfileBox.SelectedItem),
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
        var full = library != null || FullVideoBox.IsChecked == true;
        var (start, duration) = ProcessingInput.ReadRange(full, StartSeconds.Text, DurationSeconds.Text);
        var settings = state.Settings;
        var video2x = Discover(settings.Video2X, toolsDirectory, "video2x.exe");
        var ffmpeg = Discover(settings.FFmpeg, toolsDirectory, "ffmpeg.exe");
        var ffprobe = Discover(settings.FFprobe, toolsDirectory, "ffprobe.exe");
        (string Name, string Path)[] tools = [("video2x.exe", video2x), ("ffmpeg.exe", ffmpeg), ("ffprobe.exe", ffprobe)];
        var missing = tools.Where(tool => !File.Exists(tool.Path)).Select(tool => tool.Name).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException($"Не найдены файлы «{string.Join(", ", missing)}». Распакуйте инструменты в папку {toolsDirectory}.");

        return new(input, OutputPath.Text, video2x, ffmpeg, ffprobe, (ProcessingProfile)ProfileBox.SelectedItem, w, h, start, duration, full, KeepTemporaryBox.IsChecked == true, library, SelectedEncodingCrf);
    }
    /// <summary>Добавляет выбранные файлы в очередь.</summary>
    private async void AddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Видео|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.ts;*.m4v;*.bin|Все файлы|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        var files = dialog.FileNames;
        try
        {
            await WhileLoadingAsync(() =>
            {
                var template = Request(files[0]);
                Enqueue(files.Select(file => template with { Input = file }));
                return Task.FromResult(true);
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message); }
    }
    /// <summary>Добавляет выбранные серии AnimeLib.</summary>
    private async void AddLibrary(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = LibraryPath.Text;
            var savedProbe = state.Settings.FFprobe;
            var videos = await WhileLoadingAsync(() => Task.Run(() =>
            {
                var ffprobe = Discover(savedProbe, toolsDirectory, "ffprobe.exe");
                return LibraryIntegration.ReadCatalogAsync(directory, ffprobe);
            }));
            var list = new ListBox { ItemsSource = videos, SelectionMode = SelectionMode.Extended, Margin = new Thickness(12) };
            var button = new Button { Content = "Добавить выбранные", Margin = new Thickness(12) }; var panel = new DockPanel(); DockPanel.SetDock(button, Dock.Bottom); panel.Children.Add(button); panel.Children.Add(list);
            var dialog = new Window { Title = "Серии AnimeLib", Owner = this, Icon = Icon, Background = Background, Width = 850, Height = 550, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            List<LibraryVideo> chosen = []; button.Click += (_, _) => { chosen = list.SelectedItems.Cast<LibraryVideo>().ToList(); if (chosen.Count > 0) dialog.DialogResult = true; };
            if (dialog.ShowDialog() != true) return;
            await WhileLoadingAsync(async () =>
            {
                var template = Request(chosen[0].SourcePath, chosen[0]);
                var requests = await Task.Run(() => chosen.Select(video =>
                {
                    if (video.ExistingBackupPath != null)
                        throw new IOException("Серия уже заменена или требует восстановления: " + video.Title);
                    var hash = LibraryIntegration.Hash(video.SourcePath);
                    return template with { Input = video.SourcePath, Library = video with { OriginalHash = hash } };
                }).ToArray());
                Enqueue(requests);
                return true;
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Библиотека AnimeLib"); }
    }

    /// <summary>Блокирует окно до окончания загрузки файлов.</summary>
    private async Task<T> WhileLoadingAsync<T>(Func<Task<T>> load)
    {
        WorkspacePanel.IsEnabled = false;
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            return await load();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            WorkspacePanel.IsEnabled = true;
        }
    }

    /// <summary>Добавляет и сохраняет новую группу заданий.</summary>
    private void Enqueue(IEnumerable<ProcessingRequest> requests)
    {
        var added = requests.Select(request => new QueueJob { Request = request }).ToArray();
        foreach (var job in added) Jobs.Add(job);
        try { Save(); }
        catch
        {
            foreach (var job in added) Jobs.Remove(job);
            throw;
        }
    }
    /// <summary>Запускает очередь и обновляет окно.</summary>
    private async void Run(object sender, RoutedEventArgs e)
    {
        if (cancellation != null) return;
        if (!Jobs.Any(job => job.Status == "Ожидает"))
        {
            StatusText.Text = "Нет ожидающих заданий";
            return;
        }

        cancellation = new();
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        var started = new HashSet<QueueJob>();
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => ShowQueueMetrics(started, clock.Elapsed, estimate: !cancellation.IsCancellationRequested);
        ShowQueueMetrics(started, clock.Elapsed, estimate: true);
        timer.Start();
        var finished = false;
        try
        {
            Save();
            await queue.RunAsync(Jobs, Save, job =>
            {
                started.Add(job);
                LogBox.Clear();
                StatusText.Text = job.Name;
            }, Log, (job, value) => Dispatcher.InvokeAsync(() =>
            {
                if (job.Status == "Обработка") job.Progress = Math.Min(value, 99);
            }), cancellation.Token);
            finished = !cancellation.IsCancellationRequested;
            StatusText.Text = finished ? "Очередь завершена" : "Очередь остановлена. Ожидающие задания сохранены.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Очередь остановлена";
            MessageBox.Show(this, ex.Message, "Очередь остановлена");
        }
        finally
        {
            timer.Stop();
            clock.Stop();
            ShowQueueMetrics(started, clock.Elapsed, estimate: finished);
            cancellation.Dispose();
            cancellation = null;
            RunButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            if (closeAfterStop) Close();
        }
    }

    /// <summary>Обновляет показатели текущего запуска очереди.</summary>
    private void ShowQueueMetrics(IEnumerable<QueueJob> started, TimeSpan elapsed, bool estimate)
    {
        var metrics = QueueRunProgress.Calculate(started, Jobs, elapsed);
        Progress.Value = metrics.Percent;
        var remaining = estimate && metrics.Remaining is { } time ? QueueRunProgress.FormatDuration(time) : "—";
        QueueMetricsText.Text = $"Выполнено: {metrics.Percent:0.##} %   ·   Прошло: {QueueRunProgress.FormatDuration(elapsed)}   ·   Осталось: {remaining}";
    }

    /// <summary>Возвращает выбранные незавершённые задания в очередь.</summary>
    private void Retry(object sender, RoutedEventArgs e)
    {
        var selected = JobsGrid.SelectedItems.Cast<QueueJob>()
            .Where(job => job.Status is "Ошибка" or "Отменено" or "Прервано")
            .Select(job => (Job: job, job.Status, job.Progress)).ToArray();
        if (selected.Length == 0) return;

        foreach (var item in selected)
        {
            item.Job.Status = "Ожидает";
            item.Job.Progress = 0;
        }

        try { Save(); }
        catch (Exception ex)
        {
            foreach (var item in selected)
            {
                item.Job.Status = item.Status;
                item.Job.Progress = item.Progress;
            }
            MessageBox.Show(this, "Не удалось повторить задания: " + ex.Message);
        }
    }
    /// <summary>Убирает выбранные задания из очереди.</summary>
    private void Remove(object sender, RoutedEventArgs e)
    {
        var selected = JobsGrid.SelectedItems.Cast<QueueJob>()
            .Where(job => job.Status != "Обработка")
            .Select(job => (Job: job, Index: Jobs.IndexOf(job)))
            .OrderByDescending(item => item.Index).ToArray();
        if (selected.Length == 0) return;

        foreach (var item in selected)
            Jobs.RemoveAt(item.Index);

        try { Save(); }
        catch (Exception ex)
        {
            foreach (var item in selected.Reverse())
            {
                Jobs.Insert(item.Index, item.Job);
                JobsGrid.SelectedItems.Add(item.Job);
            }
            MessageBox.Show(this, ex.Message, "Не удалось убрать задания");
        }
    }
    /// <summary>Показывает журнал выбранного задания.</summary>
    private void JobSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || cancellation != null || JobsGrid.SelectedItems.Count != 1 ||
            JobsGrid.SelectedItem is not QueueJob job) return;
        LogBox.Text = job.Error;
        if (string.IsNullOrEmpty(job.ResultPath)) return;
        var file = Path.Combine(job.ResultPath, "processing.log");
        if (File.Exists(file)) try { var text = File.ReadAllText(file); LogBox.Text += "\n" + text[^Math.Min(text.Length, 50_000)..]; } catch (IOException) { }
    }
    /// <summary>Выбирает папку результатов.</summary>
    private void ChooseOutput(object s, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) OutputPath.Text = d.FolderName; }
    /// <summary>Выбирает папку библиотеки.</summary>
    private void ChooseLibrary(object s, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) LibraryPath.Text = d.FolderName; }
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
        if (LoadingOverlay.Visibility == Visibility.Visible) { e.Cancel = true; return; }
        if (cancellation != null) { e.Cancel = true; closeAfterStop = true; Cancel(this, new()); StatusText.Text = "Останавливаем процесс и сохраняем очередь…"; return; }
        try { Save(); } catch (Exception ex) { e.Cancel = true; closeAfterStop = false; MessageBox.Show("Не удалось сохранить очередь: " + ex.Message); }
    }
}
