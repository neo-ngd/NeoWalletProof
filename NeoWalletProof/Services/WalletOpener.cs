using Neo.Wallets;
using Neo.Wallets.NEP6;
using Neo.Wallets.SQLite;
using System.Security.Cryptography;

namespace NeoWalletProof.Services;

internal static class WalletOpener
{
    public static Wallet Open(string path, string password)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Wallet file not found.", path);

        if (Path.GetExtension(path).Equals(".db3", StringComparison.OrdinalIgnoreCase))
            return UserWallet.Open(null, path, password);

        var wallet = new NEP6Wallet(null, path);
        wallet.Unlock(password);
        return wallet;
    }
}
