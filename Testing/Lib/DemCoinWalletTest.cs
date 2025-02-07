using DemCoinLib;

namespace Testing.Lib;

public class DemCoinWalletTest {

    [Test]
    public void DomainResolve() {
        string? resolved = DemCoinWallet.TryResolveAddress("demcointest.copokbl.net");
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved, Is.EqualTo("abc"));
    }

    [Test]
    public void SeedPhraseGenAndRestore() {
        DemCoinWallet wallet = DemCoinWallet.NewWithPhrase();
        Console.WriteLine("Seed: " + wallet.SeedPhrase);
        Console.WriteLine("Address: " + wallet.Address);

        DemCoinWallet wallet2 = DemCoinWallet.FromPhrase(wallet.SeedPhraseWords!);
        Assert.That(wallet2.Address, Is.EqualTo(wallet.Address));
    }

    [Test]
    public void GenValidAddress() {
        DemCoinWallet wallet = DemCoinWallet.NewWithPhrase();
        Assert.That(DemCoinUtils.IsAddressValid(wallet.Address), Is.True);
    }

    [Test]
    public void SignAndVerify() {
        DemCoinWallet wallet = DemCoinWallet.NewWithPhrase();
        byte[] data = new byte[12];
        Random.Shared.NextBytes(data);

        byte[] sig = wallet.Sign(data);
        Assert.That(wallet.ValidateSignature(data, sig), Is.True);

        if (sig[0] != 1) sig[0] = 1; else sig[0] = 2;  // Account for random chance being dumb
        Assert.That(wallet.ValidateSignature(data, sig), Is.False);
    }

    [Test]
    public void ExportAndImportPublicKey() {
        DemCoinWallet wallet = DemCoinWallet.NewWithPhrase();
        DemCoinWallet imported = DemCoinWallet.ImportPublic(wallet.PublicKey);
        
        Assert.That(wallet.Address, Is.EqualTo(imported.Address));
    }
}