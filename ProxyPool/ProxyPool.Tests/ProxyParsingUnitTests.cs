using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ProxyPool;
using Xunit;

namespace ProxyPool.Tests
{
    public class ProxyParsingUnitTests
    {
        private static ProxyEnabledHttpClient CreateClient()
        {
            return new ProxyEnabledHttpClient(
                proxyListUrls: new List<string> { "https://example.com/proxies.txt" },
                testTimeoutSeconds: 1,
                fetchTimeoutSeconds: 5,
                maxParallelTests: 1);
        }

        private static ProxyEnabledHttpClient.ProxyInfo ParseProxy(ProxyEnabledHttpClient client, string proxy)
        {
            MethodInfo method = typeof(ProxyEnabledHttpClient)
                .GetMethod("ParseProxy", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);

            var parsed = method.Invoke(client, new object[] { proxy });
            return Assert.IsType<ProxyEnabledHttpClient.ProxyInfo>(parsed);
        }

        [Fact]
        public void ParseProxy_ShouldHandleSchemelessHttpProxy()
        {
            using var client = CreateClient();

            var parsed = ParseProxy(client, "1.2.3.4:8080");

            Assert.Equal("1.2.3.4", parsed.Host);
            Assert.Equal(8080, parsed.Port);
            Assert.Equal(ProxyEnabledHttpClient.ProxyType.Http, parsed.Type);
        }

        [Fact]
        public void ParseProxy_ShouldHandleSchemeAndCredentials()
        {
            using var client = CreateClient();

            var parsed = ParseProxy(client, "socks5://user:pass@1.2.3.4:1080");

            Assert.Equal("1.2.3.4", parsed.Host);
            Assert.Equal(1080, parsed.Port);
            Assert.Equal(ProxyEnabledHttpClient.ProxyType.Socks5, parsed.Type);
            Assert.Equal("user", parsed.Username);
            Assert.Equal("pass", parsed.Password);
        }

        [Fact]
        public void ParseProxy_ShouldDefaultHttpsPortTo443()
        {
            using var client = CreateClient();

            var parsed = ParseProxy(client, "https://1.2.3.4");

            Assert.Equal(443, parsed.Port);
            Assert.Equal(ProxyEnabledHttpClient.ProxyType.Https, parsed.Type);
        }

        [Fact]
        public async Task ProxyInfo_RecordMethods_ShouldRemainBoundedUnderConcurrency()
        {
            var proxy = new ProxyEnabledHttpClient.ProxyInfo();

            var tasks = new[]
            {
                Task.Run(() =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        proxy.RecordSuccess(TimeSpan.FromMilliseconds(100 + (i % 10)));
                    }
                }),
                Task.Run(() =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        proxy.RecordFailure();
                    }
                })
            };

            await Task.WhenAll(tasks);

            Assert.InRange(proxy.ReliabilityScore, 0.0, 1.0);
            Assert.True(proxy.SuccessCount >= 2000);
            Assert.True(proxy.FailureCount >= 0);
            Assert.True(proxy.AverageResponseTime > TimeSpan.Zero);
        }

        [Theory]
        [InlineData("[::1]:8080", "::1", 8080)]
        [InlineData("http://[::1]:8080", "::1", 8080)]
        [InlineData("https://[2001:db8::1]", "2001:db8::1", 443)]
        public void ParseProxy_ShouldHandleIPv6Variants(string input, string expectedHost, int expectedPort)
        {
            using var client = CreateClient();

            var parsed = ParseProxy(client, input);

            Assert.Equal(expectedHost, parsed.Host);
            Assert.Equal(expectedPort, parsed.Port);
        }

        [Fact]
        public void ParseProxy_ShouldDecodePercentEncodedUserInfo()
        {
            using var client = CreateClient();

            var parsed = ParseProxy(client, "http://user%40name:pa%3Ass@1.2.3.4:8080");

            Assert.Equal("user@name", parsed.Username);
            Assert.Equal("pa:ss", parsed.Password);
        }

        [Fact]
        public void ParseProxy_ShouldFallbackForUnsupportedScheme()
        {
            using var client = CreateClient();

            var parsed = ParseProxy(client, "ftp://1.2.3.4:2121");

            Assert.Equal("1.2.3.4", parsed.Host);
            Assert.Equal(2121, parsed.Port);
            Assert.Equal(ProxyEnabledHttpClient.ProxyType.Http, parsed.Type);
            Assert.Equal("http://1.2.3.4:2121", parsed.Address);
        }

        [Fact]
        public void ParseProxy_ShouldNotThrowOnMalformedAuthority()
        {
            using var client = CreateClient();

            var parsed = ParseProxy(client, "http://:@:");

            Assert.NotNull(parsed);
            Assert.Equal("http://:@:", parsed.Address);
        }

        [Fact]
        public async Task ProxyInfo_GetSnapshot_ShouldRemainConsistentUnderConcurrency()
        {
            var proxy = new ProxyEnabledHttpClient.ProxyInfo();
            int violations = 0;

            var writer = Task.Run(() =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    proxy.RecordSuccess(TimeSpan.FromMilliseconds(80 + (i % 7)));
                    proxy.RecordFailure();
                }
            });

            var reader = Task.Run(() =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    var snapshot = proxy.GetSnapshot();
                    bool expectedHealthy = snapshot.FailureCount < 3 && snapshot.ReliabilityScore > 0.3;
                    if (snapshot.IsHealthy != expectedHealthy)
                    {
                        Interlocked.Increment(ref violations);
                    }
                }
            });

            await Task.WhenAll(writer, reader);

            Assert.Equal(0, violations);
        }

        [Fact]
        public async Task Dispose_ShouldCancelBackgroundHealthCheckTask()
        {
            var client = CreateClient();
            var performHealthCheck = typeof(ProxyEnabledHttpClient)
                .GetMethod("PerformHealthCheck", BindingFlags.Instance | BindingFlags.NonPublic);
            var healthCheckTaskField = typeof(ProxyEnabledHttpClient)
                .GetField("_healthCheckTask", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(performHealthCheck);
            Assert.NotNull(healthCheckTaskField);

            performHealthCheck.Invoke(client, new object[] { null });
            await Task.Delay(50);

            client.Dispose();

            var backgroundTask = healthCheckTaskField.GetValue(client) as Task;
            if (backgroundTask != null)
            {
                Assert.True(backgroundTask.IsCompleted);
            }
        }
    }
}
