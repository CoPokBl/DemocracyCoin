namespace CoinNode;

public static class Logger {
    public static bool Debugs = true;
    public static bool Infos = true;
    public static bool Errors = true;

    public static bool AllLogs {
        set {
            Debugs = value;
            Infos = value;
            Errors = value;
        }
    }

    public static void Debug(string system, string msg) {
        if (Debugs) {
            Console.WriteLine($"[{system}] {msg}");
        }
    }

    public static void Info(string system, string msg) {
        if (Infos) {
            Console.WriteLine($"[{system}] {msg}");
        }
    }
    
    public static void Error(string system, string msg) {
        if (Errors) {
            Console.WriteLine($"[{system}] {msg}");
        }
    }
    
}