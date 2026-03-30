using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProxyPool;

namespace ProxyPool.Examples
{
    /// <summary>
    /// Example using the simplified ProxyManager API
    /// </summary>
    public class ProxyManagerExample
    {
        public static async Task Main(string[] args)
        {
            // Define proxy list sources
            var proxyListUrls = new List<string>
            {
                "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt"
            };

            Console.WriteLine("Initializing ProxyManager...");

            // Create proxy manager with simplified configuration
            var proxyManager = new ProxyManager(
                proxyListUrls: proxyListUrls,
                minProxiesToFind: 10,          // Find at least 10 working proxies
                maxProxyTestTimeSeconds: 60    // Spend up to 60 seconds testing
            );

            try
            {
                // Initialize the proxy pool
                Console.WriteLine("Finding working proxies...");
                await proxyManager.InitializeProxyPoolAsync();
                Console.WriteLine("✓ Proxy pool initialized successfully");

                // Fetch content using the simplified API
                Console.WriteLine("\nFetching content...");
                string html = await proxyManager.FetchHtmlAsync("https://example.com");

                if (!string.IsNullOrEmpty(html))
                {
                    Console.WriteLine($"✓ Successfully fetched {html.Length} bytes");
                }
                else
                {
                    Console.WriteLine("✗ Failed to fetch content");
                }

                // Get the underlying client for more control
                var client = await proxyManager.GetProxyClientAsync();
                var stats = client.GetStatistics();

                Console.WriteLine($"\nFound {stats.HealthyProxies} healthy proxies");
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
