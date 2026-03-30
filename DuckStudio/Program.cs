using System.Diagnostics;
using ProxyPool;

// DuckStudio - A .NET chat client for DuckDuckGo's AI Chat (Duck.ai)
// Supports two modes: "api" (HTTP client) and "browser" (Puppeteer-based)

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "api";

if (mode != "api" && mode != "browser")
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Unknown mode: '{mode}'. Use 'api' (default) or 'browser'.");
    Console.ResetColor();
    return;
}

Debug.WriteLine("Duck.ai Chat Client - Debug Mode");
Debug.WriteLine($"=================================  (mode: {mode})\n");

// Ask whether to use proxies (only relevant for api mode, browser uses its own network stack)
ProxyEnabledHttpClient? proxyPool = null;
if (mode == "api")
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Use proxies? (y/n): ");
    Console.ResetColor();
    var proxyAnswer = Console.ReadLine()?.Trim().ToLowerInvariant();

    if (proxyAnswer == "y" || proxyAnswer == "yes")
    {
        var proxyListUrls = new List<string>
        {
            "https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/http.txt",
            "https://raw.githubusercontent.com/clarketm/proxy-list/master/proxy-list-raw.txt",
            "https://raw.githubusercontent.com/ShiftyTR/Proxy-List/master/http.txt",
        };

        proxyPool = new ProxyEnabledHttpClient(
            proxyListUrls: proxyListUrls,
            testTimeoutSeconds: 3,      // fast per-proxy timeout
            fetchTimeoutSeconds: 20,
            maxParallelTests: 200,      // high parallelism
            maxRetries: 1,
            allowDirectFallback: false);

        // Start discovery in background — runs until cancelled
        var discoveryCts = new CancellationTokenSource();
        var discoveryTask = proxyPool.DiscoverProxiesAsync(
            onProgress: (tested, working) =>
            {
                if (working > 0)
                {
                    Console.Write($"\r  Testing proxies... {tested} checked, {working} working   ");
                }
            },
            cancellationToken: discoveryCts.Token);

        // Wait until at least one working proxy is ready (or 90s timeout)
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  Testing proxies...");
        Console.ResetColor();

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (proxyPool.GetStatistics().HealthyProxies == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(300);

        Console.WriteLine();

        var stats = proxyPool.GetStatistics();
        if (stats.HealthyProxies == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No working proxies found. Continuing without a proxy.");
            Console.ResetColor();
            discoveryCts.Cancel();
            proxyPool.Dispose();
            proxyPool = null;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{stats.HealthyProxies} working proxies ready. Discovery continues in background.");
            Console.ResetColor();
            // Let discovery keep running in the background — don't await or cancel it
            _ = discoveryTask.ContinueWith(t =>
            {
                var s = proxyPool?.GetStatistics();
                if (s != null)
                    Console.WriteLine($"\n[Proxy discovery done: {s.HealthyProxies} total working proxies]");
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }
}

if (mode == "browser")
{
    using var chat = new DuckAIBrowserClient();

    chat.OnDebugOutput += (s, msg) => Debug.WriteLine(msg);
    chat.OnResponseChunk += (s, chunk) =>
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(chunk);
        Console.ResetColor();
    };
    chat.OnResponseComplete += (s, response) => Debug.WriteLine("\n[Response Complete]");
    chat.OnError += (s, ex) => Debug.WriteLine($"[ERROR] {ex.Message}");

    Debug.WriteLine("Initializing Duck.ai browser client...\n");

    try
    {
        await chat.InitializeAsync();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to initialize Duck.ai browser client: {ex.Message}");
        Console.ResetColor();
        Debug.WriteLine($"Initialization error: {ex}");
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Connected to Duck.ai (browser mode)!");
    Console.ResetColor();
    Console.WriteLine("Commands: quit, clear\n");

    await RunChatLoop(
        askAsync: input => chat.AskAsync(input),
        clearAction: () =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Conversation cleared.");
            Console.ResetColor();
        });
}
else
{
    using var chat = new DuckAIChat(proxyPool);

    chat.OnDebugOutput += (s, msg) => Debug.WriteLine(msg);
    chat.OnResponseChunk += (s, chunk) =>
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(chunk);
        Console.ResetColor();
    };
    chat.OnResponseComplete += (s, response) => Debug.WriteLine("\n[Response Complete]");
    chat.OnError += (s, ex) => Debug.WriteLine($"[ERROR] {ex.Message}");

    Debug.WriteLine("Initializing Duck.ai HTTP client...\n");

    if (!await chat.EnsureInitializedAsync())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Failed to initialize Duck.ai HTTP client. Check debug output for details.");
        Console.ResetColor();
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Connected to Duck.ai successfully!");
    Console.ResetColor();
    Console.WriteLine("Commands: quit, clear, reconnect, models, model:<name>, export\n");

    await RunChatLoop(
        askAsync: async input =>
        {
            if (input.Equals("models", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Available models:");
                foreach (var model in DuckAIClient.AvailableModels)
                    Console.WriteLine($"  - {model}");
                Console.ResetColor();
                return null; // signal: command handled internally
            }

            if (input.StartsWith("model:", StringComparison.OrdinalIgnoreCase))
            {
                var modelName = input.Substring(6).Trim();
                try
                {
                    chat.SetModel(modelName);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Switched to model: {modelName}");
                    Console.ResetColor();
                }
                catch (ArgumentException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.ResetColor();
                }
                return null;
            }

            if (input.Equals("export", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine(chat.ExportConversation());
                Console.ResetColor();
                return null;
            }

            if (input.Equals("reconnect", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Reconnecting...");
                Console.ResetColor();
                if (await chat.ReconnectAsync())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Reconnected successfully!");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Reconnection failed. Try again or restart the application.");
                    Console.ResetColor();
                }
                return null;
            }

            await chat.StreamAskAsync(input);
            return "";
        },
        clearAction: () =>
        {
            chat.ClearConversation();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Conversation cleared.");
            Console.ResetColor();
        });

    proxyPool?.Dispose();
}

/// <summary>
/// Main interactive chat loop. Reads user input, handles built-in commands
/// (quit, clear), and delegates questions to the provided async handler.
/// </summary>
async Task RunChatLoop(Func<string, Task<string?>> askAsync, Action clearAction)
{
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("\n\nYOU: ");
        Console.ResetColor();

        var input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
            continue;

        if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            break;

        if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            clearAction();
            continue;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\nDuck.ai: ");
        Console.ResetColor();

        try
        {
            await askAsync(input);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError: {ex.Message}");
            Console.ResetColor();
            Debug.WriteLine($"\nUnable to complete request: {ex}");
        }
    }
}
