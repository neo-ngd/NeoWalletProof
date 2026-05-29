using Neo.Cryptography.ECC;
using Neo.SmartContract;
using Neo.VM;

namespace NeoWalletProof.Services;

/// <summary>
/// Parser for Neo Legacy multi-signature redeem scripts.
/// Layout: <c>PUSH M ; PUSH33 pk1 ; ... ; PUSH33 pkN ; PUSH N ; CHECKMULTISIG</c>.
/// </summary>
internal static class MultiSigScript
{
    public readonly record struct Parsed(int M, ECPoint[] PublicKeys);

    /// <summary>
    /// Parse a multi-sig redeem script into (M, pubkeys-in-script-order).
    /// Returns <c>null</c> when <paramref name="script"/> is not a multi-sig contract.
    /// </summary>
    public static Parsed? TryParse(byte[] script)
    {
        if (script == null || !script.IsMultiSigContract())
            return null;

        var i = 0;
        int m;
        switch (script[i])
        {
            case 1:                          // PUSHBYTES1 <m>
                m = script[++i];
                ++i;
                break;
            case 2:                          // PUSHBYTES2 <m-le16>
                m = script[++i] | (script[++i] << 8);
                ++i;
                break;
            default:                          // PUSH1..PUSH16
                m = script[i++] - (byte)OpCode.PUSH1 + 1;
                break;
        }

        var keys = new List<ECPoint>();
        while (script[i] == 33)
        {
            i++;
            keys.Add(ECPoint.DecodePoint(script.AsSpan(i, 33).ToArray(), ECCurve.Secp256r1));
            i += 33;
        }

        return new Parsed(m, keys.ToArray());
    }
}
