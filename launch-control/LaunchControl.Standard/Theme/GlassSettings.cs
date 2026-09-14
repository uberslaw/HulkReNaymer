using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LaunchControl.Standard.Theme;

public sealed class GlassSettings
{
    public bool Enabled { get; set; }
    public string? BackgroundImagePath { get; set; }
    public double Intensity { get; set; } = 0.65;
    public double BlurRadius { get; set; } = 18;
    public bool Halo { get; set; } = true;

    public GlassSettings Clone() => new()
    {
        Enabled = Enabled,
        BackgroundImagePath = BackgroundImagePath,
        Intensity = Math.Clamp(Intensity, 0, 1),
        BlurRadius = Math.Clamp(BlurRadius, 0, 50),
        Halo = Halo
    };
}

/// <summary>Live bindable glass state stored in Application.Resources["GlassState"].</summary>
public sealed class GlassState : INotifyPropertyChanged
{
    private bool _enabled;
    private ImageSource? _background;
    private double _blurRadius = 18;
    private double _intensity = 0.65;
    private bool _halo = true;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageVisibility));
            OnPropertyChanged(nameof(ScrimOpacity));
            OnPropertyChanged(nameof(OverlayOpacity));
            OnPropertyChanged(nameof(HaloOpacity));
        }
    }

    public ImageSource? Background
    {
        get => _background;
        set
        {
            _background = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageVisibility));
        }
    }

    public double BlurRadius
    {
        get => _blurRadius;
        set
        {
            var next = Math.Clamp(value, 0, 50);
            if (Math.Abs(_blurRadius - next) < 0.01) return;
            _blurRadius = next;
            OnPropertyChanged();
        }
    }

    public double Intensity
    {
        get => _intensity;
        set
        {
            var next = Math.Clamp(value, 0, 1);
            if (Math.Abs(_intensity - next) < 0.001) return;
            _intensity = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScrimOpacity));
            OnPropertyChanged(nameof(OverlayOpacity));
        }
    }

    public bool Halo
    {
        get => _halo;
        set
        {
            if (_halo == value) return;
            _halo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HaloOpacity));
        }
    }

    public Visibility ImageVisibility =>
        Enabled && Background is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Dark scrim over the photo. Higher intensity = more image, less scrim.</summary>
    public double ScrimOpacity => Enabled ? 0.18 + (1 - Intensity) * 0.32 : 0;

    /// <summary>Subtle sheen on glass fills. Keep this low so it does not re-opacify buttons/console.</summary>
    public double OverlayOpacity => Enabled ? 0.14 + Intensity * 0.16 : 0;

    public double HaloOpacity => Enabled && Halo ? 0.85 : 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void CopyFrom(GlassSettings settings, ImageSource? image)
    {
        Enabled = settings.Enabled;
        BlurRadius = settings.BlurRadius;
        Intensity = settings.Intensity;
        Halo = settings.Halo;
        Background = image;
    }

    internal static ImageSource? TryLoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(Path.GetFullPath(path));
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
