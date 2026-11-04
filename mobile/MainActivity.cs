using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Capri.Mobile;

[Activity(
    Label = "Capri Mobile",
    Theme = "@android:style/Theme.Material.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
