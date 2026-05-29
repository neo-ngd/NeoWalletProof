using Neo;
using Neo.Wallets;
using NeoWalletProof.Configuration;
using NeoWalletProof.Models;
using NeoWalletProof.Services;
using System.Reflection;
using System.Text;

namespace NeoWalletProof.Shell;

internal sealed class ProofConsole
{
    private Wallet? _wallet;

    public void Run()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"NeoWalletProof {ver}  (Neo 2.13.0, protocol magic={ProtocolSettings.Default.Magic})");
        Console.ResetColor();
        Console.WriteLine("Type 'help' for commands.");
        Console.WriteLine();

        TryOpenConfiguredWallet();

        var running = true;
        while (running)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("proof> ");
            Console.ResetColor();
            var line = Console.ReadLine()?.Trim();
            if (line == null) break;
            if (line.Length == 0) continue;

            try
            {
                running = HandleCommand(ParseLine(line));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private void TryOpenConfiguredWallet()
    {
        var unlock = AppBootstrap.Settings.UnlockWallet;
        if (!unlock.IsActive || string.IsNullOrWhiteSpace(unlock.Path)) return;
        try
        {
            _wallet = WalletOpener.OpenAuto(unlock.Path, unlock.Password);
            Console.WriteLine($"Wallet opened from config.json: {unlock.Path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"config.json UnlockWallet failed: {ex.Message}");
        }
    }

    private bool HandleCommand(string[] args)
    {
        switch (args[0].ToLowerInvariant())
        {
            case "help":
                PrintHelp();
                return true;
            case "exit":
            case "quit":
                return false;
            case "clear":
                Console.Clear();
                return true;
            case "open":
                return CmdOpen(args);
            case "close":
                return CmdClose(args);
            case "import":
                return CmdImport(args);
            case "list":
                return CmdList(args);
            case "prove":
                return CmdProve(args);
            case "verify":
                return CmdVerify(args);
            case "protocol":
                CmdProtocol();
                return true;
            default:
                Console.WriteLine($"Unknown command: {args[0]}");
                return true;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            "Commands:\n" +
            "  open wallet <input>         Open from any of:\n" +
            "                                .json / .db3 wallet file  (prompts password)\n" +
            "                                NEP-2 key (6P...)         (prompts password)\n" +
            "                                WIF key (K.../L.../5...)  (no password)\n" +
            "                                Hex key (64 chars)        (no password)\n" +
            "  close wallet                Close wallet\n" +
            "  import multisig <m> <pk1> ... <pkN>\n" +
            "                              Register an M-of-N multi-sig in the open wallet\n" +
            "                              (neo-cli alias: 'import multisigaddress')\n" +
            "  list address                List addresses in wallet\n" +
            "  prove <address> <challenge> Start a single- or multi-sig proof\n" +
            "  prove <file|json>           Add my signature to a partial multi-sig proof\n" +
            "  verify <file|json>          Verify a WalletProof JSON (single- or multi-sig)\n" +
            "  protocol                    Show protocol.json network settings\n" +
            "  clear / exit");
    }

    private bool CmdOpen(string[] args)
    {
        if (args.Length < 2 || !args[1].Equals("wallet", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: open wallet <path|WIF|NEP-2|hex>");
            return true;
        }
        if (args.Length < 3)
        {
            Console.WriteLine("error");
            return true;
        }

        var input = args[2];

        WalletInputKind kind;
        try
        {
            kind = WalletOpener.DetectKind(input);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return true;
        }

        try
        {
            Wallet newWallet;
            switch (kind)
            {
                case WalletInputKind.File:
                {
                    var password = ReadPassword("password: ");
                    if (password.Length == 0) { Console.WriteLine("cancelled"); return true; }
                    newWallet = WalletOpener.Open(input, password);
                    break;
                }
                case WalletInputKind.Nep2:
                {
                    var password = ReadPassword("NEP-2 password: ");
                    if (password.Length == 0) { Console.WriteLine("cancelled"); return true; }
                    newWallet = WalletOpener.OpenFromKey(input, password);
                    break;
                }
                case WalletInputKind.Wif:
                case WalletInputKind.Hex:
                    newWallet = WalletOpener.OpenFromKey(input);
                    break;
                default:
                    Console.WriteLine($"Unsupported wallet input kind: {kind}");
                    return true;
            }

            _wallet?.Dispose();
            _wallet = newWallet;

            Console.WriteLine(kind == WalletInputKind.File
                ? "Wallet opened."
                : $"Wallet opened from {KindLabel(kind)} private key.");

            foreach (var a in _wallet.GetAccounts().Where(a => !a.WatchOnly))
                Console.WriteLine($"  {a.Address}");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Console.WriteLine(kind == WalletInputKind.File
                ? $"failed to open file \"{input}\""
                : $"failed to decrypt {KindLabel(kind)} private key (wrong password?)");
        }
        catch (FormatException ex)
        {
            Console.WriteLine($"Invalid {KindLabel(kind)} format: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open wallet: {ex.Message}");
        }
        return true;
    }

    private static string KindLabel(WalletInputKind kind) => kind switch
    {
        WalletInputKind.File => "wallet file",
        WalletInputKind.Wif => "WIF",
        WalletInputKind.Nep2 => "NEP-2",
        WalletInputKind.Hex => "hex",
        _ => kind.ToString(),
    };

    private bool CmdClose(string[] args)
    {
        if (args.Length < 2 || !args[1].Equals("wallet", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: close wallet");
            return true;
        }
        _wallet?.Dispose();
        _wallet = null;
        Console.WriteLine("Wallet closed.");
        return true;
    }

    private bool CmdImport(string[] args)
    {
        if (_wallet == null)
        {
            Console.WriteLine("You have to open the wallet first.");
            return true;
        }

        // Accept both forms:
        //   import multisig         <m> <pk1> ... <pkN>     (shorter)
        //   import multisigaddress  <m> <pk1> ... <pkN>     (neo-cli compatible)
        if (args.Length < 2 ||
            !(args[1].Equals("multisigaddress", StringComparison.OrdinalIgnoreCase) ||
              args[1].Equals("multisig", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Usage: import multisig <m> <pubkey1> <pubkey2> ... <pubkeyN>");
            Console.WriteLine("       (alias: import multisigaddress — neo-cli compatible)");
            return true;
        }

        // args layout matches neo-cli: ["import", "multisig(address)", "<m>", "<pk1>", ...]
        if (args.Length < 4)
        {
            Console.WriteLine("Error. Invalid parameters.");
            return true;
        }

        if (!int.TryParse(args[2], out var m))
        {
            Console.WriteLine("Error. Invalid parameters.");
            return true;
        }

        var n = args.Length - 3;
        if (m < 1 || m > n || n > 1024)
        {
            Console.WriteLine("Error. Invalid parameters.");
            return true;
        }

        Neo.Cryptography.ECC.ECPoint[] publicKeys;
        try
        {
            publicKeys = args.Skip(3)
                .Select(p => Neo.Cryptography.ECC.ECPoint.Parse(p, Neo.Cryptography.ECC.ECCurve.Secp256r1))
                .ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Invalid pubkey: {ex.Message}");
            return true;
        }

        try
        {
            var result = MultiSigImporter.Import(_wallet, m, publicKeys);
            Console.WriteLine($"Multisig. Addr.: {result.Address}");
            if (result.Signable)
                Console.WriteLine($"  signable as participant: {result.SignerPublicKey}");
            else
                Console.WriteLine("  watch-only (no participating key in current wallet)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Import failed: {ex.Message}");
        }
        return true;
    }

    private bool CmdList(string[] args)
    {
        if (_wallet == null)
        {
            Console.WriteLine("You have to open the wallet first.");
            return true;
        }
        if (args.Length < 2 || args[1] != "address")
        {
            Console.WriteLine("Usage: list address");
            return true;
        }
        foreach (var account in _wallet.GetAccounts().Where(a => !a.WatchOnly))
            Console.WriteLine(account.Address);
        return true;
    }

    private bool CmdProve(string[] args)
    {
        if (_wallet == null)
        {
            Console.WriteLine("You have to open the wallet first.");
            return true;
        }
        var hasSignable = _wallet.GetAccounts().Any(a => a.HasKey && !a.WatchOnly);
        if (!hasSignable)
        {
            Console.WriteLine("No signable accounts in wallet.");
            return true;
        }

        // prove <single-arg>  → continue an in-flight multi-sig context (file path or JSON blob)
        if (args.Length == 2 && LooksLikeContextInput(args[1]))
        {
            var contextJson = LoadTextArg(args, 1);
            return RunProve(() => ProveService.Continue(_wallet, contextJson));
        }

        // prove <address> <challenge…>  → start a new single- or multi-sig proof
        if (args.Length >= 3)
        {
            var address = args[1];
            var challenge = string.Join(' ', args.Skip(2));
            return RunProve(() => ProveService.Start(_wallet, address, challenge));
        }

        Console.WriteLine("Usage:");
        Console.WriteLine("  prove <address> <challenge>     Start a single- or multi-sig proof");
        Console.WriteLine("  prove <file|json>               Add my signature to a partial multi-sig proof");
        Console.WriteLine("  challenge = one-time nonce issued by the verifier (not generated here).");
        return true;
    }

    private static bool RunProve(Func<ProveOutcome> action)
    {
        try
        {
            var outcome = action();
            PrintProveOutcome(outcome);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        return true;
    }

    private static void PrintProveOutcome(ProveOutcome outcome)
    {
        if (!outcome.IsMultiSig)
        {
            Console.WriteLine("Signed Output:");
            Console.WriteLine(outcome.Json);
            return;
        }

        if (outcome.IsComplete)
        {
            Console.WriteLine(
                $"Multi-sig proof complete ({outcome.SignaturesPresent}/{outcome.SignaturesRequired}). Signed Output:");
        }
        else
        {
            var more = outcome.SignaturesRequired - outcome.SignaturesPresent;
            Console.WriteLine(
                $"Partial proof: {outcome.SignaturesPresent}/{outcome.SignaturesRequired} signatures. " +
                $"Hand this JSON to the next signer ({more} more needed):");
        }
        Console.WriteLine(outcome.Json);
    }

    private static bool LooksLikeContextInput(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (File.Exists(s)) return true;
        var t = s.TrimStart();
        return t.Length > 0 && t[0] == '{';
    }

    private static bool CmdVerify(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: verify <file-path|json>");
            return true;
        }
        var input = LoadTextArg(args, 1);

        try
        {
            var ok = WalletProofVerifier.TryVerify(
                input, out var error, out var address, out var challenge,
                out var present, out var required);
            if (ok)
            {
                var sigs = required > 1 ? $" ({present}/{required} multi-sig signatures)" : "";
                Console.WriteLine($"VALID — signature proves control of the wallet private key{sigs}.");
                Console.WriteLine($"  address: {address}");
                Console.WriteLine($"  challenge: {challenge}");
            }
            else
            {
                Console.WriteLine($"INVALID — {error}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"INVALID — {ex.Message}");
        }
        return true;
    }

    private static void CmdProtocol()
    {
        var s = ProtocolSettings.Default;
        Console.WriteLine($"Magic: {s.Magic} (0x{s.Magic:X8})");
        Console.WriteLine($"AddressVersion: {s.AddressVersion}");
        Console.WriteLine($"SecondsPerBlock: {s.SecondsPerBlock}");
    }

    private static string LoadTextArg(string[] args, int startIndex)
    {
        var text = string.Join(' ', args.Skip(startIndex));
        if (File.Exists(text))
            return File.ReadAllText(text);
        return text;
    }

    private static string ReadPassword(string prompt)
    {
        var sb = new StringBuilder();
        var inputColor = Console.ForegroundColor;

        void Redraw()
        {
            var masked = new string('*', sb.Length);
            Console.Write($"\r{prompt}{masked}   ");
        }

        Console.Write(prompt);

        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
            {
                sb.Length--;
                Redraw();
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Redraw();
                Console.ForegroundColor = inputColor;
            }
        }

        Console.ForegroundColor = inputColor;
        Console.WriteLine();
        return sb.ToString();
    }

    private static string[] ParseLine(string line)
    {
        var args = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && line[i] == ' ') i++;
            if (i >= line.Length) break;
            if (line[i] == '"')
            {
                i++;
                var start = i;
                while (i < line.Length && line[i] != '"') i++;
                args.Add(line[start..i]);
                if (i < line.Length) i++;
            }
            else
            {
                var start = i;
                while (i < line.Length && line[i] != ' ') i++;
                args.Add(line[start..i]);
            }
        }
        return args.ToArray();
    }
}
