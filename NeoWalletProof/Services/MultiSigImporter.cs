using Neo;
using Neo.Cryptography.ECC;
using Neo.SmartContract;
using Neo.Wallets;

namespace NeoWalletProof.Services;

/// <summary>
/// Register an M-of-N multi-sig contract in an open wallet.
///
/// Mirrors neo-cli's <c>import multisigaddress</c> handler (see
/// neo-node <c>MainService.OnImportMultisigAddress</c>): build the multi-sig
/// contract via <see cref="Contract.CreateMultiSigContract"/>, locate one of
/// our keys among the participants (<c>null</c> if none → the account is
/// added watch-only), then <see cref="Wallet.CreateAccount(Contract, KeyPair)"/>.
///
/// We do <b>not</b> persist the wallet to disk afterwards — the change is
/// kept in memory only. That's the intended use case (a one-shot proof tool)
/// and avoids surprising the user by mutating their wallet file.
/// </summary>
internal static class MultiSigImporter
{
    public sealed record Result(string Address, bool Signable, string? SignerPublicKey);

    public static Result Import(Wallet wallet, int m, ECPoint[] publicKeys)
    {
        var contract = Contract.CreateMultiSigContract(m, publicKeys);

        var keyPair = wallet.GetAccounts()
            .FirstOrDefault(p => p.HasKey && SafeMatches(p, publicKeys))
            ?.GetKey();

        wallet.CreateAccount(contract, keyPair);

        return new Result(
            Address: contract.Address,
            Signable: keyPair != null,
            SignerPublicKey: keyPair?.PublicKey.EncodePoint(true).ToHexString());
    }

    private static bool SafeMatches(WalletAccount account, ECPoint[] publicKeys)
    {
        try
        {
            var key = account.GetKey();
            return key != null && publicKeys.Contains(key.PublicKey);
        }
        catch
        {
            return false;
        }
    }
}
