# DuckStudio - This works but it's WIP and still has an issue or two.

A .NET console application for chatting with DuckDuckGo's AI Chat service ([Duck.ai](https://duck.ai)). Supports multiple AI models with real-time streaming responses.

## Features

- **Two client modes:**
  - **API mode** (default) — lightweight HTTP client with full protocol implementation
  - **Browser mode** — Puppeteer-based Chrome automation for UI-level interaction
- **Multiple AI models** — GPT-4o Mini, Claude 3 Haiku, Llama 3.1 70B, Mixtral 8x7B, o3-mini
- **Real-time streaming** — responses stream token-by-token via Server-Sent Events
- **VQD challenge solver** — pure C# implementation of DuckDuckGo's JavaScript bot-detection challenge (no JS runtime required)
- **Conversation management** — history tracking, model switching, conversation export
- **Debug logging** — comprehensive event-based diagnostics for troubleshooting

## Requirements

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- For browser mode: Chrome/Chromium (auto-downloaded by PuppeteerSharp on first run)

## Getting Started

```bash
# Clone the repository
git clone https://github.com/johnbrodowski/DuckStudio.git
cd DuckStudio

# Run in API mode (default)
dotnet run

# Run in browser mode
dotnet run -- browser
```

## Usage

Once connected, type your message and press Enter. The AI response streams back in real time.

### Commands

| Command | Mode | Description |
|---------|------|-------------|
| `quit` | Both | Exit the application |
| `clear` | Both | Clear conversation history |
| `models` | API | List available AI models |
| `model:<name>` | API | Switch to a different model |
| `export` | API | Export conversation history to the console |
| `reconnect` | API | Force a fresh session (new cookies + VQD token) |

### Example Session

```
Connected to Duck.ai successfully!
Commands: quit, clear, reconnect, models, model:<name>, export


YOU: What is DuckDuckGo?

Duck.ai: DuckDuckGo is a privacy-focused search engine that doesn't track
your searches or build a personal profile on you...


YOU: models
Available models:
  - gpt-4o-mini
  - claude-3-haiku-20240307
  - meta-llama/Meta-Llama-3.1-70B-Instruct-Turbo
  - mistralai/Mixtral-8x7B-Instruct-v0.1
  - o3-mini


YOU: model:claude-3-haiku-20240307
Switched to model: claude-3-haiku-20240307
```

## Architecture

### Project Structure

```
DuckStudio/
├── DuckStudio.sln       # Visual Studio solution file
├── DuckStudio.csproj    # Project file (.NET 10.0)
├── Program.cs           # CLI entry point and interactive chat loop
├── DuckApi.cs           # Core client implementations
├── LICENSE              # MIT License
└── README.md
```

### Key Classes

| Class | Description |
|-------|-------------|
| `DuckAIClient` | HTTP API client — handles authentication, VQD challenge solving, and SSE streaming |
| `DuckAIBrowserClient` | Puppeteer-based client — drives a headless Chrome instance against the Duck.ai web UI |
| `DuckAIChat` | High-level convenience wrapper around `DuckAIClient` with automatic initialization |
| `ChatMessage` | Data model for a single conversation message (role, content, timestamp) |

### How It Works

**API Mode** follows DuckDuckGo's browser protocol:

1. Sets the `access_type` cookie via a redirect endpoint
2. Loads the chat page to extract the front-end version and session cookies
3. Fetches an auth token
4. Obtains a VQD token by solving an obfuscated JavaScript challenge in pure C#
5. Sends chat messages as JSON and parses Server-Sent Events (SSE) for streaming responses
6. Automatically refreshes the VQD token from each response

**Browser Mode** automates a real Chrome instance:

1. Launches headless Chrome via PuppeteerSharp
2. Navigates to Duck.ai and handles the onboarding modal
3. Types messages into the chat textarea and monitors DOM changes for responses
4. Detects response completion via send-button state and text stability

### VQD Challenge Solver

DuckDuckGo uses an obfuscated JavaScript challenge for bot detection. DuckStudio solves this entirely in C#:

- Decodes the base64-encoded challenge JavaScript
- Extracts and deobfuscates a rotated string array via checksum matching
- Parses server hashes, challenge ID, timestamp, and XOR key from the obfuscated code
- Computes a DOM fingerprint using [AngleSharp](https://anglesharp.github.io/) (HTML5-compliant parsing)
- Assembles a signed response with SHA-256 hashed client fingerprints

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [AngleSharp](https://www.nuget.org/packages/AngleSharp) | 1.1.2 | HTML5 parsing for DOM fingerprint computation |
| [PuppeteerSharp](https://www.nuget.org/packages/PuppeteerSharp) | 21.0.1 | Headless Chrome automation (browser mode) |

## Using as a Library

The client classes can be used programmatically in your own .NET projects:

```csharp
using var chat = new DuckAIChat();

// Subscribe to streaming events
chat.OnResponseChunk += (sender, chunk) => Console.Write(chunk);
chat.OnError += (sender, ex) => Console.Error.WriteLine(ex.Message);

// Initialize and send a message
if (await chat.EnsureInitializedAsync())
{
    // Streaming (events fire as chunks arrive)
    await chat.StreamAskAsync("Explain quantum computing in simple terms");

    // Or get the complete response at once
    string response = await chat.AskAsync("What is 2 + 2?");
}
```

## Troubleshooting

- **HTTP 418 errors after a few messages**: This means the session/VQD token has expired. The client automatically detects this and re-initializes the session (fresh cookies + new VQD token) before retrying the failed message. If auto-recovery fails, type `reconnect` to manually force a new session. Conversation history is preserved across reconnections.
- **Initialization fails**: DuckDuckGo may change their challenge format. Check debug output (visible in Visual Studio Output window or with a debug listener) for detailed diagnostics.
- **Browser mode issues**: Ensure Chrome/Chromium can be downloaded. On Linux, you may need additional dependencies for headless Chrome (`apt-get install -y libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 libxrandr2 libgbm1 libpango-1.0-0 libcairo2 libasound2`).
- **Rate limiting**: DuckDuckGo may rate-limit requests. If you encounter errors, wait a moment before retrying.

## Disclaimer

This project is an independent, community-built tool and is **not affiliated with, endorsed by, or sponsored by DuckDuckGo**. It interacts with DuckDuckGo's publicly accessible AI Chat service. Use responsibly and in accordance with DuckDuckGo's [Terms of Service](https://duckduckgo.com/terms).

## License

This project is licensed under the [MIT License](LICENSE).
