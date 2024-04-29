using System.Text;

namespace CoinNode;

public static class Utils {
    
    public static string BytesToHex(this IReadOnlyList<byte> bytes) {
        StringBuilder hex = new(bytes.Count * 2);
        foreach (byte b in bytes) {
            hex.Append($"{b:x2}");
        }
        return hex.ToString();
    }

    public static void Announce(string msg, string? system = null) {
        int spaces = 43 - msg.Length;
        int firstSpaces = spaces / 2;
        string middleBar = $"|{RepeatChars(' ', firstSpaces)}{msg}{RepeatChars(' ', spaces - firstSpaces)}|";
        if (system == null) {  // 43
            Console.WriteLine("---------------------------------------------");
            Console.WriteLine(middleBar);
            Console.WriteLine("---------------------------------------------");
        }
        else {
            Logger.Info(system, "---------------------------------------------");
            Logger.Info(system, middleBar);
            Logger.Info(system, "---------------------------------------------");
        }
    }

    public static string RepeatChars(char c, int a) {
        StringBuilder sb = new(a);
        for (int i = 0; i < a; i++) {
            sb.Append(c);
        }
        return sb.ToString();
    }
    
}