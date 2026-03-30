# About this code
This example was extracted from AGPA — my fully autonomous general-purpose agent (closed-source, ~150k LOC).

# ProxyPool

A high-performance .NET library for intelligent proxy management with automatic discovery, health monitoring, and smart rotation. Built for reliable web scraping and HTTP requests, ProxyPool automatically finds working proxies from public sources, tests them in parallel, tracks their reliability, and seamlessly rotates through the healthiest options—all with zero configuration required.

Perfect for bypassing rate limits, avoiding IP blocks, and ensuring continuous access to web resources.

[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

## Features

- **Automatic Proxy Discovery**: Fetches and parses proxy lists from multiple URLs
- **Intelligent Health Checking**: Continuously monitors proxy health and reliability
- **Smart Rotation**: Automatically rotates through working proxies based on reliability scores
- **Parallel Testing**: Tests multiple proxies concurrently for fast initialization
- **Retry Logic**: Built-in retry mechanism with configurable attempts
- **Multiple Proxy Types**: Supports HTTP, HTTPS, SOCKS4, and SOCKS5 proxies
- **Authentication Support**: Handles proxies with username/password authentication
- **Performance Tracking**: Monitors response times and reliability scores for each proxy
- **Fallback Options**: Optional direct connection fallback when all proxies fail
- **Thread-Safe**: Uses concurrent collections for safe multi-threaded access
- **Easy Integration**: Simple API with minimal configuration required

## Installation

### NuGet Package (Coming Soon)

```bash
dotnet add package ProxyPool
```

### Build from Source

```bash
git clone https://github.com/yourusername/ProxyPool.git
cd ProxyPool
dotnet build
```

## Quick Start

### Basic Usage

```csharp
using ProxyPool;

// Define proxy list sources
var proxyListUrls = new List<string>
{
    "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt",
    "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies/socks5.txt"
};

// Create the proxy-enabled client
using var proxyClient = new ProxyEnabledHttpClient(
    proxyListUrls: proxyListUrls,
    testTimeoutSeconds: 10,
    fetchTimeoutSeconds: 30,
    maxParallelTests: 50,
    maxRetries: 3,
    allowDirectFallback: false
);

// Fetch content through proxies
var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;
string html = await proxyClient.FetchHtmlAsync("https://example.com", cancellationToken);

Console.WriteLine($"Fetched {html.Length} characters");

// Get statistics
var stats = proxyClient.GetStatistics();
Console.WriteLine($"Healthy proxies: {stats.HealthyProxies}/{stats.TotalProxies}");
```

### Using ProxyManager (Simplified API)

```csharp
using ProxyPool;

var proxyListUrls = new List<string>
{
    "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
};

var proxyManager = new ProxyManager(
    proxyListUrls: proxyListUrls,
    minProxiesToFind: 10,
    maxProxyTestTimeSeconds: 60
);

// Initialize the proxy pool
await proxyManager.InitializeProxyPoolAsync();

// Fetch content
string html = await proxyManager.FetchHtmlAsync("https://example.com");
Console.WriteLine($"Successfully fetched content: {html.Length} bytes");
```

## Testing ProxyPool

ProxyPool includes both automated tests and an interactive demo application.

### Automated Tests (xUnit)

Run automated tests with `dotnet test` or in Visual Studio Test Explorer:

```bash
dotnet test
```

Or use the test runner scripts:
```bash
./run-tests.sh    # Linux/Mac
run-tests.bat     # Windows
```

**What's tested:**
- Proxy discovery and testing
- Statistics tracking
- Multiple request handling
- Both ProxyEnabledHttpClient and ProxyManager APIs

See [ProxyPool.Tests/README.md](ProxyPool.Tests/README.md) for details.

### Interactive Demo App

Watch ProxyPool work in real-time with the interactive console demo:

```bash
cd ProxyPool.Demo
dotnet run
```

Or from Visual Studio: Set `ProxyPool.Demo` as startup project and press F5.

**The demo shows:**
- ✓ Real-time proxy discovery and testing
- ✓ Detailed statistics and top proxies
- ✓ Color-coded output
- ✓ Both API styles (ProxyEnabledHttpClient and ProxyManager)

Perfect for understanding how ProxyPool works!

## Configuration Options

### ProxyEnabledHttpClient Constructor

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `proxyListUrls` | `IEnumerable<string>` | *Required* | URLs to fetch proxy lists from |
| `testTimeoutSeconds` | `int` | *Required* | Timeout for testing individual proxies |
| `fetchTimeoutSeconds` | `int` | *Required* | Timeout for fetching content through proxies |
| `maxParallelTests` | `int` | *Required* | Maximum number of parallel proxy tests (1-500) |
| `maxRetries` | `int` | `3` | Maximum retry attempts per proxy |
| `allowDirectFallback` | `bool` | `false` | Allow direct connection if all proxies fail |
| `userAgent` | `string` | Chrome UA | Custom user agent string |

### ProxyManager Constructor

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `proxyListUrls` | `List<string>` | *Required* | URLs to fetch proxy lists from |
| `minProxiesToFind` | `int` | `10` | Minimum working proxies to find |
| `maxProxyTestTimeSeconds` | `int` | `60` | Maximum time to spend testing proxies |

## Advanced Usage

### Custom User Agent

```csharp
using var proxyClient = new ProxyEnabledHttpClient(
    proxyListUrls: proxyUrls,
    testTimeoutSeconds: 10,
    fetchTimeoutSeconds: 30,
    maxParallelTests: 50,
    userAgent: "MyBot/1.0 (Custom User Agent)"
);
```

### Monitoring Proxy Health

```csharp
var stats = proxyClient.GetStatistics();

Console.WriteLine($"Total Proxies: {stats.TotalProxies}");
Console.WriteLine($"Healthy Proxies: {stats.HealthyProxies}");
Console.WriteLine($"Initialized: {stats.IsInitialized}");

Console.WriteLine("\nTop Performing Proxies:");
foreach (var proxy in stats.TopProxies.Take(5))
{
    Console.WriteLine($"  {proxy.Address}");
    Console.WriteLine($"    Type: {proxy.Type}");
    Console.WriteLine($"    Reliability: {proxy.ReliabilityScore:P0}");
    Console.WriteLine($"    Success Rate: {proxy.SuccessCount}/{(proxy.SuccessCount + proxy.FailureCount)}");
    Console.WriteLine($"    Avg Response: {proxy.AverageResponseTime.TotalMilliseconds:F0}ms");
}
```

### Handling Timeouts and Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

try
{
    string html = await proxyClient.FetchHtmlAsync("https://example.com", cts.Token);
    Console.WriteLine("Content fetched successfully");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation timed out");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
```

### Enabling Direct Fallback

```csharp
// Allow direct connection if all proxies fail
using var proxyClient = new ProxyEnabledHttpClient(
    proxyListUrls: proxyUrls,
    testTimeoutSeconds: 10,
    fetchTimeoutSeconds: 30,
    maxParallelTests: 50,
    allowDirectFallback: true  // Enable fallback
);
```

## Proxy List Format

ProxyPool supports standard proxy list formats:

```
# Simple format
127.0.0.1:1080
192.168.1.1:8080

# With authentication
username:password@proxy.example.com:1080

# With protocol prefix
socks5://127.0.0.1:1080
http://proxy.example.com:8080
```

### Supported Proxy Types

- HTTP (`http://`)
- HTTPS (`https://`)
- SOCKS4 (`socks4://`)
- SOCKS5 (`socks5://`)

## How It Works

1. **Initialization**: Fetches proxy lists from provided URLs
2. **Parsing**: Extracts proxy addresses and credentials
3. **Testing**: Tests proxies in parallel using test URLs
4. **Health Scoring**: Assigns reliability scores based on success/failure
5. **Rotation**: Automatically uses healthiest proxies first
6. **Monitoring**: Periodic health checks update proxy status
7. **Retry Logic**: Retries with different proxies on failure

### Proxy Selection Algorithm

ProxyPool uses a smart selection algorithm:
1. Prioritizes proxies with high reliability scores (0.0-1.0)
2. Considers average response times
3. Tracks success/failure counts
4. Marks proxies as unhealthy after 3 consecutive failures
5. Automatically discovers new proxies when needed

## API Reference

### ProxyEnabledHttpClient

#### Methods

**`Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken = default)`**
- Fetches HTML content from the specified URL using available proxies
- Returns: HTML content as a string, or empty string on failure
- Throws: `ArgumentException` if URL is invalid
- Throws: `OperationCanceledException` if operation is cancelled

**`ProxyPoolStatistics GetStatistics()`**
- Returns current statistics about the proxy pool
- Returns: `ProxyPoolStatistics` object with pool metrics

**`void Dispose()`**
- Cleans up resources and stops health check timer
- Always call when done using the client

### ProxyManager

#### Methods

**`Task InitializeProxyPoolAsync()`**
- Initializes the proxy pool by fetching and testing proxies
- Should be called before fetching content

**`Task<string> FetchHtmlAsync(string url)`**
- Simplified fetch method
- Returns: HTML content as a string

**`Task<ProxyEnabledHttpClient> GetProxyClientAsync()`**
- Returns the underlying ProxyEnabledHttpClient instance
- Returns: Configured `ProxyEnabledHttpClient`

### ProxyPoolStatistics

#### Properties

- `int TotalProxies`: Total number of proxies in the pool
- `int HealthyProxies`: Number of currently healthy proxies
- `bool IsInitialized`: Whether the pool has been initialized
- `List<ProxyStatistics> TopProxies`: Top 10 proxies by reliability

### ProxyStatistics

#### Properties

- `string Address`: Proxy address
- `int SuccessCount`: Number of successful requests
- `int FailureCount`: Number of failed requests
- `double ReliabilityScore`: Reliability score (0.0-1.0)
- `TimeSpan AverageResponseTime`: Average response time
- `ProxyType Type`: Proxy type (Http, Https, Socks4, Socks5)

## Best Practices

1. **Use Multiple Proxy Sources**: Provide multiple proxy list URLs for better availability
2. **Set Appropriate Timeouts**: Balance between speed and reliability
3. **Monitor Health**: Regularly check proxy pool statistics
4. **Handle Cancellation**: Always use CancellationTokens for long-running operations
5. **Dispose Properly**: Use `using` statements or call `Dispose()` explicitly
6. **Start with Conservative Settings**: Begin with lower `maxParallelTests` values
7. **Enable Fallback Carefully**: Only enable `allowDirectFallback` if direct connections are acceptable

## Troubleshooting

### No Working Proxies Found

- Verify proxy list URLs are accessible
- Increase `maxProxyTestTimeSeconds` to allow more testing time
- Try different proxy list sources
- Check network connectivity

### Slow Initialization

- Reduce `maxParallelTests` to avoid overwhelming the system
- Use faster proxy list sources
- Decrease `testTimeoutSeconds` for faster testing

### Frequent Failures

- Increase `maxRetries` for more resilience
- Use `allowDirectFallback` as a safety net
- Monitor proxy health with `GetStatistics()`
- Switch to more reliable proxy sources

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE.txt](LICENSE.txt) file for details.

## Acknowledgments

- Thanks to the various proxy list maintainers for providing free proxy sources
- Built with .NET 10.0

## Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/ProxyPool/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/ProxyPool/discussions)

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for a list of changes in each version.
