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
            _wallet = WalletOpener.Open(unlock.Path, unlock.Password);
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
            case "list":
                return CmdList(args);
            case "prove":
                return CmdProve(args);
            case "verify":
                return CmdVerify(args);
            case "sign":
                return CmdSign(args);
            case "verify-sign":
                return CmdVerifySign(args);
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
            "  open wallet <path>          Open NEP-6 / db3 wallet (prompts password)\n" +
            "  close wallet                Close wallet\n" +
            "  list address                List addresses in wallet\n" +
            "  prove <address> <challenge> Sign proof (challenge = nonce from verifier)\n" +
            "  verify <file|json>          Verify a WalletProof JSON\n" +
            "  sign <json>                 neo-cli compatible ContractParametersContext sign\n" +
            "  verify-sign <file|json>     Verify neo-cli Signed Output JSON\n" +
            "  protocol                    Show protocol.json network settings\n" +
            "  clear / exit");
    }

    private bool CmdOpen(string[] args)
    {
        if (args.Length < 2 || !args[1].Equals("wallet", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: open wallet <path>");
            return true;
        }
        if (args.Length < 3)
        {
            Console.WriteLine("error");
            return true;
        }
        var path = args[2];
        if (!File.Exists(path))
        {
            Console.WriteLine("File does not exist");
            return true;
        }
        var password = ReadPassword("password: ");
        if (password.Length == 0)
        {
            Console.WriteLine("cancelled");
            return true;
        }
        try
        {
            _wallet?.Dispose();
            _wallet = WalletOpener.Open(path, password);
            Console.WriteLine("Wallet opened.");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Console.WriteLine($"failed to open file \"{path}\"");
        }
        return true;
    }

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
        var accounts = _wallet.GetAccounts().Where(a => a.HasKey && !a.WatchOnly).ToList();
        if (accounts.Count == 0)
        {
            Console.WriteLine("No signable accounts in wallet.");
            return true;
        }

        if (args.Length < 3)
        {
            Console.WriteLine("Usage: prove <address> <challenge>");
            Console.WriteLine("  challenge = one-time nonce issued by the verifier (not generated here).");
            return true;
        }

        var address = args[1];
        var challenge = string.Join(' ', args.Skip(2));
        var account = accounts.FirstOrDefault(a => a.Address == address);
        if (account == null)
        {
            Console.WriteLine("Address not found in wallet.");
            return true;
        }

        var proof = WalletProofPayload.Create(address, challenge, account.GetKey());
        Console.WriteLine("Signed Output:");
        Console.WriteLine(proof.ToJson());
        return true;
    }

    private static bool CmdVerify(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: verify <file-path|json>");
            return true;
        }
        var input = LoadTextArg(args, 1);
        var proof = WalletProofPayload.FromJson(input);
        if (WalletProofVerifier.TryVerify(proof, out var error))
        {
            Console.WriteLine("VALID — signature proves control of the wallet private key.");
            Console.WriteLine($"  address: {proof.Address}");
            Console.WriteLine($"  challenge: {proof.Challenge}");
        }
        else
            Console.WriteLine($"INVALID — {error}");
        return true;
    }

    private bool CmdSign(string[] args)
    {
        if (_wallet == null)
        {
            Console.WriteLine("You have to open the wallet first.");
            return true;
        }
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: sign <jsonObjectToSign>");
            return true;
        }
        var json = string.Join(string.Empty, args.Skip(1));
        try
        {
            var output = ContractContextService.SignContext(_wallet, json);
            Console.WriteLine($"Signed Output:\r\n{output}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        return true;
    }

    private static bool CmdVerifySign(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: verify-sign <file-path|json>");
            return true;
        }
        var json = LoadTextArg(args, 1);
        if (ContractContextService.TryVerifySignedContext(json, out var error))
            Console.WriteLine("VALID — context signatures match verifiable hash (private key proven).");
        else
            Console.WriteLine($"INVALID — {error}");
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
