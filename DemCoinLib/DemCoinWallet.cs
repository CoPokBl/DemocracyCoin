using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using GeneralPurposeLib;
using Org.BouncyCastle.Security;

namespace DemCoinLib;

public class DemCoinWallet {
    public ECDsa Creds;
    private const int EntropyBytes = 16;
    private const string EcCurve = "1.2.840.10045.3.1.7";  // ecCurve specifies P-256 curve.

    public ECParameters PublicParams => Creds.ExportParameters(false);
    public byte[] PublicKey => PublicParams.Q.X!.Concat(PublicParams.Q.Y!).ToArray();
    public string Address => DemCoinUtils.PublicKeyToAddress(PublicKey);
    public byte[] AddressBytes => DemCoinUtils.PublicKeyToAddressBytes(PublicKey);
    public string[]? SeedPhraseWords;
    public string? SeedPhrase => SeedPhraseWords == null ? null : string.Join(' ', SeedPhraseWords);

    private DemCoinWallet(ECDsa creds) {
        Creds = creds;
    }

    public static DemCoinWallet Import(string xml) {
        ECDsa ecdsa = ECDsa.Create();
        ecdsa.FromXmlString(xml);
        return new DemCoinWallet(ecdsa);
    }

    public static DemCoinWallet FromPrivateKey(byte[] privKey) {
        return new DemCoinWallet(GenEcDsaFromPrivKey(privKey));
    }

    private static ECDsa GenEcDsaFromPrivKey(byte[] privKey) {
        ECCurve curve = ECCurve.CreateFromValue(EcCurve); // ecCurve specifies P-256 curve.
        return ECDsa.Create(new ECParameters { Curve = curve, D = privKey });
    }

    public static DemCoinWallet NewWithPhrase() {
        byte[] privKey = new byte[EntropyBytes];
        SecureRandom.GetInstance("SHA256PRNG").NextBytes(privKey);
        
        ECCurve curve = ECCurve.CreateFromValue(EcCurve); // ecCurve specifies P-256 curve.
        ECDsa ecdsa = ECDsa.Create(new ECParameters { Curve = curve, D = privKey }); 

        DataWriter writer = new();
        writer.Write(privKey)
              .Write(SHA256.HashData(privKey)[0]);
        byte[] wordData = writer.ToArray();
        Debug.Assert(wordData.Length == 17);  // It must be the entropy plus the checksum byte

        uint[] wordIndexes = DemCoinUtils.SplitBytes(wordData, 11);
        //Debug.Assert(wordIndexes.SequenceEqual(DemCoinUtils.CombineBytes(wordIndexes, 11, EntropyBytes)));
        string[] words = new string[wordIndexes.Length];

        string[] allWords = File.ReadAllLines(Path.Combine("Data", "seedwords.txt"));
        for (int i = 0; i < words.Length; i++) {
            words[i] = allWords[wordIndexes[i]];
        }
        
        return new DemCoinWallet(ecdsa) {
            SeedPhraseWords = words
        };
    }

    public static DemCoinWallet ImportPublic(byte[] publicKey) {
        ECDsa ecdsa = ECDsa.Create();
        ECParameters parameters = new();
        parameters.Q.X = publicKey[..32];  // First 32 bytes are this param, see "PublicKey"
        parameters.Q.Y = publicKey[32..];  // Last 32 bytes are this param, see "PublicKey"
        parameters.Curve = ECCurve.CreateFromValue(EcCurve);
        ecdsa.ImportParameters(parameters);
        return new DemCoinWallet(ecdsa);
    }

    public static DemCoinWallet FromPhrase(string[] words) {
        List<string> allWords = [..File.ReadAllLines(Path.Combine("Data", "seedwords.txt"))];
        uint[] indexes = new uint[words.Length];

        for (int i = 0; i < words.Length; i++) {
            int index = allWords.IndexOf(words[i].ToLower());
            if (index == -1) {
                throw new ArgumentException($"Phrase is not valid, '{words[i]}' is not valid.");
            }
            indexes[i] = (uint) index;
        }

        byte[] wordData = DemCoinUtils.CombineBytes(indexes, 11, EntropyBytes+1);
        byte checksumByte = wordData[^1];

        byte[] privKey = wordData[..^1];
        byte[] keyHash = SHA256.HashData(privKey);

        if (checksumByte != keyHash[0]) {
            throw new ArgumentException($"Phrase is not valid, checksum invalid (expected: {keyHash[0]}, got: {checksumByte})");
        }
        
        DemCoinWallet wal = FromPrivateKey(privKey);
        wal.SeedPhraseWords = words;
        return wal;
    }

    public static DemCoinWallet? ResolvePublic(string address) {
        string? wallet = TryResolveAddress(address);
        if (wallet == null) {
            return null;
        }

        return ImportPublic(Encoding.UTF8.GetBytes(wallet));
    }

    public static string? TryResolveAddress(string address) {
        DomainName domain = DomainName.Parse(address);
        DnsClient client = DnsClient.Default;
        DnsMessage? resp = client.Resolve(domain, RecordType.Txt);
        resp.ThrowIfNull();
        foreach (DnsRecordBase record in resp!.AnswerRecords) {
            if (record is not TxtRecord txt) {
                continue;
            }

            if (!txt.TextData.StartsWith("demcoinwallet=")) {
                continue;
            }

            return txt.TextData.Replace("demcoinwallet=", "");
        }

        return null;
    }

    public string Export() {
        return Creds.ToXmlString(true);
    }

    public byte[] Sign(byte[] data) {
        byte[] hashed = SHA256.HashData(data);
        return Creds.SignHash(hashed);
    }

    public bool ValidateSignature(byte[] data, byte[] signature) {
        byte[] hashed = SHA256.HashData(data);
        return Creds.VerifyHash(hashed, signature);
    }

    public override string ToString() {
        return Export();
    }
}