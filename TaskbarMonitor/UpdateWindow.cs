using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace TaskbarMonitor;

/// <summary>
/// The one update surface: check, download with a progress bar, verify and install.
/// Opened by the taskbar menu, the settings row and the automatic startup check.
/// Colors follow the settings-window theme; the buttons carry their own template
/// because the stock one turns white when disabled.
/// </summary>
public sealed class UpdateWindow : Window
{
    private static UpdateWindow? _open;

    private readonly TextBlock _status;
    private readonly ProgressBar _bar;
    private readonly Button _install;
    private readonly Button _cancel;
    private readonly Button _releases;
    private CancellationTokenSource? _cts;

    public static void ShowUpdate()
    {
        if (_open is { IsVisible: true })
        {
            _open.Activate();
            return;
        }

        _open = new UpdateWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
        _open.Topmost = true;
        _open.Topmost = false;
    }

    private UpdateWindow()
    {
        var dark = App.Settings.Theme switch
        {
            "Light" => false,
            "Dark" => true,
            _ => !Palette.SystemAppsUseLightTheme()
        };
        var colors = Palette.AppColors(dark);
        var ink = new SolidColorBrush(colors["Ink"]);
        var muted = new SolidColorBrush(colors["Muted"]);

        Title = "Taskbar Monitor — update";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 430;
        ShowInTaskbar = true;
        Background = new SolidColorBrush(colors["Surface"]);

        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _bar = new ProgressBar
        {
            Height = 4,
            Minimum = 0,
            Maximum = 100,
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(colors["Accent"]),
            Background = new SolidColorBrush(colors["SliderTrack"]),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 14)
        };

        _install = MakeButton("Download and install", true, colors);
        _install.Visibility = Visibility.Collapsed;
        _install.Click += async (_, _) => await Install();

        _cancel = MakeButton("Cancel", false, colors);
        _cancel.Visibility = Visibility.Collapsed;
        _cancel.Click += (_, _) => _cts?.Cancel();

        _releases = MakeButton("Release page", false, colors);
        _releases.Visibility = Visibility.Collapsed;
        _releases.Click += (_, _) => OpenReleases();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_install);
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_releases);

        var panel = new StackPanel { Margin = new Thickness(22), Width = 386 };
        panel.Children.Add(new TextBlock
        {
            Text = "Taskbar Monitor v" + AppVersion.Display,
            Foreground = ink,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(_status);
        panel.Children.Add(_bar);
        panel.Children.Add(buttons);
        Content = panel;

        Closed += (_, _) => _cts?.Cancel();
        Loaded += async (_, _) => await Check();

        _dark = dark;
    }

    private readonly bool _dark;

    /// <summary>Win10 ignores attribute 20 and needs 19; the title bar must not stay white over the dark body.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!_dark) return;

        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1;
        if (Native.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            Native.DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }

    /// <summary>Stock buttons turn into a white chrome block when disabled; this one keeps its own colors.</summary>
    private static Button MakeButton(string text, bool primary, Dictionary<string, Color> colors)
    {
        var template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="Button">
              <Border x:Name="Chrome" Background="{TemplateBinding Background}" CornerRadius="6"
                      Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                                  TextElement.Foreground="{TemplateBinding Foreground}" />
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter TargetName="Chrome" Property="Opacity" Value="0.85" />
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="Chrome" Property="Opacity" Value="0.7" />
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                  <Setter TargetName="Chrome" Property="Opacity" Value="0.5" />
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);

        return new Button
        {
            Content = text,
            Template = template,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Foreground = primary ? Brushes.White : new SolidColorBrush(colors["Ink"]),
            Background = new SolidColorBrush(primary ? colors["Accent"] : colors["Chip"])
        };
    }

    private static void OpenReleases()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(UpdateService.ReleasesUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("open releases", ex);
        }
    }

    private async Task Check()
    {
        _status.Text = "Checking GitHub…";
        _bar.Visibility = Visibility.Collapsed;
        _install.Visibility = Visibility.Collapsed;
        _releases.Visibility = Visibility.Collapsed;

        var available = await UpdateService.CheckAsync();

        if (available is null)
        {
            _status.Text = "Could not reach GitHub. Check the connection and try again.";
            _releases.Visibility = Visibility.Visible;
            return;
        }

        if (!available.Value)
        {
            _status.Text = $"You have the latest version (v{AppVersion.Display}).";
            return;
        }

        _status.Text = $"v{UpdateService.Latest} is available — v{AppVersion.Display} is installed.";
        _install.Visibility = Visibility.Visible;
    }

    private async Task Install()
    {
        _cts = new CancellationTokenSource();
        _install.IsEnabled = false;
        _cancel.Visibility = Visibility.Visible;
        _bar.Visibility = Visibility.Visible;
        _bar.Value = 0;

        var progress = new Progress<(string Text, int Percent)>(report =>
        {
            _status.Text = report.Text;
            _bar.Value = report.Percent;
        });

        try
        {
            await UpdateService.InstallAsync(progress, _cts.Token);
            App.ExitApp(); // the new copy is already starting with --replace
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Update cancelled.";
            _bar.Visibility = Visibility.Collapsed;
            _cancel.Visibility = Visibility.Collapsed;
            _install.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error("update install", ex);
            _status.Text = "Update failed: " + ex.Message;
            _bar.Visibility = Visibility.Collapsed;
            _cancel.Visibility = Visibility.Collapsed;
            _releases.Visibility = Visibility.Visible;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
}
