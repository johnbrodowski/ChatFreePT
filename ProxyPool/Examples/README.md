# ProxyPool Examples

This directory contains example code demonstrating various usage patterns for the ProxyPool library.

## Examples

### BasicUsage.cs
Demonstrates the fundamental usage of `ProxyEnabledHttpClient`:
- Creating a client with basic configuration
- Fetching content through proxies
- Displaying proxy pool statistics
- Error handling and timeouts

**Run this first** to understand the core concepts.

### ProxyManagerExample.cs
Shows the simplified `ProxyManager` API:
- Easier configuration with sensible defaults
- Automatic proxy pool initialization
- Simplified content fetching
- When to use ProxyManager vs ProxyEnabledHttpClient

**Use this** if you want a simpler API for common use cases.

### AdvancedUsage.cs
Covers advanced scenarios:
- **Monitoring proxy health** over multiple requests
- **Parallel requests** for improved performance
- **Custom user agents** for specific requirements
- Performance tracking and reliability metrics

**Explore this** after mastering the basics.

## Running the Examples

These examples are provided as reference code. To use them in your project:

1. **Create a console application**:
   ```bash
   dotnet new console -n ProxyPoolExamples
   cd ProxyPoolExamples
   ```

2. **Add ProxyPool reference**:
   ```bash
   dotnet add reference ../ProxyPool/ProxyPool.csproj
   ```

3. **Copy an example** to your Program.cs:
   ```bash
   cp ../Examples/BasicUsage.cs Program.cs
   ```

4. **Run the example**:
   ```bash
   dotnet run
   ```

## Example Output

### Basic Usage Example

```
Fetching content through proxies...
✓ Successfully fetched 1256 characters
Content preview: <!doctype html>
<html>
<head>
    <title>Example Domain</title>
...

Proxy Pool Statistics:
  Total Proxies: 45
  Healthy Proxies: 23
  Initialization Status: True

Top 5 Performing Proxies:
  • 192.168.1.100:1080
    Type: Socks5
    Reliability: 95%
    Success/Failure: 19/1
    Avg Response Time: 245ms
...
```

## Common Configuration Patterns

### Fast Testing (Development)
```csharp
new ProxyEnabledHttpClient(
    proxyListUrls: urls,
    testTimeoutSeconds: 5,      // Quick tests
    fetchTimeoutSeconds: 15,    // Quick fetches
    maxParallelTests: 100,      // High parallelism
    maxRetries: 2               // Fewer retries
);
```

### Reliable Production
```csharp
new ProxyEnabledHttpClient(
    proxyListUrls: urls,
    testTimeoutSeconds: 15,     // Thorough testing
    fetchTimeoutSeconds: 45,    // Allow slower proxies
    maxParallelTests: 30,       // Moderate parallelism
    maxRetries: 5,              // More retries
    allowDirectFallback: true   // Safety net
);
```

### High Volume Scraping
```csharp
new ProxyEnabledHttpClient(
    proxyListUrls: urls,
    testTimeoutSeconds: 10,
    fetchTimeoutSeconds: 30,
    maxParallelTests: 50,
    maxRetries: 3
);
```

## Tips

1. **Start Simple**: Begin with BasicUsage.cs to understand the fundamentals
2. **Monitor Health**: Use `GetStatistics()` to track proxy pool health
3. **Use Timeouts**: Always use CancellationTokens to prevent hanging
4. **Test Locally**: Start with small numbers of proxies during development
5. **Scale Gradually**: Increase parallelism and proxy counts as needed

## Troubleshooting

### No Proxies Found
- Check that proxy list URLs are accessible
- Try different proxy sources
- Increase `testTimeoutSeconds`

### Slow Performance
- Reduce `maxParallelTests` to avoid overwhelming your system
- Use faster proxy sources
- Decrease `testTimeoutSeconds` for quicker filtering

### High Failure Rate
- Increase `maxRetries`
- Use more reliable proxy sources
- Enable `allowDirectFallback` as a safety net
- Monitor with `GetStatistics()` to identify issues

## Additional Resources

- [Main README](../README.md) - Complete documentation
- [Contributing Guide](../CONTRIBUTING.md) - How to contribute
- [API Reference](../README.md#api-reference) - Detailed API docs
