namespace G9MAUIControls.Helpers;

public static class G9ServiceProvider
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services
    {
        get
        {
            if (_services == null)
            {
                throw new InvalidOperationException(
                    "G9ServiceProvider.Services is not initialized. " +
                    "Call G9ServiceProvider.Initialize(app.Services) in MauiProgram.CreateMauiApp() after builder.Build().");
            }

            return _services;
        }
        private set => _services = value;
    }

    public static void Initialize(IServiceProvider services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (_services != null)
        {
            return;
        }

        Services = services;
    }

    public static T GetService<T>() where T : class
    {
        return Services.GetRequiredService<T>();
    }

    public static T? GetServiceNullable<T>() where T : class
    {
        return Services.GetService<T>();
    }
}