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
        using var wallet = OpenWalletFromOpt(opt);

        // Multi-sig continuation: -k / --continue carries a partial context (file or JSON).
        var contextArg = opt.GetValueOrDefault("continue") ?? opt.GetValueOrDefault("k");
        ProveOutcome outcome;
        if (!string.IsNullOrEmpty(contextArg))
        {
            var json = File.Exists(contextArg) ? File.ReadAllText(contextArg) : contextArg;
            outcome = ProveService.Continue(wallet, json);
        }
        else
        {
            var challenge = Require(opt, "challenge", "c");
            var address = ResolveStartAddress(wallet, opt);
            outcome = ProveService.Start(wallet, address, challenge);
        }

        EmitProveOutcome(outcome, opt);
        return 0;
    }

    private static string ResolveStartAddress(Wallet wallet, Dictionary<string, string?> opt)
    {
        var requested = opt.GetValueOrDefault("address") ?? opt.GetValueOrDefault("a");
        if (!string.IsNullOrEmpty(requested))
            return requested;

        var signable = wallet.GetAccounts().Where(a => a.HasKey && !a.WatchOnly).ToList();
        if (signable.Count == 0)
            throw new InvalidOperationException("No signable accounts in wallet.");
        if (signable.Count > 1)
            throw new ArgumentException("Wallet has multiple accounts; specify --address (-a).");
        return signable[0].Address;
    }

    private static void EmitProveOutcome(ProveOutcome outcome, Dictionary<string, string?> opt)
    {
        var outFile = opt.GetValueOrDefault("out") ?? opt.GetValueOrDefault("o");
        if (!string.IsNullOrEmpty(outFile))
            File.WriteAllText(outFile, outcome.Json);
        else
            Console.WriteLine(outcome.Json);

        if (outcome.IsMultiSig)
        {
            var status = outcome.IsComplete
                ? $"complete ({outcome.SignaturesPresent}/{outcome.SignaturesRequired})"
                : $"partial — {outcome.SignaturesPresent}/{outcome.SignaturesRequired} signatures, " +
                  $"{outcome.SignaturesRequired - outcome.SignaturesPresent} more needed";
            Console.Error.WriteLine($"# multi-sig proof {status}; signed as {outcome.SignerPublicKey}");
        }
    }

    private static int RunVerify(Dictionary<string, string?> opt)
    {
        var input = opt.GetValueOrDefault("file") ?? opt.GetValueOrDefault("f");
        if (string.IsNullOrEmpty(input) && opt.TryGetValue("json", out var j))
            input = j;
        if (string.IsNullOrEmpty(input))
            throw new ArgumentException("Provide --file or --json.");

        var json = File.Exists(input) ? File.ReadAllText(input) : input;
        if (WalletProofVerifier.TryVerify(
                json, out var error, out var address, out var challenge,
                out var present, out var required))
        {
            Console.WriteLine("VALID");
            Console.WriteLine($"address={address}");
            Console.WriteLine($"challenge={challenge}");
            if (required > 1)
                Console.WriteLine($"signatures={present}/{required}");
            return 0;
        }
        Console.Error.WriteLine(error);
        return 2;
    }

    /// <summary>
    /// Open a wallet from <c>--wallet</c>/<c>-w</c>, accepting a wallet file path or a
    /// raw private key (WIF / NEP-2 / hex). <c>--password</c>/<c>-p</c> is required for
    /// file wallets and NEP-2 keys; ignored for WIF and hex.
    /// </summary>
    private static Wallet OpenWalletFromOpt(Dictionary<string, string?> opt)
    {
        var walletInput = Require(opt, "wallet", "w");
        var password = opt.GetValueOrDefault("password") ?? opt.GetValueOrDefault("p");
        var kind = WalletOpener.DetectKind(walletInput);
        return kind switch
        {
            WalletInputKind.File => string.IsNullOrEmpty(password)
                ? throw new ArgumentException("Missing --password (-p) for wallet file.")
                : WalletOpener.Open(walletInput, password),
            WalletInputKind.Nep2 => string.IsNullOrEmpty(password)
                ? throw new ArgumentException("Missing --password (-p) for NEP-2 private key.")
                : WalletOpener.OpenFromKey(walletInput, password),
            _ => WalletOpener.OpenFromKey(walletInput),
        };
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
  Start a proof (single- or multi-sig):
    NeoWalletProof prove -w <wallet|WIF|NEP-2|hex> [-p <pass>] [-a <address>] -c <challenge> [-o out.json]
  Continue an in-flight multi-sig proof (add my signature):
    NeoWalletProof prove -w <wallet|WIF|NEP-2|hex> [-p <pass>] -k <context.json|inline-json> [-o out.json]
  Verify any proof JSON (single- or multi-sig):
    NeoWalletProof verify -f <proof.json>

-w / --wallet accepts any of:
  - NEP-6 wallet file (*.json)            : -p required
  - SQLite wallet file (*.db3)            : -p required
  - NEP-2 encrypted private key (6P...)   : -p required
  - WIF private key (K... / L... / 5...)  : -p ignored
  - Hex private key (64 hex chars)        : -p ignored

-a / --address may be omitted when the wallet contains a single signable account
(typical for WIF / NEP-2 / hex imports).

Place protocol.json / config.json next to the executable (same as neo-cli).
");
        return 0;
    }
}
