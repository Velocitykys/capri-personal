using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;

namespace Capri.Mobile;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime android)
            android.MainViewFactory = () => new MainView();

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class MainView : UserControl
{
    private MobileBrain brain;
    private readonly StackPanel messages = new StackPanel { Spacing = 10 };
    private readonly TextBox composer = new TextBox { Watermark = "Message Capri…", MinHeight = 48, AcceptsReturn = false };
    private readonly Border settingsPanel = new Border
    {
        IsVisible = false,
        Background = new SolidColorBrush(Color.Parse("#1B2231")),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(14),
        Child = new StackPanel { Spacing = 9 }
    };
    private readonly TextBox keyInput = new TextBox { Watermark = "NVIDIA API key", PasswordChar = '•' };
    private readonly TextBox endpointInput = new TextBox { Watermark = "API endpoint" };
    private readonly TextBox modelInput = new TextBox { Watermark = "Model ID" };
    private readonly Button sendButton = new Button { Content = "Send", MinWidth = 76 };
    private ScrollViewer chatScroll;

    internal MainView()
    {
        try
        {
            brain = new MobileBrain();
            InitializeChat();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine("CAPRI MOBILE STARTUP FAILURE: " + error);
            Content = new TextBlock
            {
                Text = "Capri couldn't finish starting.\n\nPlease send this error to the developer:\n\n" + error,
                Margin = new Thickness(20),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14
            };
        }
    }

    private void InitializeChat()
    {
        Background = new SolidColorBrush(Color.Parse("#10131C"));
        endpointInput.Text = brain.Settings.Endpoint;
        modelInput.Text = brain.Settings.Model;
        keyInput.Text = brain.Settings.ApiKey;

        var settingsContent = (StackPanel)settingsPanel.Child;
        settingsContent.Children.Add(new TextBlock { Text = "YOUR NVIDIA CONNECTION", FontWeight = FontWeight.Bold, Foreground = Brush("#8CB8FF") });
        settingsContent.Children.Add(keyInput);
        settingsContent.Children.Add(endpointInput);
        settingsContent.Children.Add(modelInput);
        var saveSettings = new Button { Content = "Save connection" };
        saveSettings.Click += (_, _) => SaveSettings();
        settingsContent.Children.Add(saveSettings);
        settingsContent.Children.Add(new TextBlock
        {
            Text = "Your API key is stored in this app's private data folder. Never share this phone's app data.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#AAB4C8")
        });

        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(new TextBlock { Text = "CAPRI", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brush("#8CB8FF") });
        header.Children.Add(new TextBlock { Text = "Your own little Night City gremlin.", FontSize = 13, Foreground = Brush("#AAB4C8") });
        var settingsButton = new Button { Content = "AI settings", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        settingsButton.Click += (_, _) => settingsPanel.IsVisible = !settingsPanel.IsVisible;
        header.Children.Add(settingsButton);

        chatScroll = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var composerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(composer, 0);
        Grid.SetColumn(sendButton, 1);
        composerRow.Children.Add(composer);
        composerRow.Children.Add(sendButton);
        sendButton.Click += async (_, _) => await SendMessage();
        composer.KeyDown += async (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                await SendMessage();
            }
        };

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(16) };
        Grid.SetRow(header, 0);
        Grid.SetRow(chatScroll, 1);
        var bottom = new StackPanel { Spacing = 10 };
        bottom.Children.Add(settingsPanel);
        bottom.Children.Add(composerRow);
        Grid.SetRow(bottom, 2);
        layout.Children.Add(header);
        layout.Children.Add(chatScroll);
        layout.Children.Add(bottom);
        Content = layout;

        AddMessage("assistant", "Oh, there you are. Took you long enough. I'm still getting my bearings in this phone, choom.");
        foreach (MobileChatLine line in brain.History)
            AddMessage(line.Role, line.Content);
    }

    private static SolidColorBrush Brush(string colour) => new SolidColorBrush(Color.Parse(colour));

    private void SaveSettings()
    {
        brain.Settings.ApiKey = keyInput.Text ?? "";
        brain.Settings.Endpoint = string.IsNullOrWhiteSpace(endpointInput.Text) ? "https://integrate.api.nvidia.com/v1" : endpointInput.Text.Trim();
        brain.Settings.Model = string.IsNullOrWhiteSpace(modelInput.Text) ? "nvidia/nemotron-3.5-super-vl-120b-a12b" : modelInput.Text.Trim();
        brain.SaveSettings();
        settingsPanel.IsVisible = false;
        AddMessage("assistant", "Connection settings saved. My brain's plugged in.");
    }

    private async Task SendMessage()
    {
        string prompt = (composer.Text ?? "").Trim();
        if (prompt.Length == 0) return;
        composer.Text = "";
        sendButton.IsEnabled = false;
        AddMessage("user", prompt);
        try
        {
            string answer = await brain.AskAsync(prompt);
            AddMessage("assistant", answer);
        }
        catch (Exception error)
        {
            AddMessage("assistant", error.Message);
        }
        finally
        {
            sendButton.IsEnabled = true;
            composer.Focus();
        }
    }

    private void AddMessage(string role, string text)
    {
        bool capri = role == "assistant";
        var bubble = new Border
        {
            HorizontalAlignment = capri ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right,
            MaxWidth = 520,
            Background = Brush(capri ? "#1B2231" : "#203A5B"),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(13),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15,
                Foreground = Brush("#F0F3FA")
            }
        };
        messages.Children.Add(bubble);
        chatScroll?.ScrollToEnd();
    }
}
