using Neo;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.SmartContract;
using Neo.Wallets;
using NeoWalletProof.Models;
using System.Text;

namespace NeoWalletProof.Services;

internal static class WalletProofVerifier
{
    /// <summary>
    /// Parse JSON of either single-sig (<see cref="WalletProofPayload"/>) or
    /// multi-sig (<see cref="MultiSigProofPayload"/>) shape and verify it.
    /// </summary>
    public static bool TryVerify(string json, out string error, out string address, out string challenge,
        out int signaturesPresent, out int signaturesRequired)
    {
        address = "";
        challenge = "";
        signaturesPresent = 0;
        signaturesRequired = 0;

        if (MultiSigProofPayload.IsMultiSigJson(json))
        {
            var multi = MultiSigProofPayload.FromJson(json);
            address = multi.Address;
            challenge = multi.Challenge;
            signaturesPresent = multi.Signatures.Count;
            signaturesRequired = multi.M;
            return TryVerify(multi, out error);
        }

        var single = WalletProofPayload.FromJson(json);
        address = single.Address;
        challenge = single.Challenge;
        signaturesPresent = 1;
        signaturesRequired = 1;
        return TryVerify(single, out error);
    }

    public static bool TryVerify(WalletProofPayload proof, out string error)
    {
        error = "";

        var settings = ProtocolSettings.Default;
        if (proof.Magic != settings.Magic)
        {
            error = $"Magic mismatch: proof={proof.Magic}, protocol.json={settings.Magic}";
            return false;
        }

        if (proof.AddressVersion != settings.AddressVersion)
        {
            error = $"AddressVersion mismatch: proof={proof.AddressVersion}, protocol.json={settings.AddressVersion}";
            return false;
        }

        ECPoint publicKey;
        try
        {
            publicKey = ECPoint.Parse(proof.PublicKey, ECCurve.Secp256r1);
        }
        catch (Exception ex)
        {
            error = $"Invalid public key: {ex.Message}";
            return false;
        }

        UInt160 expectedAddress;
        try
        {
            expectedAddress = proof.Address.ToScriptHash();
        }
        catch (Exception ex)
        {
            error = $"Invalid address: {ex.Message}";
            return false;
        }

        var scriptHash = Contract.CreateSignatureRedeemScript(publicKey).ToScriptHash();
        if (!expectedAddress.Equals(scriptHash))
        {
            error = "Address does not match public key (wrong network or forged address).";
            return false;
        }

        var message = WalletProofPayload.BuildMessage(proof.Magic, proof.Address, proof.Challenge, proof.Timestamp);
        var messageHash = Encoding.UTF8.GetBytes(message).Sha256();
        byte[] signature;
        try
        {
            signature = proof.Signature.HexToBytes();
        }
        catch
        {
            error = "Invalid signature hex.";
            return false;
        }

        var pubBytes = publicKey.EncodePoint(true).ToArray();
        if (!Crypto.Default.VerifySignature(messageHash, signature, pubBytes))
        {
            error = "ECDSA signature verification failed — holder does not possess the private key.";
            return false;
        }

        return true;
    }

    public static bool TryVerify(MultiSigProofPayload proof, out string error)
    {
        error = "";

        var settings = ProtocolSettings.Default;
        if (proof.Magic != settings.Magic)
        {
            error = $"Magic mismatch: proof={proof.Magic}, protocol.json={settings.Magic}";
            return false;
        }
        if (proof.AddressVersion != settings.AddressVersion)
        {
            error = $"AddressVersion mismatch: proof={proof.AddressVersion}, protocol.json={settings.AddressVersion}";
            return false;
        }
        if (proof.M < 1 || proof.M > proof.PublicKeys.Length)
        {
            error = $"Invalid threshold M={proof.M} for {proof.PublicKeys.Length} keys.";
            return false;
        }
        if (proof.Signatures.Count < proof.M)
        {
            error = $"Incomplete proof: {proof.Signatures.Count}/{proof.M} signatures.";
            return false;
        }

        ECPoint[] pubkeys;
        try
        {
            pubkeys = proof.PublicKeys.Select(p => ECPoint.Parse(p, ECCurve.Secp256r1)).ToArray();
        }
        catch (Exception ex)
        {
            error = $"Invalid public key in list: {ex.Message}";
            return false;
        }

        UInt160 expectedAddress;
        try
        {
            expectedAddress = proof.Address.ToScriptHash();
        }
        catch (Exception ex)
        {
            error = $"Invalid address: {ex.Message}";
            return false;
        }

        var redeem = Contract.CreateMultiSigRedeemScript(proof.M, pubkeys);
        if (!expectedAddress.Equals(redeem.ToScriptHash()))
        {
            error = "Address does not match M-of-N multi-sig script (forged address or wrong key set).";
            return false;
        }

        var pubkeySet = new HashSet<string>(proof.PublicKeys, StringComparer.Ordinal);
        var message = WalletProofPayload.BuildMessage(proof.Magic, proof.Address, proof.Challenge, proof.Timestamp);
        var messageHash = Encoding.UTF8.GetBytes(message).Sha256();

        var validCount = 0;
        foreach (var kvp in proof.Signatures)
        {
            if (!pubkeySet.Contains(kvp.Key))
            {
                error = $"Signature from non-participant pubkey {kvp.Key}.";
                return false;
            }

            ECPoint pk;
            byte[] sig;
            try
            {
                pk = ECPoint.Parse(kvp.Key, ECCurve.Secp256r1);
                sig = kvp.Value.HexToBytes();
            }
            catch (Exception ex)
            {
                error = $"Malformed signature for {kvp.Key}: {ex.Message}";
                return false;
            }

            var pkBytes = pk.EncodePoint(true).ToArray();
            if (!Crypto.Default.VerifySignature(messageHash, sig, pkBytes))
            {
                error = $"ECDSA signature invalid for pubkey {kvp.Key}.";
                return false;
            }
            validCount++;
        }

        if (validCount < proof.M)
        {
            error = $"Only {validCount} valid signatures, need {proof.M}.";
            return false;
        }

        return true;
    }
}
