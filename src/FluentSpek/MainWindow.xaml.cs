using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Spek.Net;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using SpekView = Spek.Net.WinUI.SpekView;

namespace FluentSpek;

public sealed partial class MainWindow : Window
{
    private static readonly string[] AudioExtensions =
    [
        ".aac", ".aif", ".aiff", ".alac", ".ape", ".dsf", ".flac", ".m4a", ".mka", ".mkv", ".mp2", ".mp3", ".mp4",
        ".mpc", ".oga", ".ogg", ".opus", ".tta", ".wav", ".webm", ".wma", ".wv",
    ];

    private readonly AppSettings settings;

    public MainWindow(string? filePath)
    {
        InitializeComponent();
        settings = AppSettings.Load();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RestorePlacement();

        Spek.BrandName = AppInfo.Name;
        Spek.BrandVersion = AppInfo.Version;
        Spek.FfmpegPath = AppSettings.FindFfmpeg(settings);
        Spek.Palette = settings.Palette;
        Spek.WindowFunction = settings.WindowFunction;
        Spek.FftBits = settings.FftBits;
        Spek.LowerDb = settings.LowerDb;
        Spek.UpperDb = settings.UpperDb;
        Spek.ShowFullPath = settings.ShowFullPath;
        Spek.ShowDetailedDescription = settings.ShowDetailedDescription;

        foreach (var property in new[]
        {
            SpekView.PaletteProperty, SpekView.WindowFunctionProperty, SpekView.FftBitsProperty, SpekView.ChannelProperty,
            SpekView.StreamProperty, SpekView.ChannelCountProperty, SpekView.StreamCountProperty, SpekView.FilePathProperty,
        })
        {
            Spek.RegisterPropertyChangedCallback(property, (_, _) => UpdateCommands());
        }
        UpdateCommands();

        Closed += (_, _) => SaveSettings();
        if (filePath is not null) OpenFile(Path.GetFullPath(filePath));
        Root.Loaded += (_, _) => Spek.Focus(FocusState.Programmatic);
    }

    private void OpenFile(string path)
    {
        Spek.FilePath = path;
        Title = $"{Path.GetFileName(path)} - {AppInfo.Name}";
        AppTitleBar.Subtitle = Path.GetFileName(path);
        Spek.Focus(FocusState.Programmatic);
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        foreach (var extension in AudioExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }
        picker.FileTypeFilter.Add("*");

        var result = await picker.PickSingleFileAsync();
        if (result is not null) OpenFile(result.Path);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Spek.FilePath)) return;

        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(Spek.FilePath),
            DefaultFileExtension = ".png",
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);

        var result = await picker.PickSaveFileAsync();
        if (result is not null) await Spek.SaveAsync(result.Path);
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.OfType<StorageFile>().FirstOrDefault() is { } file) OpenFile(file.Path);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        FullPathToggle.IsOn = Spek.ShowFullPath;
        DetailedToggle.IsOn = Spek.ShowDetailedDescription;
        UpperBox.Value = Spek.UpperDb;
        LowerBox.Value = Spek.LowerDb;
        FfmpegBox.Text = settings.FfmpegPath ?? "";
        FfmpegStatus.Text = Spek.FfmpegPath is { } found ? $"Using {found}" : "ffmpeg wasn't found.";

        SettingsDialog.XamlRoot = Content.XamlRoot;
        await SettingsDialog.ShowAsync();

        Spek.ShowFullPath = FullPathToggle.IsOn;
        Spek.ShowDetailedDescription = DetailedToggle.IsOn;
        int upper = double.IsNaN(UpperBox.Value) ? Spek.UpperDb : (int)UpperBox.Value;
        int lower = double.IsNaN(LowerBox.Value) ? Spek.LowerDb : (int)LowerBox.Value;
        if (lower < upper)
        {
            Spek.LowerDb = Math.Max(lower, SpekDefaults.MinDb);
            Spek.UpperDb = Math.Min(upper, SpekDefaults.MaxDb);
        }

        var ffmpeg = FfmpegBox.Text.Trim().Trim('"');
        settings.FfmpegPath = ffmpeg.Length == 0 ? null : ffmpeg;
        var resolved = AppSettings.FindFfmpeg(settings);
        if (resolved != Spek.FfmpegPath) Spek.FfmpegPath = resolved;

        SaveSettings();
        Spek.Focus(FocusState.Programmatic);
    }

    private void Menu_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        menu.Items.Clear();

        if (menu == PaletteMenu)
        {
            foreach (var palette in Enum.GetValues<Palette>())
                Add(menu, Palettes.DisplayName(palette), Spek.Palette == palette, () => Spek.Palette = palette);
        }
        else if (menu == WindowMenu)
        {
            foreach (var function in Enum.GetValues<WindowFunction>())
                Add(menu, WindowFunctions.DisplayName(function), Spek.WindowFunction == function, () => Spek.WindowFunction = function);
        }
        else if (menu == FftMenu)
        {
            for (int bits = SpekDefaults.MinFftBits; bits <= SpekDefaults.MaxFftBits; bits++)
            {
                int value = bits;
                Add(menu, $"{1 << bits} samples", Spek.FftBits == bits, () => Spek.FftBits = value);
            }
        }
        else if (menu == ChannelMenu)
        {
            for (int channel = 0; channel < Spek.ChannelCount; channel++)
            {
                int value = channel;
                Add(menu, $"Channel {channel + 1}", Spek.Channel == channel, () => Spek.Channel = value);
            }
        }
        else if (menu == StreamMenu)
        {
            for (int stream = 0; stream < Spek.StreamCount; stream++)
            {
                int value = stream;
                Add(menu, $"Stream {stream + 1}", Spek.Stream == stream, () => Spek.Stream = value);
            }
        }

        static void Add(MenuFlyout menu, string text, bool isChecked, Action select)
        {
            var item = new RadioMenuFlyoutItem { Text = text, IsChecked = isChecked, GroupName = menu.GetHashCode().ToString() };
            item.Click += (_, _) => select();
            menu.Items.Add(item);
        }
    }

    private void UpdateCommands()
    {
        PaletteButton.Label = Palettes.DisplayName(Spek.Palette);
        WindowButton.Label = WindowFunctions.DisplayName(Spek.WindowFunction);
        FftButton.Label = $"{1 << Spek.FftBits}";
        ChannelButton.Label = Spek.ChannelCount > 0 ? $"Channel {Spek.Channel + 1} / {Spek.ChannelCount}" : "Channel";
        ChannelButton.IsEnabled = Spek.ChannelCount > 1;
        StreamButton.Label = $"Stream {Spek.Stream + 1} / {Spek.StreamCount}";
        StreamButton.Visibility = Spek.StreamCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = !string.IsNullOrEmpty(Spek.FilePath);
    }

    private void RestorePlacement()
    {
        if (settings.WindowWidth is int width && settings.WindowHeight is int height && settings.WindowX is int x && settings.WindowY is int y)
        {
            var rect = new RectInt32(x, y, width, height);
            if (DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is not null)
            {
                AppWindow.MoveAndResize(rect);
                if (settings.WindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
                return;
            }
        }

        double scale = GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(680 * scale)));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void SaveSettings()
    {
        settings.Palette = Spek.Palette;
        settings.WindowFunction = Spek.WindowFunction;
        settings.FftBits = Spek.FftBits;
        settings.LowerDb = Spek.LowerDb;
        settings.UpperDb = Spek.UpperDb;
        settings.ShowFullPath = Spek.ShowFullPath;
        settings.ShowDetailedDescription = Spek.ShowDetailedDescription;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            settings.WindowMaximized = presenter.State == OverlappedPresenterState.Maximized;
            if (presenter.State == OverlappedPresenterState.Restored)
            {
                settings.WindowX = AppWindow.Position.X;
                settings.WindowY = AppWindow.Position.Y;
                settings.WindowWidth = AppWindow.Size.Width;
                settings.WindowHeight = AppWindow.Size.Height;
            }
        }
        settings.Save();
    }
}
