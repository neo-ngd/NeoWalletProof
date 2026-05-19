using Microsoft.Extensions.Configuration;
using Neo;

namespace NeoWalletProof.Configuration;

internal static class AppBootstrap
{
    private static bool _initialized;

    public static AppSettings Settings { get; private set; } = null!;

    public static void Initialize(string? basePath = null)
    {
        if (_initialized) return;

        basePath ??= AppContext.BaseDirectory;
        var protocolConfig = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("protocol.json", optional: false)
            .Build();
        ProtocolSettings.Initialize(protocolConfig);

        var appConfig = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("config.json", optional: false)
            .Build();
        Settings = new AppSettings(appConfig.GetSection("ApplicationConfiguration"));

        _initialized = true;
    }
}

internal sealed class AppSettings
{
    public UnlockWalletSettings UnlockWallet { get; }

    public AppSettings(IConfigurationSection section)
    {
        UnlockWallet = new UnlockWalletSettings(section.GetSection("UnlockWallet"));
    }
}

internal sealed class UnlockWalletSettings
{
    public string Path { get; }
    public string Password { get; }
    public bool IsActive { get; }

    public UnlockWalletSettings(IConfigurationSection section)
    {
        Path = section.GetSection("Path").Value ?? "";
        Password = section.GetSection("Password").Value ?? "";
        IsActive = bool.TryParse(section.GetSection("IsActive").Value, out var active) && active;
    }
}
