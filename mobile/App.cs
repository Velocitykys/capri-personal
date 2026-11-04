using Avalonia;
using Avalonia.Controls;
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
    public MainView()
    {
        Background = new SolidColorBrush(Color.Parse("#10131C"));

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 18,
                Margin = new Thickness(24),
                Children =
                {
                    new TextBlock
                    {
                        Text = "CAPRI",
                        FontSize = 30,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#8CB8FF"))
                    },
                    new TextBlock
                    {
                        Text = "Your own Capri. On your phone.",
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new Border
                    {
                        Background = new SolidColorBrush(Color.Parse("#1B2231")),
                        CornerRadius = new CornerRadius(16),
                        Padding = new Thickness(18),
                        Child = new TextBlock
                        {
                            Text = "Mobile foundation is installed. Next we are bringing across Capri's personality, independent AI connection, private chat history, memory and voice.",
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            LineHeight = 24
                        }
                    },
                    new TextBlock
                    {
                        Text = "This app is separate from Windows Capri. It will keep its own API settings, conversations and memories.",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.Parse("#AAB4C8")),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }
}
