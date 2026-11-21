// Поля параметров обработки.
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FrameForge;

/// <summary>Показывает параметры обработки нового или выбранного задания.</summary>
public partial class ProcessingSettingsPanel : UserControl
{
    /// <summary>Создаёт панель параметров.</summary>
    public ProcessingSettingsPanel()
    {
        InitializeComponent();
        ProfileBox.ItemsSource = ProcessingProfile.MenuOrder;
    }

    public ProcessingProfile Profile => (ProcessingProfile)ProfileBox.SelectedItem;
    public int EncodingCrf => (int)EncodingBox.SelectedValue;
    public bool FullVideo => FullVideoBox.IsChecked == true;
    public bool KeepTemporaryFiles => KeepTemporaryBox.IsChecked == true;
    public string WidthText => TargetWidthBox.Text;
    public string HeightText => TargetHeightBox.Text;

    /// <summary>Загружает параметры обработки.</summary>
    public void LoadValues(ProcessingProfile profile, int width, int height, int encodingCrf,
        bool fullVideo, bool keepTemporaryFiles)
    {
        var menuProfile = ProcessingProfile.MenuOrder.FirstOrDefault(item =>
            item.Processor == profile.Processor && item.Model == profile.Model && item.Noise == profile.Noise);
        ProfileBox.ItemsSource = menuProfile == null ? ProcessingProfile.MenuOrder.Append(profile) : ProcessingProfile.MenuOrder;
        ProfileBox.SelectedItem = menuProfile ?? profile;

        EncodingBox.ItemsSource = new[] { 22, 20, 16 }.Append(encodingCrf).Distinct()
            .Select(crf => new ComboBoxItem { Tag = crf, Content = EncodingPreset.Describe(crf) }).ToArray();
        EncodingBox.SelectedValue = encodingCrf;

        ResolutionBox.SelectedIndex = (width, height) switch { (1920, 1080) => 0, (2560, 1440) => 1, (3840, 2160) => 2, _ => 3 };
        TargetWidthBox.Text = width.ToString(CultureInfo.InvariantCulture);
        TargetHeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        FullVideoBox.IsChecked = fullVideo;
        KeepTemporaryBox.IsChecked = keepTemporaryFiles;
        RangePanel.IsEnabled = !fullVideo;
    }

    /// <summary>Загружает диапазон и ограничивает серию обработкой целиком.</summary>
    public void LoadRange(ProcessingRequest request)
    {
        if (!request.FullVideo && request.Library == null)
        {
            StartSeconds.Text = request.Start.ToString(CultureInfo.InvariantCulture);
            DurationSeconds.Text = request.Duration.ToString(CultureInfo.InvariantCulture);
        }
        if (request.Library != null)
        {
            FullVideoBox.IsChecked = true;
            FullVideoBox.IsEnabled = false;
        }
    }

    /// <summary>Проверяет размеры кадра.</summary>
    public (int Width, int Height) ReadSize() => ProcessingInput.ReadSize(WidthText, HeightText);

    /// <summary>Проверяет диапазон обработки.</summary>
    public (double Start, double Duration) ReadRange(bool fullVideo) =>
        ProcessingInput.ReadRange(fullVideo, StartSeconds.Text, DurationSeconds.Text);

    /// <summary>Применяет выбранный размер кадра.</summary>
    private void ResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetWidthBox == null || TargetHeightBox == null ||
            ResolutionBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;

        TargetWidthBox.IsEnabled = TargetHeightBox.IsEnabled = tag == "custom";
        if (tag == "custom") return;

        var sizes = tag.Split(',');
        TargetWidthBox.Text = sizes[0];
        TargetHeightBox.Text = sizes[1];
    }

    /// <summary>Переключает поля выбора фрагмента.</summary>
    private void RangeChanged(object sender, RoutedEventArgs e)
    {
        if (RangePanel != null) RangePanel.IsEnabled = !FullVideo;
    }
}
