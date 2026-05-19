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
}
