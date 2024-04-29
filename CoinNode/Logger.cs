namespace CoinNode;

public static class Logger {
    public static bool EnableDebug = true;
    public static bool EnableInfo = true;
    public static bool EnableError = true;
    public const bool WhitelistIsBlacklist = true;

    private static readonly string[] SystemWhitelist = [
    ];

    public static bool AllLogs {
        set {
            EnableDebug = value;
            EnableInfo = value;
            EnableError = value;
        }
    }

    private static bool IsSystemAllowed(string system) {
        return WhitelistIsBlacklist ? !SystemWhitelist.Contains(system) : SystemWhitelist.Contains(system);
    }

    public static void Debug(string system, string msg) {
        if (EnableDebug && IsSystemAllowed(system)) {
            Console.WriteLine($"[{system}] {msg}");
        }
    }

    public static void Info(string system, string msg) {
        if (EnableInfo && IsSystemAllowed(system)) {
            Console.WriteLine($"[{system}] {msg}");
        }
    }
    
    public static void Error(string system, string msg) {
        if (EnableError && IsSystemAllowed(system)) {
            Console.WriteLine($"[{system}] {msg}");
        }
    }
    
}