using System;
using Avalonia;

namespace MkLink
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Services.AdminDragDropHelper.EnableProcessWideFilter();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
