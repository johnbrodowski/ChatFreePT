using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProxyPool;

namespace ProxyPool.Examples
{
    /// <summary>
    /// Advanced usage examples showing custom configuration and monitoring
    /// </summary>
    public class AdvancedUsageExample
    {
        public static async Task Main(string[] args)
        {
            await MonitorProxyHealthExample();
            await MultipleRequestsExample();
            await CustomUserAgentExample();
        }

        /// <summary>
        /// Example showing how to monitor proxy health over time
        /// </summary>
        public static async Task MonitorProxyHealthExample()
        {
            Console.WriteLine("=== Proxy Health Monitoring Example ===\n");

            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
            };

            using var proxyClient = new ProxyEnabledHttpClient(
                proxyListUrls: proxyListUrls,
                testTimeoutSeconds: 10,
                fetchTimeoutSeconds: 30,
                maxParallelTests: 50,
                maxRetries: 3
            );

            // Make multiple requests and monitor health
            var urls = new[]
            {
                "https://example.com",
                "https://httpbin.org/ip",
                "https://ifconfig.me"
            };

            foreach (var url in urls)
            {
                Console.WriteLine($"\nFetching: {url}");
                var sw = Stopwatch.StartNew();

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    string html = await proxyClient.FetchHtmlAsync(url, cts.Token);

                    sw.Stop();
                    if (!string.IsNullOrEmpty(html))
                    {
                        Console.WriteLine($"✓ Success in {sw.ElapsedMilliseconds}ms ({html.Length} bytes)");
                    }
                    else
                    {
                        Console.WriteLine($"✗ Failed after {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Console.WriteLine($"✗ Error after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                }

                // Display current health statistics
                var stats = proxyClient.GetStatistics();
                Console.WriteLine($"  Pool Status: {stats.HealthyProxies}/{stats.TotalProxies} healthy proxies");
            }

            // Final statistics
            Console.WriteLine("\n=== Final Proxy Pool Statistics ===");
            var finalStats = proxyClient.GetStatistics();
            Console.WriteLine($"Total Proxies Discovered: {finalStats.TotalProxies}");
            Console.WriteLine($"Healthy Proxies: {finalStats.HealthyProxies}");

            if (finalStats.TopProxies.Any())
            {
                Console.WriteLine("\nTop 3 Most Reliable Proxies:");
                foreach (var proxy in finalStats.TopProxies.Take(3))
                {
                    var totalRequests = proxy.SuccessCount + proxy.FailureCount;
                    var successRate = totalRequests > 0
                        ? (double)proxy.SuccessCount / totalRequests * 100
                        : 0;

                    Console.WriteLine($"\n  {proxy.Address} ({proxy.Type})");
                    Console.WriteLine($"    Success Rate: {successRate:F1}% ({proxy.SuccessCount}/{totalRequests})");
                    Console.WriteLine($"    Reliability Score: {proxy.ReliabilityScore:F2}");
                    Console.WriteLine($"    Avg Response Time: {proxy.AverageResponseTime.TotalMilliseconds:F0}ms");
                }
            }
        }

        /// <summary>
        /// Example showing multiple parallel requests
        /// </summary>
        public static async Task MultipleRequestsExample()
        {
            Console.WriteLine("\n\n=== Parallel Requests Example ===\n");

            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
            };

            using var proxyClient = new ProxyEnabledHttpClient(
                proxyListUrls: proxyListUrls,
                testTimeoutSeconds: 10,
                fetchTimeoutSeconds: 30,
                maxParallelTests: 50
            );

            // Make 5 parallel requests
            var urls = Enumerable.Repeat("https://example.com", 5).ToArray();
            var tasks = urls.Select(async (url, index) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    string html = await proxyClient.FetchHtmlAsync(url, cts.Token);
                    sw.Stop();

                    return new
                    {
                        Index = index + 1,
                        Success = !string.IsNullOrEmpty(html),
                        Duration = sw.ElapsedMilliseconds,
                        Size = html?.Length ?? 0
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new
                    {
                        Index = index + 1,
                        Success = false,
                        Duration = sw.ElapsedMilliseconds,
                        Size = 0
                    };
                }
            });

            var results = await Task.WhenAll(tasks);

            Console.WriteLine("Parallel Request Results:");
            foreach (var result in results)
            {
                var status = result.Success ? "✓" : "✗";
                Console.WriteLine($"  {status} Request #{result.Index}: {result.Duration}ms ({result.Size} bytes)");
            }

            var successCount = results.Count(r => r.Success);
            var avgDuration = results.Where(r => r.Success).Average(r => r.Duration);
            Console.WriteLine($"\nSuccess Rate: {successCount}/{results.Length}");
            Console.WriteLine($"Average Duration: {avgDuration:F0}ms");
        }

        /// <summary>
        /// Example showing custom user agent configuration
        /// </summary>
        public static async Task CustomUserAgentExample()
        {
            Console.WriteLine("\n\n=== Custom User Agent Example ===\n");

            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
            };

            // Use a custom user agent string
            using var proxyClient = new ProxyEnabledHttpClient(
                proxyListUrls: proxyListUrls,
                testTimeoutSeconds: 10,
                fetchTimeoutSeconds: 30,
                maxParallelTests: 50,
                maxRetries: 3,
                allowDirectFallback: false,
                userAgent: "MyCustomBot/1.0 (Advanced ProxyPool Example)"
            );

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                // httpbin.org/user-agent returns the user agent it received
                string response = await proxyClient.FetchHtmlAsync(
                    "https://httpbin.org/user-agent",
                    cts.Token
                );

                if (!string.IsNullOrEmpty(response))
                {
                    Console.WriteLine("✓ Request successful with custom user agent");
                    Console.WriteLine($"Response: {response}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
            }
        }
    }
}
