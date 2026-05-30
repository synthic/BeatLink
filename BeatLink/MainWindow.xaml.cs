using System.ComponentModel;
using BeatLink.Game;
using BeatLink.Web;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BeatLink;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set application icon
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/AppIcon.ico");
        AppWindow.SetIcon(iconPath);

        // Set custom title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Set title bar colors
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.Black;
        }

        // Set main window dimensions
        int windowWidth = 640;
        int windowHeight = 480;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = windowWidth;
            presenter.PreferredMinimumHeight = windowHeight;
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(windowWidth, windowHeight));

        // Display a check mark if the user is logged in
        CheckLoginStatus();
    }

    private async void LogInButton_Click(object sender, RoutedEventArgs e)
    {
        LogInButton.IsEnabled = false;

        await Authentication.GetTokenAsync();

        LogInButton.IsEnabled = true;

        CheckLoginStatus();
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectIcon.Opacity = 0;

        var tokenData = Authentication.ReadTokenFile();

        if (Environment.GetCommandLineArgs().Length > 2 && Environment.GetCommandLineArgs()[1].Equals("--token", StringComparison.OrdinalIgnoreCase))
        {
            tokenData.AccessToken = Environment.GetCommandLineArgs()[2];
            tokenData.ExpiresAt = DateTime.UtcNow.AddDays(7);
        }

        if (tokenData.ExpiresAt > DateTime.UtcNow)
        {
            try
            {
                Memory.ApplyPatches(tokenData.AccessToken);

                ConnectIcon.Opacity = 1.0;
            }
            catch (IndexOutOfRangeException)
            {
                DisplayCustomDialog("Window Not Found", "Please open Mirror's Edge™ Catalyst and try again.");
            }
            catch (Win32Exception ex)
            {
                DisplayCustomDialog("Error", ex.Message);
            }
        }
        else
        {
            DisplayCustomDialog("Session Expired", "Your session has expired. Please log in again.");
        }
    }

    private async void DisplayCustomDialog(string title, string content)
    {
        ContentDialog customDialog = new()
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot // Required in WinUI 3
        };

        await customDialog.ShowAsync();
    }

    private void CheckLoginStatus()
    {
        var tokenData = Authentication.ReadTokenFile();

        if (tokenData.ExpiresAt > DateTime.UtcNow)
        {
            LogInIcon.Opacity = 1.0;
        }
    }
}
