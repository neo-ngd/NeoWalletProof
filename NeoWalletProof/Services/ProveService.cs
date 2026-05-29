using Neo;
using Neo.SmartContract;
using Neo.Wallets;
using NeoWalletProof.Models;

namespace NeoWalletProof.Services;

/// <summary>
/// Outcome of a <c>prove</c> invocation. Either a single-sig proof (always complete)
/// or a multi-sig context that may still be partial.
/// </summary>
internal sealed class ProveOutcome
{
    public string Json { get; init; } = "";
    public bool IsMultiSig { get; init; }
    public bool IsComplete { get; init; }
    public int SignaturesPresent { get; init; }
    public int SignaturesRequired { get; init; }
    public string SignerPublicKey { get; init; } = "";
    public string Address { get; init; } = "";
}

/// <summary>
/// Logic shared by the interactive console and the CLI for building a wallet proof.
/// Supports starting a new single- or multi-sig proof and continuing an in-flight
/// multi-sig context.
/// </summary>
internal static class ProveService
{
    /// <summary>
    /// Start a new proof. The wallet must contain the account matching
    /// <paramref name="address"/>; that account's contract decides whether
    /// this is a single-sig proof or a multi-sig start.
    /// </summary>
    public static ProveOutcome Start(Wallet wallet, string address, string challenge)
    {
        var account = wallet.GetAccounts().FirstOrDefault(a => a.Address == address)
            ?? throw new InvalidOperationException("Address not found in wallet.");
        if (!account.HasKey || account.WatchOnly)
            throw new InvalidOperationException("Account has no signable key (watch-only).");

        var contractScript = account.Contract?.Script
            ?? throw new InvalidOperationException("Account has no contract script.");

        if (contractScript.IsSignatureContract())
        {
            var key = account.GetKey()
                ?? throw new InvalidOperationException("Could not decrypt the private key.");
            var proof = WalletProofPayload.Create(address, challenge, key);
            return new ProveOutcome
            {
                Json = proof.ToJson(),
                IsMultiSig = false,
                IsComplete = true,
                SignaturesPresent = 1,
                SignaturesRequired = 1,
                SignerPublicKey = proof.PublicKey,
                Address = address,
            };
        }

        if (contractScript.IsMultiSigContract())
        {
            var parsed = MultiSigScript.TryParse(contractScript)
                ?? throw new InvalidOperationException("Could not parse multi-sig redeem script.");

            var settings = ProtocolSettings.Default;
            var context = MultiSigProofPayload.CreateStart(
                settings.Magic, settings.AddressVersion, address, challenge, parsed.M, parsed.PublicKeys);

            return AddSignerSignature(wallet, context, contextSource: "wallet contract");
        }

        throw new InvalidOperationException("Account contract is neither single-sig nor multi-sig.");
    }

    /// <summary>
    /// Continue an in-flight multi-sig context: locate one of the participating
    /// private keys in <paramref name="wallet"/> that has not yet signed and add
    /// its signature.
    /// </summary>
    public static ProveOutcome Continue(Wallet wallet, string contextJson)
    {
        if (!MultiSigProofPayload.IsMultiSigJson(contextJson))
            throw new InvalidOperationException(
                "Input is not a multi-sig proof context (schema must be \"" +
                MultiSigProofPayload.SchemaVersion + "\").");

        var context = MultiSigProofPayload.FromJson(contextJson);

        var settings = ProtocolSettings.Default;
        if (context.Magic != settings.Magic)
            throw new InvalidOperationException(
                $"Magic mismatch: context={context.Magic}, protocol.json={settings.Magic}.");
        if (context.AddressVersion != settings.AddressVersion)
            throw new InvalidOperationException(
                $"AddressVersion mismatch: context={context.AddressVersion}, protocol.json={settings.AddressVersion}.");

        if (context.IsComplete)
            throw new InvalidOperationException(
                $"Context is already complete ({context.Signatures.Count}/{context.M}). Nothing to sign.");

        return AddSignerSignature(wallet, context, contextSource: "input context");
    }

    private static ProveOutcome AddSignerSignature(Wallet wallet, MultiSigProofPayload context, string contextSource)
    {
        var pubkeySet = new HashSet<string>(context.PublicKeys, StringComparer.Ordinal);

        // Walk every signable key in the wallet, look for one that
        //  (a) participates in this multi-sig, and
        //  (b) hasn't already signed.
        KeyPair? chosen = null;
        string chosenPub = "";
        foreach (var a in wallet.GetAccounts())
        {
            if (a.WatchOnly || !a.HasKey) continue;
            KeyPair? k;
            try { k = a.GetKey(); }
            catch { continue; }
            if (k == null) continue;

            var pubHex = k.PublicKey.EncodePoint(true).ToHexString();
            if (!pubkeySet.Contains(pubHex)) continue;
            if (context.Signatures.ContainsKey(pubHex)) continue;
            chosen = k;
            chosenPub = pubHex;
            break;
        }

        if (chosen == null)
            throw new InvalidOperationException(
                $"No usable signer in wallet for the multi-sig participants listed in the {contextSource} " +
                "(every matching key has already signed, or none of the keys participate).");

        context.AddSignature(chosen);

        return new ProveOutcome
        {
            Json = context.ToJson(),
            IsMultiSig = true,
            IsComplete = context.IsComplete,
            SignaturesPresent = context.Signatures.Count,
            SignaturesRequired = context.M,
            SignerPublicKey = chosenPub,
            Address = context.Address,
        };
    }
}
