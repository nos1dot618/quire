using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Quire.Storage;

namespace Quire.UI;

/// <summary>
/// Applies the light, dark, or high-contrast palette and keeps it in step with Windows:
/// "System" follows the Windows app mode, and a Windows high-contrast theme always wins so
/// accessibility settings are respected. Also colors the native title bar to match.
/// </summary>
public static class ThemeManager
{
    private static readonly Uri LightPalette = new("pack://application:,,,/Quire;component/Themes/Palettes/Light.xaml");
    private static readonly Uri DarkPalette = new("pack://application:,,,/Quire;component/Themes/Palettes/Dark.xaml");

    private static ThemeChoice _choice = ThemeChoice.System;
    private static ResourceDictionary? _activePalette;
    private static bool _listening;

    public static bool IsDark { get; private set; }

    public static event EventHandler? ThemeChanged;

    public static void Apply(ThemeChoice choice)
    {
        _choice = choice;
        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        }
        Refresh();
    }

    private static void Refresh()
    {
        var resources = Application.Current.Resources;
        var highContrast = SystemParameters.HighContrast;
        IsDark = !highContrast && (_choice == ThemeChoice.Dark || (_choice == ThemeChoice.System && WindowsAppsUseDarkMode()));

        var palette = highContrast ? HighContrastPalette() : new ResourceDictionary { Source = IsDark ? DarkPalette : LightPalette };
        if (_activePalette is not null)
            resources.MergedDictionaries.Remove(_activePalette);
        // Palettes go first so the style dictionaries that reference them resolve against them.
        resources.MergedDictionaries.Insert(0, palette);
        _activePalette = palette;

        foreach (Window window in Application.Current.Windows)
            ApplyTitleBar(window);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Builds the palette from the user's Windows contrast colors.</summary>
    private static ResourceDictionary HighContrastPalette() => new()
    {
        ["Quire.Background"] = SystemColors.WindowBrush,
        ["Quire.Text"] = SystemColors.WindowTextBrush,
        ["Quire.Muted"] = SystemColors.GrayTextBrush,
        ["Quire.Rule"] = SystemColors.WindowTextBrush,
        ["Quire.CodeBackground"] = SystemColors.WindowBrush,
        ["Quire.Canvas"] = SystemColors.WindowBrush,
        ["Quire.CellBackground"] = SystemColors.WindowBrush,
        ["Quire.CellHeader"] = SystemColors.WindowBrush,
        ["Quire.CellBorder"] = SystemColors.WindowTextBrush,
        ["Quire.CellBorderHover"] = SystemColors.HighlightBrush,
        ["Quire.Accent"] = SystemColors.HighlightBrush,
        ["Quire.Link"] = SystemColors.HotTrackBrush,
        ["Quire.TextSelection"] = SystemColors.HighlightBrush,
        ["Quire.Chrome"] = SystemColors.MenuBrush,
        ["Quire.ChromeHover"] = SystemColors.HighlightBrush,
        ["Quire.ChromeText"] = SystemColors.MenuTextBrush,
        ["Quire.ChromeDisabled"] = SystemColors.GrayTextBrush,
        ["Quire.PopupBorder"] = SystemColors.WindowTextBrush,
        ["Quire.ScrollThumb"] = SystemColors.WindowTextBrush,
        ["Quire.ScrollThumbHover"] = SystemColors.HighlightBrush,
        ["SyntaxKeywordBrush"] = SystemColors.WindowTextBrush,
        ["SyntaxStringBrush"] = SystemColors.WindowTextBrush,
        ["SyntaxCommentBrush"] = SystemColors.GrayTextBrush,
        ["SyntaxNumberBrush"] = SystemColors.WindowTextBrush,
    };

    /// <summary>Reads Settings > Personalization > Colors > "Choose your default app mode". Absent on old builds: light.</summary>
    public static bool WindowsAppsUseDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks Windows to draw a dark or light title bar. The attribute id is 20 on Windows 10 20H1 and
    /// later (including Windows 11) and 19 on Windows 10 1809 to 1909; older builds ignore it.
    /// </summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        var dark = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.SourceInitialized -= OnSourceInitialized;
            ApplyTitleBar(window);
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
            Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    private static void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
