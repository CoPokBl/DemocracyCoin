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
    
}