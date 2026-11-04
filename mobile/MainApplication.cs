using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Capri.Mobile;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    // Avalonia's Android application base uses a protected JNI constructor.
    protected MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
