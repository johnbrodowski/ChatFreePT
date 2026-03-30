using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ProxyPool;

namespace ProxyPool.Tests
{
    /// <summary>
    /// Integration tests for ProxyEnabledHttpClient
    /// Tests proxy discovery, testing, and content fetching
    /// </summary>
    public class ProxyEnabledHttpClientTests
    {
        private readonly ITestOutputHelper _output;

        public ProxyEnabledHttpClientTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ProxyClient_ShouldDiscoverProxies()
        {
            // Arrange
            _output.WriteLine("TEST: Proxy Discovery");
            _output.WriteLine("=====================");

            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
            };

            _output.WriteLine($"Using proxy source: {proxyListUrls[0]}");

            using var proxyClient = new ProxyEnabledHttpClient(
                proxyListUrls: proxyListUrls,
                testTimeoutSeconds: 10,
                fetchTimeoutSeconds: 30,
                maxParallelTests: 50,
                maxRetries: 2,
                allowDirectFallback: false
            );

            // Act
            _output.WriteLine("Attempting to fetch content through proxies...");
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            string html = await proxyClient.FetchHtmlAsync("https://example.com", cts.Token);
            sw.Stop();

            var stats = proxyClient.GetStatistics();

            // Assert
            _output.WriteLine($"\nExecution time: {sw.Elapsed.TotalSeconds:F1}s");
            _output.WriteLine($"Total proxies discovered: {stats.TotalProxies}");
            _output.WriteLine($"Healthy proxies found: {stats.HealthyProxies}");

            Assert.True(stats.TotalProxies > 0, "Should discover at least some proxies from the source");

            // Note: We don't assert healthy proxies > 0 because free proxy lists
            // may have all proxies offline at any given time. That's expected behavior.
            if (stats.HealthyProxies > 0)
            {
                _output.WriteLine($"✓ Found {stats.HealthyProxies} working proxies!");

                // If we found working proxies, content fetch should have succeeded
                Assert.False(string.IsNullOrEmpty(html), "Should fetch content when healthy proxies are available");
                _output.WriteLine($"✓ Successfully fetched {html.Length} characters");
            }
            else
            {
                _output.WriteLine("⚠ No healthy proxies found (this is normal with free proxy lists)");
            }
        }

        [Fact]
        public async Task ProxyClient_ShouldTrackStatistics()
        {
            // Arrange
            _output.WriteLine("TEST: Statistics Tracking");
            _output.WriteLine("========================");

            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
            };

            using var proxyClient = new ProxyEnabledHttpClient(
                proxyListUrls: proxyListUrls,
                testTimeoutSeconds: 10,
                fetchTimeoutSeconds: 30,
                maxParallelTests: 50,
                maxRetries: 2
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await proxyClient.FetchHtmlAsync("https://example.com", cts.Token);

            var stats = proxyClient.GetStatistics();

            // Assert
            Assert.NotNull(stats);
            _output.WriteLine($"Total Proxies: {stats.TotalProxies}");
            _output.WriteLine($"Healthy Proxies: {stats.HealthyProxies}");
            _output.WriteLine($"Initialized: {stats.IsInitialized}");

            if (stats.HealthyProxies > 0)
            {
                Assert.NotNull(stats.TopProxies);
                Assert.NotEmpty(stats.TopProxies);

                _output.WriteLine($"\nTop {Math.Min(3, stats.TopProxies.Count)} Proxies:");
                foreach (var proxy in stats.TopProxies.Take(3))
                {
                    _output.WriteLine($"  - {proxy.Address}");
                    _output.WriteLine($"    Type: {proxy.Type}");
                    _output.WriteLine($"    Reliability: {proxy.ReliabilityScore:P0}");
                    _output.WriteLine($"    Avg Response: {proxy.AverageResponseTime.TotalMilliseconds:F0}ms");

                    Assert.True(proxy.ReliabilityScore >= 0 && proxy.ReliabilityScore <= 1.0,
                        "Reliability score should be between 0 and 1");
                }
            }
        }

        [Fact]
        public async Task ProxyClient_ShouldHandleMultipleRequests()
        {
            // Arrange
            _output.WriteLine("TEST: Multiple Requests");
            _output.WriteLine("======================");

            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
            };

            using var proxyClient = new ProxyEnabledHttpClient(
                proxyListUrls: proxyListUrls,
                testTimeoutSeconds: 10,
                fetchTimeoutSeconds: 30,
                maxParallelTests: 50,
                maxRetries: 2
            );

            // Act - Make 3 sequential requests
            _output.WriteLine("Making 3 sequential requests...");
            var results = new List<bool>();

            for (int i = 1; i <= 3; i++)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var sw = Stopwatch.StartNew();
                string html = await proxyClient.FetchHtmlAsync("https://example.com", cts.Token);
                sw.Stop();

                bool success = !string.IsNullOrEmpty(html);
                results.Add(success);

                _output.WriteLine($"Request {i}: {(success ? "✓ Success" : "✗ Failed")} ({sw.ElapsedMilliseconds}ms)");
            }

            var stats = proxyClient.GetStatistics();

            // Assert
            _output.WriteLine($"\nFinal statistics:");
            _output.WriteLine($"  Healthy proxies: {stats.HealthyProxies}");

            // If we have healthy proxies, at least some requests should succeed
            if (stats.HealthyProxies > 0)
            {
                var successCount = results.Count(r => r);
                _output.WriteLine($"  Successful requests: {successCount}/3");
                Assert.True(successCount > 0, "At least one request should succeed with healthy proxies");
            }
        }
    }

    /// <summary>
    /// Integration tests for ProxyManager
    /// Tests the simplified high-level API
    /// </summary>
    public class ProxyManagerTests
    {
        private readonly ITestOutputHelper _output;

        public ProxyManagerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ProxyManager_ShouldInitializeSuccessfully()
        {
            // Arrange
            _output.WriteLine("TEST: ProxyManager Initialization");
            _output.WriteLine("=================================");

            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
            };

            var proxyManager = new ProxyManager(
                proxyListUrls: proxyListUrls,
                minProxiesToFind: 5,
                maxProxyTestTimeSeconds: 60
            );

            // Act
            _output.WriteLine("Initializing proxy pool...");
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            bool initialized = await proxyManager.InitializeProxyPoolAsync(cts.Token);
            sw.Stop();

            var stats = proxyManager.GetStatistics();

            // Assert
            _output.WriteLine($"Initialization time: {sw.Elapsed.TotalSeconds:F1}s");
            _output.WriteLine($"Result: {(initialized ? "Success" : "Partial")}");
            _output.WriteLine($"Total proxies: {stats.TotalProxies}");
            _output.WriteLine($"Healthy proxies: {stats.HealthyProxies}");

            Assert.True(stats.TotalProxies > 0, "Should discover proxies during initialization");

            // Note: initialized may be false if not enough working proxies found,
            // but that's not a test failure - it's expected with free proxy lists
        }

        [Fact]
        public async Task ProxyManager_ShouldFetchContent()
        {
            // Arrange
            _output.WriteLine("TEST: ProxyManager Content Fetch");
            _output.WriteLine("================================");

            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
            };

            var proxyManager = new ProxyManager(
                proxyListUrls: proxyListUrls,
                minProxiesToFind: 5,
                maxProxyTestTimeSeconds: 60
            );

            // Act
            _output.WriteLine("Initializing...");
            using var initCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await proxyManager.InitializeProxyPoolAsync(initCts.Token);

            _output.WriteLine("Fetching content...");
            var sw = Stopwatch.StartNew();
            using var fetchCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            string html = await proxyManager.FetchHtmlAsync("https://example.com", fetchCts.Token);
            sw.Stop();

            var stats = proxyManager.GetStatistics();

            // Assert
            _output.WriteLine($"Fetch time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Healthy proxies: {stats.HealthyProxies}");

            if (stats.HealthyProxies > 0)
            {
                Assert.False(string.IsNullOrEmpty(html),
                    "Should fetch content when healthy proxies are available");
                _output.WriteLine($"✓ Fetched {html.Length} characters");
            }
            else
            {
                _output.WriteLine("⚠ No healthy proxies available (normal with free proxy lists)");
            }
        }
    }
}
