using Neo;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.IO.Json;
using Neo.SmartContract;
using Neo.VM;
using Neo.Network.P2P;
using Neo.Wallets;

namespace NeoWalletProof.Services;

/// <summary>
/// neo-cli compatible <c>sign</c> / signature verification for ContractParametersContext JSON.
/// </summary>
internal static class ContractContextService
{
    public static string SignContext(Wallet wallet, string jsonToSign)
    {
        var context = ContractParametersContext.Parse(jsonToSign);
        if (!wallet.Sign(context))
            throw new InvalidOperationException("No private key in wallet can sign this context.");
        return context.ToString();
    }

    /// <summary>
    /// Verifies signatures embedded in a ContractParametersContext JSON (neo-cli Signed Output).
    /// Works offline; does not require a synced node.
    /// </summary>
    public static bool TryVerifySignedContext(string json, out string error)
    {
        error = "";
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (Exception ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }

        ContractParametersContext context;
        try
        {
            context = ContractParametersContext.Parse(json);
        }
        catch (Exception ex)
        {
            error = $"Cannot parse ContractParametersContext: {ex.Message}";
            return false;
        }

        var message = context.Verifiable.GetHashData();
        var items = root["items"];
        if (items == null)
        {
            error = "Missing 'items' in context JSON.";
            return false;
        }

        var verified = 0;
        foreach (var property in items.Properties)
        {
            var scriptHashKey = property.Key;
            var item = property.Value;
            var scriptHex = item?["script"]?.AsString();
            if (string.IsNullOrEmpty(scriptHex)) continue;

            var script = scriptHex.HexToBytes();
            var contract = new Contract
            {
                Script = script,
                ParameterList = new[] { ContractParameterType.Signature }
            };
            var pubkey = ExtractPublicKeyFromSignatureContract(script);
            if (pubkey == null)
            {
                error = $"Unsupported verification script for {scriptHashKey}.";
                return false;
            }

            byte[]? signature = null;
            var sigs = item?["signatures"];
            if (sigs != null)
            {
                foreach (var sigProp in sigs.Properties)
                {
                    signature = sigProp.Value.AsString().HexToBytes();
                    break;
                }
            }

            if (signature == null)
            {
                var parameters = item?["parameters"] as JArray;
                if (parameters != null)
                {
                    foreach (var p in parameters)
                    {
                        if (p["type"]?.AsString() == "Signature" && p["value"] != null)
                        {
                            signature = p["value"].AsString().HexToBytes();
                            break;
                        }
                    }
                }
            }

            if (signature == null)
            {
                error = $"No signature found for script hash {scriptHashKey}.";
                return false;
            }

            var pubBytes = pubkey.EncodePoint(true).ToArray();
            if (!Crypto.Default.VerifySignature(message, signature, pubBytes))
            {
                error = $"Signature invalid for {scriptHashKey}.";
                return false;
            }

            verified++;
        }

        if (verified == 0)
        {
            error = "No signable items found in context.";
            return false;
        }

        return true;
    }

    private static ECPoint? ExtractPublicKeyFromSignatureContract(byte[] script)
    {
        // Standard single-sig: PUSH33 <pubkey> CHECKSIG
        if (script.Length < 35 || script[0] != 0x21 || script[34] != (byte)OpCode.CHECKSIG)
            return null;
        try
        {
            return ECPoint.DecodePoint(script.Skip(1).Take(33).ToArray(), ECCurve.Secp256r1);
        }
        catch
        {
            return null;
        }
    }
}
