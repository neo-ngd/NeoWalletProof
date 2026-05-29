using Neo.Wallets;
using Neo.Wallets.NEP6;
using Neo.Wallets.SQLite;
using System.Globalization;
using System.Security.Cryptography;

namespace NeoWalletProof.Services;

/// <summary>
/// Recognised wallet input formats accepted by <see cref="WalletOpener"/>.
/// </summary>
internal enum WalletInputKind
{
    File,
    Nep2,
    Wif,
    Hex,
}

internal static class WalletOpener
{
    /// <summary>
    /// Inspect a user-provided string and decide what kind of wallet input it is.
    /// Existing files always win; otherwise we discriminate purely by length / prefix
    /// so that callers can decide whether to prompt for a password before any decryption.
    /// </summary>
    public static WalletInputKind DetectKind(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Wallet input is empty.");

        if (File.Exists(input))
            return WalletInputKind.File;

        if (LooksLikeHex(input))
            return WalletInputKind.Hex;

        if (LooksLikeNep2(input))
            return WalletInputKind.Nep2;

        if (LooksLikeWif(input))
            return WalletInputKind.Wif;

        throw new ArgumentException(
            "Input is not a recognised wallet file or private key " +
            "(expected .json/.db3 path, 64-char hex, WIF starting with K/L/5, or NEP-2 starting with 6P).");
    }

    /// <summary>
    /// Open a NEP-6 (<c>.json</c>) or SQLite (<c>.db3</c>) wallet from disk.
    /// </summary>
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

    /// <summary>
    /// Build an in-memory wallet that contains a single account derived from a raw private key.
    /// Accepts hex, WIF, or NEP-2; <paramref name="nep2Password"/> is required for NEP-2 only.
    /// </summary>
    public static Wallet OpenFromKey(string input, string? nep2Password = null)
    {
        byte[] privateKey;
        switch (DetectKind(input))
        {
            case WalletInputKind.Hex:
                privateKey = HexToBytes(input);
                break;
            case WalletInputKind.Wif:
                privateKey = Wallet.GetPrivateKeyFromWIF(input);
                break;
            case WalletInputKind.Nep2:
                if (string.IsNullOrEmpty(nep2Password))
                    throw new ArgumentException("Password is required to decrypt NEP-2 private key.");
                var scrypt = ScryptParameters.Default;
                privateKey = Wallet.GetPrivateKeyFromNEP2(input, nep2Password, scrypt.N, scrypt.R, scrypt.P);
                break;
            default:
                throw new ArgumentException("Input is not a private key.");
        }

        try
        {
            return BuildInMemoryWallet(privateKey);
        }
        finally
        {
            Array.Clear(privateKey, 0, privateKey.Length);
        }
    }

    /// <summary>
    /// Dispatcher used by callers (CLI / config auto-unlock) that already have a password
    /// in hand and just want to open whatever the user gave them.
    /// For file / NEP-2 the password is required; for WIF / hex it is ignored.
    /// </summary>
    public static Wallet OpenAuto(string input, string? password)
    {
        var kind = DetectKind(input);
        return kind switch
        {
            WalletInputKind.File => Open(input, password ?? string.Empty),
            WalletInputKind.Nep2 => OpenFromKey(input, password),
            _ => OpenFromKey(input),
        };
    }

    private static Wallet BuildInMemoryWallet(byte[] privateKey)
    {
        // NEP6Wallet needs *some* password to encrypt the in-memory key slot.
        // Since the wallet is never persisted, a random throw-away value is fine.
        var memoryPassword = Guid.NewGuid().ToString("N");
        var wallet = new NEP6Wallet(indexer: null, path: "<in-memory>");
        wallet.Unlock(memoryPassword);
        wallet.CreateAccount(privateKey);
        return wallet;
    }

    private static bool LooksLikeHex(string s)
    {
        if (s.Length != 64) return false;
        foreach (var c in s)
        {
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }

    private static bool LooksLikeNep2(string s) =>
        s.Length == 58 && s.StartsWith("6P", StringComparison.Ordinal);

    private static bool LooksLikeWif(string s)
    {
        if (s.Length != 51 && s.Length != 52) return false;
        var first = s[0];
        return first == 'K' || first == 'L' || first == '5';
    }

    private static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }
}
