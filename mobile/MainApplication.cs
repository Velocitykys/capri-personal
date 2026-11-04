using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Capri.Mobile;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    protected MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
