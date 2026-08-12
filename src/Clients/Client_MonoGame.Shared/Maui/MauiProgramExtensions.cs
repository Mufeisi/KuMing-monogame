using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

namespace MonoShare;

public static class MauiProgramExtensions
{
    public static MauiAppBuilder UseSharedMauiApp(this MauiAppBuilder builder)
    {
        builder.UseMauiApp<MonoShare.Maui.MirMobileApp>();
        builder.Services.AddSingleton<MonoShare.Maui.Services.MobileBootstrapCoordinator>();
        return builder;
    }
}
