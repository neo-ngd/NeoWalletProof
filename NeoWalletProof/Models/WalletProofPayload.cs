using Neo;
using Neo.Cryptography;
using Neo.IO.Json;
using Neo.SmartContract;
using Neo.Wallets;
using System.Text;

namespace NeoWalletProof.Models;

/// <summary>
/// Offline wallet-ownership proof using the same ECDSA (secp256r1 + SHA256) as Neo legacy.
/// </summary>
public sealed class WalletProofPayload
{
    public const string SchemaVersion = "1";

    public uint Magic { get; set; }
    public byte AddressVersion { get; set; }
    public string Address { get; set; } = "";
    public string Challenge { get; set; } = "";
    public long Timestamp { get; set; }
    public string PublicKey { get; set; } = "";
    public string Signature { get; set; } = "";

    public static WalletProofPayload Create(string address, string challenge, KeyPair key)
    {
        var settings = ProtocolSettings.Default;
        var scriptHash = address.ToScriptHash();
        var accountHash = Contract.CreateSignatureRedeemScript(key.PublicKey).ToScriptHash();
        if (!scriptHash.Equals(accountHash))
            throw new InvalidOperationException("Address does not match the wallet key.");

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var message = BuildMessage(settings.Magic, address, challenge, timestamp);
        var messageHash = Encoding.UTF8.GetBytes(message).Sha256();
        var pubBytes = key.PublicKey.EncodePoint(false).Skip(1).ToArray();
        var signature = Crypto.Default.Sign(messageHash, key.PrivateKey, pubBytes);

        return new WalletProofPayload
        {
            Magic = settings.Magic,
            AddressVersion = settings.AddressVersion,
            Address = address,
            Challenge = challenge,
            Timestamp = timestamp,
            PublicKey = key.PublicKey.EncodePoint(true).ToHexString(),
            Signature = signature.ToHexString()
        };
    }

    public static string BuildMessage(uint magic, string address, string challenge, long timestamp)
    {
        return $"NeoWalletProof/{SchemaVersion}\nmagic:{magic}\naddress:{address}\nchallenge:{challenge}\ntimestamp:{timestamp}";
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
        json["publicKey"] = PublicKey;
        json["signature"] = Signature;
        return json;
    }

    public static WalletProofPayload FromJson(string json)
    {
        var obj = JObject.Parse(json);
        return new WalletProofPayload
        {
            Magic = (uint)obj["magic"].AsNumber(),
            AddressVersion = (byte)obj["addressVersion"].AsNumber(),
            Address = obj["address"].AsString(),
            Challenge = obj["challenge"].AsString(),
            Timestamp = (long)obj["timestamp"].AsNumber(),
            PublicKey = obj["publicKey"].AsString(),
            Signature = obj["signature"].AsString()
        };
    }
}
