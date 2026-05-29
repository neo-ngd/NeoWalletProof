# NeoWalletProof（中文）

基于 [Neo 2.13.0](https://www.nuget.org/packages/Neo/)（Neo Legacy）的**钱包所有权离线证明工具**。

> 英文版：[README.md](./README.md)

工具提供 **交互式终端** 和 **命令行（CLI）** 两种用法。网络参数来自 `protocol.json` / `config.json`（与 neo-cli 同构）。底层使用 secp256r1 + SHA256 的 ECDSA 签名证明对某个 Neo 地址下私钥的控制权。

---

## 它是干什么的

| 角色 | 职责 |
|------|------|
| **验证方（Verifier）** | 生成一次性 `challenge`（nonce）发给持有人；收到 JSON 后跑 `verify` 并核对 `challenge` 一致 |
| **钱包持有人（Holder）** | 用本地钱包对 `challenge` 签名，把 JSON 回送给验证方 |

验证方**不需要**持有人的私钥，也**不需要**同步好的全节点；`prove` 与 `verify` 均完全离线。

支持 **单签** 与 **M-of-N 多签** 地址。多签场景下，第一位签名者输出"部分上下文（partial context）"JSON，后续每位参与者把它喂回 `prove` 继续签名，达到阈值 M 时输出最终的 proof JSON。

---

## 支持的钱包/私钥输入

`open wallet` 后面可以跟下列任意一种，自动识别格式：

- NEP-6 钱包文件（`.json`）
- 旧版 SQLite 钱包文件（`.db3`）
- NEP-2 加密私钥（`6P` 开头）
- WIF 私钥（`K` / `L` / `5` 开头）
- 64 字符的 hex 私钥

文件钱包和 NEP-2 会提示输入密码；WIF 和 hex 无需密码。

---

## 环境要求

- [.NET 8 SDK](https://dotnet.microsoft.com/download)（或直接运行已编译的 `NeoWalletProof.exe`）
- 可执行文件同目录下需要有 `protocol.json`（主网 / 测试网必须与验证双方对齐）

---

## 编译与运行

```powershell
cd NeoWalletProof
dotnet build
dotnet run --project NeoWalletProof
```

或者直接跑产物：

```powershell
.\NeoWalletProof\bin\Debug\net8.0\NeoWalletProof.exe
```

- 不带参数 → 进入交互终端（提示符 `proof>`）
- 带子命令 → 一次性 CLI 模式（详见下文）

测试网：把 `protocol.testnet.json` 复制覆盖 `protocol.json` 即可。

---

## 5 分钟快速上手（单签）

### 验证方（你的服务端）

1. 生成一个随机字符串作为 `challenge`，如 `dasidual12312839712984yjfabnkjb`
2. 入库或者放到 session 里（只能用一次）
3. 把它发给钱包持有人
4. 收到对方的 JSON 后运行 `verify`，并核对 JSON 里的 `challenge` 与第 1 步发出去的一致

### 持有人（钱包侧）

**从钱包文件打开：**

```text
proof> open wallet D:\wallets\my.json
password: ****               ← 屏幕用 * 遮挡；星号数 = 实际密码长度
Wallet opened.
  AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

proof> prove AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx dasidual12312839712984yjfabnkjb
```

**直接从私钥打开**（把私钥串拼在 `open wallet` 后面就行，格式自动识别）：

```text
proof> open wallet Kxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx     # WIF
Wallet opened from WIF private key.
  AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

proof> open wallet 6PYxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx     # NEP-2
NEP-2 password: ****
Wallet opened from NEP-2 private key.
  AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

proof> open wallet 7d128a9d7a4e9...        # 64 字符的 hex 私钥
Wallet opened from hex private key.
  AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

proof> prove AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx dasidual12312839712984yjfabnkjb
```

把屏幕上 `Signed Output:` 之后的那段 JSON 整体复制发给验证方。

### 验证方核验

```text
proof> verify D:\received\proof.json
```

成功输出：

```text
VALID — signature proves control of the wallet private key.
  address: AXxxxxxxxx...
  challenge: dasidual12312839712984yjfabnkjb
```

---

## 多签快速上手（以 3-of-4 为例）

总共 4 位参与者 A / B / C / D，需要其中任意 3 位接力签名才算完成。

### 第 1 位（A）—— 启动多签证明

第一位签名者所在的钱包**必须能识别多签结构**（M 和全部 N 个参与公钥）。两条途径任选其一：

**方式 1：钱包里已经导入过多签合约**

比如曾经用 neo-cli 跑过 `import multisigaddress`，那个多签账户已经躺在 NEP-6 文件里。直接 `open wallet …` 即可，跳到下面的签名步骤。

**方式 2：你手上只有一把参与多签的私钥**

用 `import multisig` 命令在内存中注册多签合约（**不写盘**，行为与 neo-cli 的 `import multisigaddress` 一致）：

```text
proof> open wallet Kxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx     # 你自己的 WIF
Wallet opened from WIF private key.
  AMxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx                                          # 你的单签地址

proof> import multisig 3 03A...pkA 02B...pkB 02C...pkC 02D...pkD
Multisig. Addr.: AagDaGRSn5iEpCf35L6nMeyAoNsPYRosmR
  signable as participant: 03A...pkA                                          # 工具确认你是参与者之一
```

接下来开始签：

```text
proof> prove AagDaGRSn5iEpCf35L6nMeyAoNsPYRosmR <challenge>
Partial proof: 1/3 signatures. Hand this JSON to the next signer (2 more needed):
{ "schema": "1-multisig", ..., "signatures": { "03A...": "<sig>" } }
```

把这段 JSON 发给 B。

### 第 2 / 3 位 —— 接力签名

后续签名者**不需要**再 `import multisig`，因为 partial JSON 里已经写明了 M 和全部公钥。每个人只要本地钱包持有一把参与多签的私钥即可（文件、WIF、NEP-2、hex 均可）：

```text
# B 的会话
proof> open wallet Kxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
proof> prove D:\received\partial1.json
Partial proof: 2/3 signatures. Hand this JSON to the next signer (1 more needed):
{ ..., "signatures": { "03A...":"...", "02B...":"..." } }

# C 的会话（最后一签，完成）
proof> open wallet D:\wallets\holder-C.json
proof> prove D:\received\partial2.json
Multi-sig proof complete (3/3). Signed Output:
{ ..., "signatures": { "03A...":"...", "02B...":"...", "02C...":"..." } }
```

最后这段 JSON 就是最终 proof，发给验证方走和单签一样的 `verify` 流程。

> **小贴士**：`open wallet` 是**替换式**的，不会向旧钱包里追加私钥。如果你自己手上同时有 A、B 两把参与同一个多签的私钥，正确做法不是把它们塞进同一个会话，而是先 A 走完后输出 JSON、然后 `open wallet <WIF_B>`、再 `prove <partial.json>`——`prove <partial.json>` 这条分支会自动从当前钱包里挑一把"参与多签且还没签过"的私钥。

---

## 交互终端命令汇总

| 命令 | 说明 |
|------|------|
| `open wallet <input>` | 打开 `.json` / `.db3` 文件、NEP-2 (`6P...`)、WIF (`K/L/5...`)、64-char hex。文件 / NEP-2 会提示密码 |
| `close wallet` | 关闭当前钱包 |
| `import multisig <m> <pk1> ... <pkN>` | 向**当前打开**的钱包注册一个 M-of-N 多签合约（仅内存）。输出 `Multisig. Addr.: <addr>`。别名：`import multisigaddress` |
| `list address` | 列出钱包内所有可签名地址 |
| `prove <address> <challenge>` | 启动一次 proof。工具按账户合约脚本自动识别单签 / 多签 |
| `prove <file\|json>` | 给一份"多签部分上下文"加上**自己这一笔**签名，输出新的上下文 JSON |
| `verify <file\|json>` | 校验任意 proof JSON（单签 / 多签自动识别） |
| `protocol` | 显示 `protocol.json` 里的 `Magic` / `AddressVersion` |
| `help` / `clear` / `exit` | 帮助 / 清屏 / 退出 |

> **提示**：`challenge` 必须由验证方生成并下发，工具本身不负责生成 nonce。

---

## CLI（非交互，适合脚本）

### 生成 proof

**从钱包文件：**

```powershell
dotnet run --project NeoWalletProof -- prove `
  -w "D:\wallets\my.json" `
  -p "wallet-password" `
  -a "AXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" `
  -c "dasidual12312839712984yjfabnkjb" `
  -o "proof.json"
```

**从 WIF**（无需 `-p`，单账户场景 `-a` 也可省略）：

```powershell
dotnet run --project NeoWalletProof -- prove `
  -w "Kxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" `
  -c "dasidual12312839712984yjfabnkjb" `
  -o "proof.json"
```

**从 NEP-2**（`-p` 是加密用的 passphrase）：

```powershell
dotnet run --project NeoWalletProof -- prove `
  -w "6PYxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" `
  -p "nep2-passphrase" `
  -c "dasidual12312839712984yjfabnkjb"
```

**多签 3-of-4 接力**（后续签名者用 `-k` 喂入上一个 partial）：

```powershell
# 首签者（钱包里必须有多签合约，或者请用交互终端先 import multisig）
dotnet run --project NeoWalletProof -- prove `
  -w "D:\wallets\holder-A.json" -p "pwA" `
  -a "<multisig-address>" -c "<challenge>" -o partial.json

# 第 2 位 —— 任意持有参与公钥对应私钥的钱包形式均可
dotnet run --project NeoWalletProof -- prove `
  -w "<B 的 WIF 或钱包>" [-p "<密码>"] `
  -k partial.json -o partial.json

# 第 3 位 —— 完成
dotnet run --project NeoWalletProof -- prove `
  -w "<C 的 WIF 或钱包>" [-p "<密码>"] `
  -k partial.json -o proof.json
```

每次签名完成后，**stderr** 会输出一行状态（如 `# multi-sig proof partial — 2/3 signatures, 1 more needed`），方便脚本判断进度；实际的上下文 JSON 始终写到 stdout 或 `-o` 指定的文件。

| 参数 | 含义 |
|------|------|
| `-w` / `--wallet` | 钱包文件路径 **或** 原始私钥（WIF / NEP-2 / hex） |
| `-p` / `--password` | 文件钱包 & NEP-2 必填；WIF / hex 忽略 |
| `-a` / `--address` | 要签的地址。钱包里只有一个可签账户时可省略；与 `-k` 同用时忽略 |
| `-c` / `--challenge` | 验证方下发的一次性 nonce（启动签名时必填；与 `-k` 同用时忽略） |
| `-k` / `--continue` | 一份多签部分上下文的文件路径或内联 JSON——把我的签名加进去 |
| `-o` / `--out` | 可选的输出文件 |

### 验签

```powershell
dotnet run --project NeoWalletProof -- verify -f proof.json
```

| 退出码 | 含义 |
|--------|------|
| `0` | 验签通过（密码学校验成功） |
| `2` | 签名无效或字段不匹配 |
| `1` | 参数或解析错误 |

**业务层还需要自己额外校验：`proof.challenge == 当前会话发出的 nonce`**（防重放）。

---

## 配置文件

放在可执行文件同一目录下（同 neo-cli 习惯）。

### `protocol.json`

网络参数：`Magic`、`AddressVersion` 等。`verify` 会拿 proof 里的这两个字段和本文件比对。

测试网：把 `protocol.testnet.json` 复制成 `protocol.json` 即可。

### `config.json`

可选的开机自动开钱包：

```json
"UnlockWallet": {
  "Path": "D:\\wallets\\my.json",
  "Password": "your-password",
  "IsActive": true
}
```

`Path` 也接受私钥（WIF / NEP-2 / hex），逻辑与 `open wallet` 一致。

**生产环境别把明文密码写在这里。**

---

## Proof JSON 格式

### 单签（`schema: "1"`）

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

| 字段 | 含义 |
|------|------|
| `magic` / `addressVersion` | 网络标识；必须与验证方 `protocol.json` 一致 |
| `address` | 声明的 Neo 地址 |
| `challenge` | 验证方下发的一次性 nonce |
| `timestamp` | 签名时的 UTC 秒（已计入签名明文） |
| `publicKey` | 压缩格式公钥 |
| `signature` | ECDSA（secp256r1，Neo Legacy） |

### 多签（`schema: "1-multisig"`）

partial 与最终 proof **共用同一种 JSON 形状**——`signatures` 元素数 ≥ `m` 即为完成。

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

| 字段 | 含义 |
|------|------|
| `m` | 阈值（所需签名数） |
| `publicKeys` | 全部 N 个参与公钥（压缩格式），按 redeem-script 顺序排列。对其计算 `Hash160(CreateMultiSigRedeemScript(m, publicKeys))` 必须等于 `address` |
| `signatures` | `pubkey-hex → sig-hex` 的映射；每一条都按单签的同一段明文独立验签 |
| `timestamp` | 由首签者写入；后续签名者**保持不变**地复用 |

---

## 验签流程简述

`verify` 命令按 `schema` 字段分派到两套实现。

**单签 (`schema: "1"`)：**

1. 按 `magic / address / challenge / timestamp` 重建签名明文
2. `SHA256(UTF8(plaintext))`，用 `publicKey` 做 ECDSA 验签
3. 校验 `address == Hash160(CreateSignatureRedeemScript(publicKey))`
4. 校验 `magic` / `addressVersion` 与 `protocol.json` 一致

**多签 (`schema: "1-multisig"`)：**

1. `signatures` 数量 < `m` → 直接判失败（上下文未完成）
2. 校验 `address == Hash160(CreateMultiSigRedeemScript(m, publicKeys))`
3. 对 `signatures` 里的每一项 `(pubkey, sig)`：pubkey 必须在 `publicKeys` 列表中，且 ECDSA 验签通过
4. 至少有 `m` 笔互不相同的有效签名
5. 校验 `magic` / `addressVersion` 与 `protocol.json` 一致

> **两种场景下都必须**额外校验 `challenge` 等于当前会话发出去的 nonce —— 这是防重放的关键，工具本身不管会话。

---

## 常见问题

**Q：`prove` 报 `You have to open the wallet first.`**  
A：先跑 `open wallet <路径或私钥>`。

**Q：`INVALID — Magic mismatch`**  
A：持有人和验证方用了不同的网络（主网 vs 测试网 `protocol.json`）。

**Q：签名校验通过了，但我业务上还想拒绝登录？**  
A：自己比对 JSON 里的 `challenge` 是否等于本次会话发出的 nonce——本工具不管 session 状态。

**Q：单签地址和多签地址都长成 `A...`，工具怎么区分？**  
A：地址字符串本身**不携带**这个信息（都是 `Base58Check(addressVersion ‖ Hash160(redeemScript))`）。识别完全靠当前打开的钱包里、对应账户挂着的 redeem script 形状：`PUSH33 ... CHECKSIG` 是单签，`PUSH M ; PUSH33 pk … ; PUSH N ; CHECKMULTISIG` 是多签。所以**首签者**所在的钱包必须先有多签合约，要么是从 NEP-6 文件里直接带来的，要么通过 `import multisig` 在内存中注册。

**Q：我手头同时有多签的好几把私钥，能在同一个会话里全部签完吗？**  
A：当前 `open wallet` 是替换式的——不支持"同一钱包里塞多把私钥"。但你可以分多次 `open wallet → prove <partial.json>` 走完，每次只装当前签名者那把钥匙。`prove <partial.json>` 这条分支会从当前钱包里挑一把"参与多签且还没签过"的私钥来加签，所以没有"选哪把"的歧义。

**Q：`import multisig` 会不会把我的 NEP-6 钱包文件写脏？**  
A：**不会。** 与 neo-cli 不同，本工具不会在导入后调用 `wallet.Save()`，多签合约只活在当前会话的内存里。

---

## 项目结构

```
NeoWalletProof/
├── README.md           # 英文说明
├── README_CN.md        # 本文件
├── NuGet.Config
├── NeoWalletProof.sln
└── NeoWalletProof/
    ├── Program.cs
    ├── protocol.json
    ├── config.json
    ├── Configuration/  # 加载 protocol / config
    ├── Models/         # WalletProofPayload / MultiSigProofPayload
    ├── Services/       # 钱包打开、单/多签生成、验签、多签导入
    ├── Shell/          # 交互式终端
    └── Cli/            # 一次性 CLI
```

---

## 依赖

- NuGet: [Neo 2.13.0](https://www.nuget.org/packages/Neo/)

## 许可

与 Neo 项目保持一致；生产使用前请自行做安全与合规评估。
