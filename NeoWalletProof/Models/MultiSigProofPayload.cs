using Neo;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.IO.Json;
using Neo.Wallets;
using System.Text;

namespace NeoWalletProof.Models;

/// <summary>
/// Multi-signature wallet ownership proof / partial context.
///
/// The signed plaintext is identical to <see cref="WalletProofPayload"/>:
/// each participant signs <c>magic / address / challenge / timestamp</c>.
/// The address is the hash of the multi-sig redeem script built from
/// <c>M + PublicKeys</c>, so signatures are bound to a specific contract.
///
/// The same JSON shape is used both for partial (in-flight) contexts and
/// for the final completed proof; <see cref="IsComplete"/> tells them apart.
/// </summary>
public sealed class MultiSigProofPayload
{
    public const string SchemaVersion = "1-multisig";

    public uint Magic { get; set; }
    public byte AddressVersion { get; set; }
    public string Address { get; set; } = "";
    public string Challenge { get; set; } = "";
    public long Timestamp { get; set; }

    /// <summary>Threshold (signatures required).</summary>
    public int M { get; set; }

    /// <summary>All participating compressed pubkeys (hex), in redeem-script order.</summary>
    public string[] PublicKeys { get; set; } = Array.Empty<string>();

    /// <summary>Collected signatures keyed by compressed pubkey hex.</summary>
    public Dictionary<string, string> Signatures { get; set; } = new(StringComparer.Ordinal);

    public bool IsComplete => Signatures.Count >= M;
    public int Remaining => Math.Max(0, M - Signatures.Count);

    public static MultiSigProofPayload CreateStart(
        uint magic, byte addressVersion, string address, string challenge,
        int m, ECPoint[] publicKeys)
    {
        return new MultiSigProofPayload
        {
            Magic = magic,
            AddressVersion = addressVersion,
            Address = address,
            Challenge = challenge,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            M = m,
            PublicKeys = publicKeys.Select(p => p.EncodePoint(true).ToHexString()).ToArray(),
            Signatures = new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// Sign the proof message with <paramref name="key"/> and store the signature.
    /// Returns the compressed pubkey hex that was added.
    /// </summary>
    public string AddSignature(KeyPair key)
    {
        var pubHex = key.PublicKey.EncodePoint(true).ToHexString();
        if (!PublicKeys.Contains(pubHex, StringComparer.Ordinal))
            throw new InvalidOperationException("This key is not a participant of the multi-sig.");
        if (Signatures.ContainsKey(pubHex))
            throw new InvalidOperationException("This key has already signed.");
        if (IsComplete)
            throw new InvalidOperationException($"Proof already has {M} signatures.");

        var message = WalletProofPayload.BuildMessage(Magic, Address, Challenge, Timestamp);
        var messageHash = Encoding.UTF8.GetBytes(message).Sha256();
        var pubBytes = key.PublicKey.EncodePoint(false).Skip(1).ToArray();
        var signature = Crypto.Default.Sign(messageHash, key.PrivateKey, pubBytes);
        Signatures[pubHex] = signature.ToHexString();
        return pubHex;
    }

    public string ToJson() => ToJObject().ToString();

    public JObject ToJObject()
    {
        var json = new JObject();
        json["schema"] = SchemaVersion;
        json["magic"] = Magic;
        json["addressVersion"] = AddressVersion;
        json["address"] = Address;
        json["challenge"] = Challenge;
        json["timestamp"] = Timestamp;
        json["m"] = M;
        json["publicKeys"] = new JArray(PublicKeys.Select(p => (JObject)p));

        var sigs = new JObject();
        foreach (var kvp in Signatures)
            sigs[kvp.Key] = kvp.Value;
        json["signatures"] = sigs;
        return json;
    }

    public static MultiSigProofPayload FromJson(string json)
    {
        var obj = JObject.Parse(json);
        var payload = new MultiSigProofPayload
        {
            Magic = (uint)obj["magic"].AsNumber(),
            AddressVersion = (byte)obj["addressVersion"].AsNumber(),
            Address = obj["address"].AsString(),
            Challenge = obj["challenge"].AsString(),
            Timestamp = (long)obj["timestamp"].AsNumber(),
            M = (int)obj["m"].AsNumber(),
            PublicKeys = ((JArray)obj["publicKeys"]).Select(p => p.AsString()).ToArray(),
            Signatures = new Dictionary<string, string>(StringComparer.Ordinal),
        };

        var sigs = obj["signatures"];
        if (sigs != null)
        {
            foreach (var prop in sigs.Properties)
                payload.Signatures[prop.Key] = prop.Value.AsString();
        }
        return payload;
    }

    /// <summary>Cheap discriminator used by callers reading an unknown JSON blob.</summary>
    public static bool IsMultiSigJson(string json)
    {
        try
        {
            var obj = JObject.Parse(json);
            return string.Equals(obj["schema"]?.AsString(), SchemaVersion, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
