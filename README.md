# NeoWalletProof

Wallet ownership proof tool for [Neo 2.13.0](https://www.nuget.org/packages/Neo/) (Neo Legacy).

> 中文文档：[README_CN.md](./README_CN.md)

Supports an **interactive console** and **CLI**. Network parameters come from `protocol.json` and `config.json` (same layout as neo-cli). ECDSA signatures prove control of the private key for a given Neo address.

---

## What it does

| Role | Responsibility |
|------|----------------|
| **Verifier** | Generate a one-time `challenge`, send it to the holder; on receiving JSON, run `verify` and confirm `challenge` matches |
| **Wallet holder** | Sign the `challenge` with a local wallet and return the JSON proof |

The verifier does **not** need the holder’s private key or a fully synced node (`prove` / `verify` work offline).

Both **single-sig** and **M-of-N multi-sig** addresses are supported. For multi-sig, the first holder produces a partial context; each remaining participant feeds it into `prove` again until the threshold is met, at which point the final proof JSON is emitted.

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (or run a published `NeoWalletProof.exe`)
- A wallet *or* a raw private key in any of these forms:
  - NEP-6 wallet (`.json`)
  - legacy SQLite wallet (`.db3`)
  - NEP-2 encrypted private key (`6P...`)
  - WIF private key (`K...` / `L...` / `5...`)
  - 64-char hex private key
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

Open from a wallet **file**:

```text
proof> open wallet D:\wallets\my.json
password: ****               ← masked with *; length = number of asterisks
Wallet opened.
  AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

proof> prove AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx dasidual12312839712984yjfabnkjb
```

Or open directly from a **private key** — just pass it after `open wallet` in place of the path:

```text
proof> open wallet Kxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Wallet opened from WIF private key.
  AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

proof> open wallet 6PYxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
NEP-2 password: ****
Wallet opened from NEP-2 private key.
  AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

proof> open wallet 7d128a9d7a4e9...  (64 hex chars)
Wallet opened from hex private key.
  AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

proof> prove AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx dasidual12312839712984yjfabnkjb
```

Format detection is automatic; you don't have to flag the kind. WIF and hex keys need no password; NEP-2 prompts for the passphrase used to encrypt the key.

Send the **Signed Output** JSON to the verifier.

### Holder (multi-sig — e.g. 3-of-4)

Each of the M required participants signs in turn.

For the **first signer** the wallet needs to know the multi-sig structure (M + the N participating pubkeys), because that's how the tool can tell `prove <multisig-address> <challenge>` should produce a multi-sig context instead of a single-sig proof. You have two ways to get there:

1. **You already have a NEP-6 wallet that imported the multi-sig** (e.g. via neo-cli's `import multisigaddress`). Just `open wallet …` and skip to the signing step below.
2. **You only have one of the participating private keys** (WIF / NEP-2 / hex). Open it directly, then register the multi-sig in-memory with the new `import multisig` command — this mirrors neo-cli's `import multisigaddress` exactly:

   ```text
   proof> open wallet Kxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx     # your WIF
   Wallet opened from WIF private key.
     AMxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx                                                # your single-sig address

   proof> import multisig 3 03A...pkA 02B...pkB 02C...pkC 02D...pkD
   Multisig. Addr.: AagDaGRSn5iEpCf35L6nMeyAoNsPYRosmR
     signable as participant: 03A...pkA
   ```

   Nothing is written to disk — the multi-sig contract lives only in this session's wallet. `prove` will now see it.

Every subsequent signer only needs to hold one of the participating private keys (file, WIF, NEP-2, or hex all work — no `import multisig` required, because the partial context JSON already carries M and the full pubkey list).

**Signer 1 (first):**

```text
proof> open wallet D:\wallets\holder-A.json
proof> prove <multisig-address> <challenge>
Partial proof: 1/3 signatures. Hand this JSON to the next signer (2 more needed):
{ "schema": "1-multisig", ..., "signatures": { "02A...": "<sig>" } }
```

Send the partial JSON to signer 2.

**Signer 2:**

```text
proof> open wallet Kxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx        ← WIF works too
proof> prove D:\received\partial.json
Partial proof: 2/3 signatures. Hand this JSON to the next signer (1 more needed):
{ ..., "signatures": { "02A...":"...", "02B...":"..." } }
```

**Signer 3 (final):**

```text
proof> open wallet D:\wallets\holder-C.json
proof> prove D:\received\partial2.json
Multi-sig proof complete (3/3). Signed Output:
{ ..., "signatures": { "02A...":"...", "02B...":"...", "02C...":"..." } }
```

That last JSON is the final proof — send it to the verifier exactly like the single-sig case.

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
| `open wallet <input>` | Open from `.json` / `.db3` file, NEP-2 (`6P...`), WIF (`K/L/5...`), or 64-char hex. Password prompted only for file / NEP-2. |
| `close wallet` | Close wallet |
| `import multisig <m> <pk1> ... <pkN>` | Register an M-of-N multi-sig contract in the **open** wallet. Prints `Multisig. Addr.: <addr>` (neo-cli compatible). Alias: `import multisigaddress`. |
| `list address` | List wallet addresses |
| `prove <address> <challenge>` | Start a proof for `<address>`. Auto-detects single-sig vs multi-sig from the wallet contract. |
| `prove <file\|json>` | Add **my** signature to a partial multi-sig proof context and emit the updated JSON. |
| `verify <file-path\|json>` | Verify proof JSON (single- or multi-sig) |
| `protocol` | Show Magic, AddressVersion from `protocol.json` |
| `help` / `clear` / `exit` | Help, clear screen, quit |

> **Note:** The verifier must generate and deliver `challenge`; this tool does not mint nonces on the client.

---

## CLI (non-interactive)

For scripts and automation.

### Holder: create proof

From a wallet file:

```powershell
dotnet run --project NeoWalletProof -- prove `
  -w "D:\wallets\my.json" `
  -p "wallet-password" `
  -a "AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" `
  -c "dasidual12312839712984yjfabnkjb" `
  -o "proof.json"
```

From a WIF private key (no `-p`, `-a` optional because the imported key has a single address):

```powershell
dotnet run --project NeoWalletProof -- prove `
  -w "Kxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" `
  -c "dasidual12312839712984yjfabnkjb" `
  -o "proof.json"
```

From a NEP-2 encrypted private key (`-p` is the passphrase used to encrypt it):

```powershell
dotnet run --project NeoWalletProof -- prove `
  -w "6PYxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" `
  -p "nep2-passphrase" `
  -c "dasidual12312839712984yjfabnkjb"
```

For a 3-of-4 multi-sig, each subsequent signer feeds the partial context back into `prove` with `-k`:

```powershell
# First signer (wallet must have the multi-sig contract imported)
dotnet run --project NeoWalletProof -- prove `
  -w "D:\wallets\holder-A.json" -p "pwA" `
  -a "<multisig-address>" -c "<challenge>" -o partial.json

# Second signer — any wallet format that holds one of the participating keys
dotnet run --project NeoWalletProof -- prove `
  -w "<WIF-or-wallet-of-B>" [-p "<pw>"] `
  -k partial.json -o partial.json

# Third signer — same shape; this one completes the proof
dotnet run --project NeoWalletProof -- prove `
  -w "<WIF-or-wallet-of-C>" [-p "<pw>"] `
  -k partial.json -o proof.json
```

A short status line (`# multi-sig proof partial — 2/3 signatures, 1 more needed`) is written to **stderr** after each step so scripts can react; the actual context JSON always goes to stdout / `-o`.

| Option | Meaning |
|--------|---------|
| `-w` / `--wallet` | Wallet file path **or** raw private key (WIF / NEP-2 / hex) |
| `-p` / `--password` | Required for file wallets and NEP-2 keys; ignored for WIF / hex |
| `-a` / `--address` | Address to prove. Optional when the wallet contains a single signable account. Ignored with `-k`. |
| `-c` / `--challenge` | One-time nonce from verifier (required when starting; ignored with `-k`) |
| `-k` / `--continue` | Path to (or inline JSON of) a partial multi-sig context to add my signature to |
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

### Single-sig (`schema: "1"`)

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

### Multi-sig (`schema: "1-multisig"`)

The same JSON shape is used for partial contexts and the final completed proof — `signatures.length >= m` distinguishes them.

```json
{
  "schema": "1-multisig",
  "magic": 7630401,
  "addressVersion": 23,
  "address": "A...multisig...",
  "challenge": "dasidual12312839712984yjfabnkjb",
  "timestamp": 1710000000,
  "m": 3,
  "publicKeys": ["02..A", "02..B", "02..C", "03..D"],
  "signatures": {
    "02..A": "<sig>",
    "02..B": "<sig>",
    "02..C": "<sig>"
  }
}
```

| Field | Meaning |
|-------|---------|
| `m` | Threshold (signatures required) |
| `publicKeys` | All N participating compressed pubkeys, in redeem-script order. Hashing `CreateMultiSigRedeemScript(m, publicKeys)` MUST yield `address`. |
| `signatures` | Map of `pubkey-hex → sig-hex`. Each entry is verified independently against the same plaintext used in single-sig. |
| `timestamp` | Set by the first signer; **reused unchanged** by every subsequent signer. |

---

## Verification (brief)

The same `verify` command handles both formats; it dispatches on the `schema` field.

**Single-sig (`schema: "1"`):**

1. Rebuild the signed plaintext (`magic`, `address`, `challenge`, `timestamp`)
2. `SHA256(UTF8(plaintext))` then ECDSA verify with `publicKey`
3. Confirm `address` matches `Hash160(CreateSignatureRedeemScript(publicKey))`
4. Confirm `magic` / `addressVersion` match `protocol.json`

**Multi-sig (`schema: "1-multisig"`):**

1. Reject if `signatures.length < m` (incomplete)
2. Confirm `address` matches `Hash160(CreateMultiSigRedeemScript(m, publicKeys))`
3. For each `(pubkey, sig)` in `signatures`: pubkey must appear in `publicKeys`, and ECDSA verify against the same plaintext
4. Confirm at least `m` distinct valid signatures
5. Confirm `magic` / `addressVersion` match `protocol.json`

In both cases the verifier must also check `challenge` equals the nonce issued for this session (replay protection).

---

## FAQ

**Q: `prove` says `You have to open the wallet first.`**  
A: Run `open wallet <path>` first.

**Q: `INVALID — Magic mismatch`**  
A: Holder and verifier use different networks (mainnet vs testnet `protocol.json`).

**Q: Crypto valid but you want to reject login?**  
A: Compare `challenge` in JSON with your session nonce; this tool does not manage sessions.

---

## Project layout

```
NeoWalletProof/
├── README.md
├── README_CN.md             # Chinese guide
├── NuGet.Config
├── NeoWalletProof.sln
└── NeoWalletProof/
    ├── Program.cs
    ├── protocol.json
    ├── config.json
    ├── Configuration/       # Load protocol / config
    ├── Models/              # WalletProofPayload / MultiSigProofPayload
    ├── Services/            # Sign, verify, open wallet, import multisig
    ├── Shell/               # Interactive console
    └── Cli/                 # Non-interactive CLI
```

---

## Dependencies

- NuGet: [Neo 2.13.0](https://www.nuget.org/packages/Neo/)

## License

Align with the Neo project; evaluate security and compliance before production use.
