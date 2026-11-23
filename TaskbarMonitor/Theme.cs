using System.Windows.Media;
using Microsoft.Win32;

namespace TaskbarMonitor;

public sealed class Palette
{
    public bool Light { get; init; }
    public SolidColorBrush Label { get; init; } = Brushes.Gray;
    public SolidColorBrush Value { get; init; } = Brushes.Black;
    public SolidColorBrush Temp { get; init; } = Brushes.Gray;
    public SolidColorBrush Warning { get; init; } = Brushes.Orange;
    public SolidColorBrush Critical { get; init; } = Brushes.Red;

    public static Palette Create()
    {
        // The overlay follows the Windows taskbar; Settings.Theme only styles
        // the settings window.
        var light = SystemUsesLightTheme();

        Color label, value, temp, warning, critical;
        if (light)
        {
            label = Color.FromRgb(0x1F, 0x6F, 0xB5);
            value = Color.FromRgb(0x1A, 0x1A, 0x1A);
            temp = Color.FromRgb(0x66, 0x66, 0x66);
            warning = Color.FromRgb(0xD9, 0x6C, 0x00);
            critical = Color.FromRgb(0xC6, 0x28, 0x28);
        }
        else
        {
            label = Color.FromRgb(0x6C, 0xB6, 0xFF);
            value = Color.FromRgb(0xF0, 0xF0, 0xF0);
            temp = Color.FromRgb(0xA8, 0xA8, 0xA8);
            warning = Color.FromRgb(0xFF, 0xB7, 0x4D);
            critical = Color.FromRgb(0xFF, 0x6B, 0x6B);
        }

        return new Palette
        {
            Light = light,
            Label = Freeze(new SolidColorBrush(label)),
            Value = Freeze(new SolidColorBrush(value)),
            Temp = Freeze(new SolidColorBrush(temp)),
            Warning = Freeze(new SolidColorBrush(warning)),
            Critical = Freeze(new SolidColorBrush(critical))
        };
    }

    /// <summary>Settings-window colors; the sidebar and title bar stay dark in both themes.</summary>
    public static Dictionary<string, Color> AppColors(bool dark)
    {
        static Color C(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);

        return dark
            ? new Dictionary<string, Color>
            {
                ["Surface"] = C(0x15, 0x16, 0x1A),
                ["Ink"] = C(0xEF, 0xF1, 0xF4),
                ["Muted"] = C(0x9A, 0xA1, 0xAC),
                ["LineSoft"] = C(0x2B, 0x2E, 0x36),
                ["Accent"] = C(0x4A, 0x93, 0xFF),
                ["Chip"] = C(0x23, 0x26, 0x2D),
                ["RowHighlight"] = C(0x1B, 0x24, 0x33),
                ["CardBg"] = C(0x1C, 0x1F, 0x25),
                ["CardBorder"] = C(0x2B, 0x2E, 0x36),
                ["FieldBorder"] = C(0x33, 0x37, 0x41),
                ["SectionFg"] = C(0x8A, 0x8E, 0x98),
                ["BadgeBg"] = C(0x1C, 0x2A, 0x3F),
                ["BadgeInk"] = C(0x7F, 0xB3, 0xFF),
                ["SwitchTrack"] = C(0x3A, 0x3F, 0x4A),
                ["SliderTrack"] = C(0x33, 0x37, 0x41),
                ["HoverBg"] = C(0x23, 0x26, 0x2D),
                ["PressedBg"] = C(0x2B, 0x2E, 0x36)
            }
            : new Dictionary<string, Color>
            {
                ["Surface"] = C(0xF5, 0xF6, 0xF8),
                ["Ink"] = C(0x15, 0x17, 0x1A),
                ["Muted"] = C(0x71, 0x75, 0x7E),
                ["LineSoft"] = C(0xEC, 0xEE, 0xF2),
                ["Accent"] = C(0x2D, 0x7F, 0xF9),
                ["Chip"] = C(0xF0, 0xF2, 0xF6),
                ["RowHighlight"] = C(0xF5, 0xF9, 0xFF),
                ["CardBg"] = C(0xFF, 0xFF, 0xFF),
                ["CardBorder"] = C(0xE6, 0xE8, 0xEC),
                ["FieldBorder"] = C(0xDD, 0xE1, 0xE7),
                ["SectionFg"] = C(0x8A, 0x8E, 0x98),
                ["BadgeBg"] = C(0xE8, 0xF0, 0xFE),
                ["BadgeInk"] = C(0x1F, 0x6F, 0xD0),
                ["SwitchTrack"] = C(0xD6, 0xDA, 0xE1),
                ["SliderTrack"] = C(0xE1, 0xE5, 0xEB),
                ["HoverBg"] = C(0xF4, 0xF6, 0xF9),
                ["PressedBg"] = C(0xE9, 0xED, 0xF3)
            };
    }

    /// <summary>Mock taskbar behind the settings preview; follows the overlay palette.</summary>
    public static Color PreviewBarBackground(bool light) =>
        light
            ? Color.FromRgb(0xEC, 0xEE, 0xF3)
            : Color.FromRgb(0x23, 0x23, 0x29);

    public static bool SystemUsesLightTheme() => ReadTheme("SystemUsesLightTheme");

    /// <summary>Whether Windows itself draws app windows light; used for the settings window.</summary>
    public static bool SystemAppsUseLightTheme() => ReadTheme("AppsUseLightTheme");

    private static bool ReadTheme(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue(valueName) is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
