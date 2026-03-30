using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProxyPool;

namespace ProxyPool.Examples
{
    /// <summary>
    /// Basic usage example for ProxyEnabledHttpClient
    /// </summary>
    public class BasicUsageExample
    {
        public static async Task Main(string[] args)
        {
            // Define proxy list sources
            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt",
                "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies/socks5.txt"
            };

            // Create the proxy-enabled HTTP client
            using var proxyClient = new ProxyEnabledHttpClient(
                proxyListUrls: proxyListUrls,
                testTimeoutSeconds: 10,         // 10 seconds to test each proxy
                fetchTimeoutSeconds: 30,        // 30 seconds to fetch content
                maxParallelTests: 50,           // Test up to 50 proxies simultaneously
                maxRetries: 3,                  // Retry up to 3 times per proxy
                allowDirectFallback: false      // Don't use direct connection if proxies fail
            );

            try
            {
                // Create a cancellation token with 60-second timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                // Fetch content through proxies
                Console.WriteLine("Fetching content through proxies...");
                string html = await proxyClient.FetchHtmlAsync(
                    "https://example.com",
                    cts.Token
                );

                if (!string.IsNullOrEmpty(html))
                {
                    Console.WriteLine($"✓ Successfully fetched {html.Length} characters");
                    Console.WriteLine($"Content preview: {html.Substring(0, Math.Min(200, html.Length))}...");
                }
                else
                {
                    Console.WriteLine("✗ Failed to fetch content");
                }

                // Get proxy pool statistics
                var stats = proxyClient.GetStatistics();
                Console.WriteLine($"\nProxy Pool Statistics:");
                Console.WriteLine($"  Total Proxies: {stats.TotalProxies}");
                Console.WriteLine($"  Healthy Proxies: {stats.HealthyProxies}");
                Console.WriteLine($"  Initialization Status: {stats.IsInitialized}");

                // Display top performing proxies
                if (stats.TopProxies.Count > 0)
                {
                    Console.WriteLine($"\nTop {Math.Min(5, stats.TopProxies.Count)} Performing Proxies:");
                    foreach (var proxy in stats.TopProxies.Take(5))
                    {
                        Console.WriteLine($"  • {proxy.Address}");
                        Console.WriteLine($"    Type: {proxy.Type}");
                        Console.WriteLine($"    Reliability: {proxy.ReliabilityScore:P0}");
                        Console.WriteLine($"    Success/Failure: {proxy.SuccessCount}/{proxy.FailureCount}");
                        Console.WriteLine($"    Avg Response Time: {proxy.AverageResponseTime.TotalMilliseconds:F0}ms");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("✗ Operation timed out");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
