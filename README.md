# ChatFreePT - WIP - EXPECT BUGS!

A free, open-source Windows chat client for [Duck.ai](https://duck.ai) — DuckDuckGo's AI chat service — with built-in proxy discovery and automatic rotation. No API key required.

![ChatFreePT Screenshot](docs/screenshot.png)

---

## Features

- **Free AI chat** — talks directly to Duck.ai with no account or API key
- **Multiple models** — GPT-4o mini, Claude 3 Haiku, Llama 3.1 70B, Mixtral 8x7B, o3-mini
- **Automatic proxy discovery** — tests tens of thousands of fresh proxies in parallel
- **Automatic proxy rotation** — switches proxy silently on 418 / rate-limit errors
- **Dark-themed WinForms UI** — live streaming response display
- **Console client** — `DuckStudio` for headless / scripted use
- **No browser required** — VQD bot-detection challenge solved entirely in C#

---

## Projects

| Project | Type | Description |
|---|---|---|
| `ChatFreePT` | WinForms app | GUI client with proxy controls |
| `DuckStudio` | Console app | CLI client |
| `ProxyPool` | Class library | Proxy discovery, testing, and rotation engine |

---

## Requirements

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

---

## Getting Started

```
git clone https://github.com/yourname/ChatFreePT.git
cd ChatFreePT
dotnet run --project ChatFreePT
```

Or open `ChatFreePT.sln` in Visual Studio and press **F5**.

---

## GUI — ChatFreePT

### Layout

```
┌─────────────────────────────────────┬──────────────────────────┐
│                                     │  Proxy                   │
│                                     │  Proxy Sources           │
│          Chat window                │  Discovery Settings      │
│                                     │  Connection              │
│─────────────────────────────────────│                          │
│  Message input               [Send] │                          │
└─────────────────────────────────────┴──────────────────────────┘
```

### Proxy panel

| Control | Purpose |
|---|---|
| **Use Proxy** checkbox | Enables the proxy subsystem |
| **Find Proxies** | Downloads and tests proxies from the selected sources |
| **Stop** | Cancels an in-progress discovery run |
| Progress bar + counters | Live working / tested counts, refreshed every 250 ms |
| Found proxies list | Top working proxies ranked by reliability score |

### Proxy Sources

Tick any combination of the 13 built-in lists before clicking Find Proxies:

| Source | Protocol | On by default |
|---|---|---|
| TheSpeedX | HTTP | ✓ |
| TheSpeedX | SOCKS4 / SOCKS5 | |
| ShiftyTR | HTTP | ✓ |
| ShiftyTR | SOCKS4 / SOCKS5 | |
| clarketm | HTTP | |
| monosans | SOCKS4 / SOCKS5 | |
| monosans anonymous | SOCKS4 / SOCKS5 | |
| roosterkid | SOCKS4 / SOCKS5 | |

### Discovery Settings

| Setting | Range | Default | Effect |
|---|---|---|---|
| **Parallel** | 1 – 500 | 200 | How many proxies are tested simultaneously |
| **Timeout (s)** | 1 – 120 | 3 | Per-proxy connection timeout |
| **Delay (ms)** | 0 – 10 000 | 0 | Pause between starting each new test (throttles bandwidth) |

### Connection panel

| Button | Action |
|---|---|
| **Connect** | Opens a Duck.ai session using the best available proxy |
| **Cancel** | Aborts a connection attempt mid-flight |
| **Next Proxy** | Marks the current proxy as failed and reconnects with the next one |
| **Reconnect** | Re-initialises the session on the same proxy |
| **Clear Chat** | Wipes the display and resets conversation history |
| **Export** | Saves the conversation to a timestamped `.txt` file |

### Chat

- **Enter** — send message
- **Shift + Enter** — new line in the input box
- Responses stream in token-by-token

---

## Supported Models

| Model | ID |
|---|---|
| GPT-4o mini | `gpt-4o-mini` |
| Claude 3 Haiku | `claude-3-haiku-20240307` |
| Llama 3.1 70B | `meta-llama/Meta-Llama-3.1-70B-Instruct-Turbo` |
| Mixtral 8x7B | `mistralai/Mixtral-8x7B-Instruct-v0.1` |
| o3-mini | `o3-mini` |

---

## Console Client — DuckStudio

```
dotnet run --project DuckStudio
```

```
Use proxies? (y/n): y
Finding and testing proxies, please wait...
[INFO] √ Found working proxy: 1.2.3.4:8080 (Http)
...
Ready: 14 working proxies found.
Connected to Duck.ai successfully!
Commands: quit, clear, reconnect, models, model:<name>, export

YOU: explain quantum entanglement simply
Duck.ai: ...
```

**Commands**

| Command | Action |
|---|---|
| `quit` | Exit |
| `clear` | Clear conversation history |
| `reconnect` | Re-initialise Duck.ai session |
| `models` | List available models |
| `model:<name>` | Switch model, e.g. `model:gpt-4o-mini` |
| `export` | Print conversation to stdout |

---

## ProxyPool Library

`ProxyPool` is a standalone class library you can use in any .NET project.

### Quick start

```csharp
using ProxyPool;

var pool = new ProxyEnabledHttpClient(
    proxyListUrls: new[]
    {
        "https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/http.txt",
        "https://raw.githubusercontent.com/ShiftyTR/Proxy-List/master/http.txt",
    },
    testTimeoutSeconds:  3,
    fetchTimeoutSeconds: 20,
    maxParallelTests:    200);

// Discover working proxies in the background
await pool.DiscoverProxiesAsync(
    onProgress: (tested, working) => Console.WriteLine($"{working} working / {tested} tested"),
    cancellationToken: cts.Token);

// Pick the best proxy
WebProxy? proxy = pool.GetBestProxyWebProxy();

// Or fetch a URL through the pool with automatic rotation
string html = await pool.FetchHtmlAsync("https://example.com");
```

### Key API

| Member | Description |
|---|---|
| `DiscoverProxiesAsync(onProgress, delayBetweenStartsMs, ct)` | Test all proxies from every configured source URL |
| `GetBestProxyWebProxy()` | Best single proxy by reliability score |
| `GetHealthyProxyWebProxies(max)` | Top N proxies |
| `GetStatistics()` | Pool-wide stats: total, healthy, top-10 details |
| `FetchHtmlAsync(url, ct)` | Fetch a URL through the pool with automatic failover |
| `ReportProxyFailure(address)` | Manually mark a proxy as failed |

---

## How It Works

### VQD challenge

Duck.ai uses a JavaScript-based bot-detection token called a **VQD**. ChatFreePT solves it entirely in C# without a browser or JS runtime:

1. Fetches the obfuscated challenge script
2. Extracts and rotates the string table
3. Builds a deobfuscation lookup map
4. Simulates a DOM fingerprint using **AngleSharp**
5. Hashes the fingerprint with **SHA-256**
6. Base64-encodes the final token and sends it with every chat request

### Proxy rotation

- The pool scores each proxy with a reliability percentage based on success / failure history and average response time
- On a **418 I'm a Teapot** (DuckDuckGo's rate-limit response), the client automatically marks the proxy as failed and retries with the next-ranked one
- Rotation also happens silently during initialisation — if one proxy can't complete the full handshake (cookie, chat page, auth token, VQD), the next one is tried automatically

### Threading

All long-running work runs off the UI thread via `Task.Run`:

- Proxy discovery (`DiscoverProxiesAsync`)
- Duck.ai session initialisation (`EnsureInitializedAsync`)
- Message streaming (`StreamAskAsync`)

Progress is polled by a 250 ms `System.Windows.Forms.Timer` on the UI thread, so background threads never block waiting for UI updates.

---

## Dependencies

| Package | Used by | Purpose |
|---|---|---|
| [AngleSharp](https://github.com/AngleSharp/AngleSharp) | DuckStudio, ChatFreePT | HTML parsing for VQD DOM fingerprint |
| [PuppeteerSharp](https://github.com/hardkoded/puppeteer-sharp) | DuckStudio | Browser mode (optional) |

---

## License

MIT — see [LICENSE](LICENSE) for details.
