# NeoWalletProof

Wallet ownership proof tool for [Neo 2.13.0](https://www.nuget.org/packages/Neo/) (Neo Legacy).

Supports an **interactive console** and **CLI**. Network parameters come from `protocol.json` and `config.json` (same layout as neo-cli). ECDSA signatures prove control of the private key for a given Neo address.

---

## What it does

| Role | Responsibility |
|------|----------------|
| **Verifier** | Generate a one-time `challenge`, send it to the holder; on receiving JSON, run `verify` and confirm `challenge` matches |
| **Wallet holder** | Sign the `challenge` with a local wallet and return the JSON proof |

The verifier does **not** need the holder’s private key or a fully synced node (`prove` / `verify` work offline).

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (or run a published `NeoWalletProof.exe`)
- NEP-6 wallet (`.json`) or legacy SQLite wallet (`.db3`)
- `protocol.json` next to the executable (mainnet/testnet must match neo-cli)

---

## Build and run

```powershell
cd NeoWalletProof
dotnet build
dotnet run --project NeoWalletProof
```

Or run from the output folder:

```powershell
.\NeoWalletProof\bin\Debug\net8.0\NeoWalletProof.exe
```

- No arguments → interactive console (`proof>` prompt)
- With subcommands → non-interactive CLI (see below)

---

## Quick start (5 minutes)

### Verifier (server / you)

1. Generate a random string, e.g. `dasidual12312839712984yjfabnkjb`
2. Store it in session / database (single use)
3. Send it privately to the wallet holder
4. When you receive their JSON, run `verify` and confirm `challenge` equals step 1

### Holder (user machine)

```text
proof> open wallet D:\wallets\my.json
password: ****               ← masked with *; length = number of asterisks

proof> list address
AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

proof> prove AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx dasidual12312839712984yjfabnkjb
```

Send the **Signed Output** JSON to the verifier.

### Verifier checks the proof

```text
proof> verify D:\received\proof.json
```

Success:

```text
VALID — signature proves control of the wallet private key.
  address: AXxxxxxxxx...
  challenge: dasidual12312839712984yjfabnkjb
```

---

## Interactive commands

Wallet commands follow neo-cli style:

| Command | Description |
|---------|-------------|
| `open wallet <path>` | Open NEP-6 / db3 wallet (password prompt) |
| `close wallet` | Close wallet |
| `list address` | List wallet addresses |
| `prove <address> <challenge>` | Sign proof JSON (`challenge` from verifier) |
| `verify <file-path\|json>` | Verify proof JSON (verifier) |
| `sign <json>` | Same as neo-cli `sign` (`ContractParametersContext`) |
| `verify-sign <file-path\|json>` | Verify neo-cli Signed Output |
| `protocol` | Show Magic, AddressVersion from `protocol.json` |
| `help` / `clear` / `exit` | Help, clear screen, quit |

> **Note:** The verifier must generate and deliver `challenge`; this tool does not mint nonces on the client.

---

## CLI (non-interactive)

For scripts and automation.

### Holder: create proof

```powershell
dotnet run --project NeoWalletProof -- prove `
  -w "D:\wallets\my.json" `
  -p "wallet-password" `
  -a "AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" `
  -c "dasidual12312839712984yjfabnkjb" `
  -o "proof.json"
```

| Option | Meaning |
|--------|---------|
| `-w` / `--wallet` | Wallet file path |
| `-p` / `--password` | Wallet password |
| `-a` / `--address` | Address to prove |
| `-c` / `--challenge` | One-time nonce from verifier (required) |
| `-o` / `--out` | Optional output file |

### Verifier: verify proof

```powershell
dotnet run --project NeoWalletProof -- verify -f proof.json
```

| Exit code | Meaning |
|-----------|---------|
| `0` | Valid (cryptographic check passed) |
| `2` | Invalid signature or mismatch |
| `1` | Argument or parse error |

Your application must also enforce: `proof.challenge == nonce you issued for this session`.

### neo-cli compatible signing (optional)

```powershell
dotnet run --project NeoWalletProof -- sign -w my.json -p pass -f context.json -o signed.json
dotnet run --project NeoWalletProof -- verify-sign -f signed.json
```

---

## Configuration files

Same placement as neo-cli (beside the executable).

### `protocol.json`

Network settings: `Magic`, `AddressVersion`, etc. Verification checks proof fields against this file.

For testnet, copy `protocol.testnet.json` to `protocol.json`.

### `config.json`

Optional auto-unlock on interactive startup:

```json
"UnlockWallet": {
  "Path": "D:\\wallets\\my.json",
  "Password": "your-password",
  "IsActive": true
}
```

**Do not store plaintext passwords in production.**

---

## Proof JSON format

Example `prove` output:

```json
{
  "schema": "1",
  "magic": 7630401,
  "addressVersion": 23,
  "address": "A...",
  "challenge": "dasidual12312839712984yjfabnkjb",
  "timestamp": 1710000000,
  "publicKey": "02...",
  "signature": "..."
}
```

| Field | Meaning |
|-------|---------|
| `magic` / `addressVersion` | Network id; must match verifier `protocol.json` |
| `address` | Claimed Neo address |
| `challenge` | One-time nonce from verifier |
| `timestamp` | UTC seconds at signing (included in signed plaintext) |
| `publicKey` | Compressed public key |
| `signature` | ECDSA (secp256r1, Neo Legacy) |

---

## Verification (brief)

1. Rebuild the signed plaintext (`magic`, `address`, `challenge`, `timestamp`)
2. `SHA256(UTF8(plaintext))` then ECDSA verify
3. Confirm `address` matches `publicKey` (standard single-sig contract)
4. Confirm `magic` / `addressVersion` match `protocol.json`

The verifier must also check `challenge` equals the nonce issued for this session (replay protection).

---

## FAQ

**Q: `prove` says `You have to open the wallet first.`**  
A: Run `open wallet <path>` first.

**Q: `INVALID — Magic mismatch`**  
A: Holder and verifier use different networks (mainnet vs testnet `protocol.json`).

**Q: Crypto valid but you want to reject login?**  
A: Compare `challenge` in JSON with your session nonce; this tool does not manage sessions.

**Q: Difference from neo-cli `sign`?**  
A: `prove` is for offline address ownership; `sign` signs `ContractParametersContext` (transactions, etc.) like neo-cli.

---

## Project layout

```
NeoWalletProof/
├── README.md
├── NuGet.Config
├── NeoWalletProof.sln
└── NeoWalletProof/
    ├── Program.cs
    ├── protocol.json
    ├── config.json
    ├── Configuration/     # Load protocol / config
    ├── Models/              # WalletProofPayload
    ├── Services/            # Sign, verify, open wallet
    ├── Shell/               # Interactive console
    └── Cli/                 # Non-interactive CLI
```

---

## Dependencies

- NuGet: [Neo 2.13.0](https://www.nuget.org/packages/Neo/)

## License

Align with the Neo project; evaluate security and compliance before production use.
