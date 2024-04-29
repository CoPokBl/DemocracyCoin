namespace CoinNode;

public static class Logger {
    public static bool Debugs = true;
    public static bool Infos = true;
    public static bool Errors = true;
    public const bool WhitelistIsBlacklist = true;

    private static readonly string[] SystemWhitelist = [
    ];

    public static bool AllLogs {
        set {
            Debugs = value;
            Infos = value;
            Errors = value;
        }
    }

    private static bool IsSystemAllowed(string system) {
        return WhitelistIsBlacklist ? !SystemWhitelist.Contains(system) : SystemWhitelist.Contains(system);
    }

    public static void Debug(string system, string msg) {
        if (Debugs && IsSystemAllowed(system)) {
            Console.WriteLine($"[{system}] {msg}");
        }
    }

    public static void Info(string system, string msg) {
        if (Infos && IsSystemAllowed(system)) {
            Console.WriteLine($"[{system}] {msg}");
        }
    }
    
    public static void Error(string system, string msg) {
        if (Errors && IsSystemAllowed(system)) {
            Console.WriteLine($"[{system}] {msg}");
        }
    }
    
}