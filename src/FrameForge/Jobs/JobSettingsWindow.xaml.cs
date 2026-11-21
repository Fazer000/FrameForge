// Диалог изменения параметров задания.
using System.Windows;

namespace FrameForge;

/// <summary>Редактирует параметры выбранного задания.</summary>
public partial class JobSettingsWindow : Window
{
    private readonly ProcessingRequest request;
    private readonly Action<ProcessingRequest> save;

    /// <summary>Загружает сохранённые параметры задания.</summary>
    public JobSettingsWindow(QueueJob job, Action<ProcessingRequest> save)
    {
        this.save = save;
        request = job.Request;
        InitializeComponent();
        JobNameText.Text = job.Name;
        EditSettings.LoadValues(request.Profile, request.TargetWidth, request.TargetHeight,
            request.EncodingCrf, request.FullVideo, request.KeepTemporaryFiles);
        EditSettings.LoadRange(request);
    }

    /// <summary>Проверяет и сохраняет параметры задания.</summary>
    private void SaveSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            var (width, height) = EditSettings.ReadSize();
            var fullVideo = EditSettings.FullVideo;
            var (start, duration) = EditSettings.ReadRange(fullVideo);
            save(request with
            {
                Profile = EditSettings.Profile,
                TargetWidth = width,
                TargetHeight = height,
                EncodingCrf = EditSettings.EncodingCrf,
                FullVideo = fullVideo,
                Start = start,
                Duration = duration,
                KeepTemporaryFiles = EditSettings.KeepTemporaryFiles
            });
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
