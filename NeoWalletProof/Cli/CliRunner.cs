using Neo.Wallets;
using NeoWalletProof.Configuration;
using NeoWalletProof.Models;
using NeoWalletProof.Services;

namespace NeoWalletProof.Cli;

internal static class CliRunner
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
            return -1;

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "prove" => RunProve(ParseOptions(args)),
                "verify" => RunVerify(ParseOptions(args)),
                "sign" => RunSign(ParseOptions(args)),
                "verify-sign" => RunVerifySign(ParseOptions(args)),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunProve(Dictionary<string, string?> opt)
    {
        var walletPath = Require(opt, "wallet", "w");
        var password = Require(opt, "password", "p");
        var address = Require(opt, "address", "a");
        var challenge = Require(opt, "challenge", "c");

        using var wallet = WalletOpener.Open(walletPath, password);
        var account = wallet.GetAccounts().FirstOrDefault(a => a.HasKey && a.Address == address);
        if (account == null)
            throw new InvalidOperationException("Address not found or not signable in wallet.");

        var proof = WalletProofPayload.Create(address, challenge, account.GetKey());
        var outFile = opt.GetValueOrDefault("out") ?? opt.GetValueOrDefault("o");
        var json = proof.ToJson();
        if (!string.IsNullOrEmpty(outFile))
            File.WriteAllText(outFile, json);
        else
            Console.WriteLine(json);
        return 0;
    }

    private static int RunVerify(Dictionary<string, string?> opt)
    {
        var input = opt.GetValueOrDefault("file") ?? opt.GetValueOrDefault("f");
        if (string.IsNullOrEmpty(input) && opt.TryGetValue("json", out var j))
            input = j;
        if (string.IsNullOrEmpty(input))
            throw new ArgumentException("Provide --file or --json.");

        var json = File.Exists(input) ? File.ReadAllText(input) : input;
        var proof = WalletProofPayload.FromJson(json);
        if (WalletProofVerifier.TryVerify(proof, out var error))
        {
            Console.WriteLine("VALID");
            Console.WriteLine($"address={proof.Address}");
            Console.WriteLine($"challenge={proof.Challenge}");
            return 0;
        }
        Console.Error.WriteLine(error);
        return 2;
    }

    private static int RunSign(Dictionary<string, string?> opt)
    {
        var walletPath = Require(opt, "wallet", "w");
        var password = Require(opt, "password", "p");
        var json = opt.GetValueOrDefault("json") ?? opt.GetValueOrDefault("j");
        if (string.IsNullOrEmpty(json))
        {
            var file = Require(opt, "file", "f");
            json = File.ReadAllText(file);
        }

        using var wallet = WalletOpener.Open(walletPath, password);
        var output = ContractContextService.SignContext(wallet, json);
        var outFile = opt.GetValueOrDefault("out") ?? opt.GetValueOrDefault("o");
        if (!string.IsNullOrEmpty(outFile))
            File.WriteAllText(outFile, output);
        else
            Console.WriteLine(output);
        return 0;
    }

    private static int RunVerifySign(Dictionary<string, string?> opt)
    {
        var input = opt.GetValueOrDefault("file") ?? opt.GetValueOrDefault("f");
        if (string.IsNullOrEmpty(input) && opt.TryGetValue("json", out var j))
            input = j;
        if (string.IsNullOrEmpty(input))
            throw new ArgumentException("Provide --file or --json.");

        var json = File.Exists(input) ? File.ReadAllText(input) : input;
        if (ContractContextService.TryVerifySignedContext(json, out var error))
        {
            Console.WriteLine("VALID");
            return 0;
        }
        Console.Error.WriteLine(error);
        return 2;
    }

    private static string Require(Dictionary<string, string?> opt, string longName, string shortName)
    {
        if (opt.TryGetValue(longName, out var v) && !string.IsNullOrEmpty(v)) return v;
        if (opt.TryGetValue(shortName, out v) && !string.IsNullOrEmpty(v)) return v;
        throw new ArgumentException($"Missing --{longName} (-{shortName}).");
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("-", StringComparison.Ordinal)) continue;
            string key;
            string? value = null;
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                var eq = a.IndexOf('=');
                if (eq > 0)
                {
                    key = a[2..eq];
                    value = a[(eq + 1)..];
                }
                else
                {
                    key = a[2..];
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        value = args[++i];
                }
            }
            else
            {
                key = a[1..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    value = args[++i];
            }
            map[key] = value;
        }
        return map;
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        return PrintHelp();
    }

    private static int PrintHelp()
    {
        Console.WriteLine(
            @"NeoWalletProof — wallet ownership proof (Neo 2.13.0)

Interactive:  NeoWalletProof

CLI:
  NeoWalletProof prove -w <wallet> -p <pass> -a <address> -c <challenge> [-o out.json]
  NeoWalletProof verify -f <proof.json>
  NeoWalletProof sign -w <wallet> -p <pass> -f <context.json> [-o out.json]
  NeoWalletProof verify-sign -f <signed.json>

Place protocol.json / config.json next to the executable (same as neo-cli).
");
        return 0;
    }
}
