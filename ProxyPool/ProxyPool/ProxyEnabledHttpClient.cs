using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Timer = System.Threading.Timer;

namespace ProxyPool
{
    /// <summary>
    /// A robust HTTP client that automatically manages and rotates through proxies
    /// </summary>
    public class ProxyEnabledHttpClient : IDisposable
    {
        #region Configuration Settings

        private IEnumerable<string> _proxyListUrls;
        private int _testTimeoutSeconds;
        private int _fetchTimeoutSeconds;
        private int _maxParallelTests;
        private int _maxRetries;
        private bool _allowDirectFallback;
        private string _userAgent;
        private int _healthCheckIntervalMinutes;
        private static readonly string[] _blockedHosts = { "localhost", "127.0.0.1", "::1", "0.0.0.0" };

        #endregion Configuration Settings

        #region Internal State

        private readonly ConcurrentDictionary<string, ProxyInfo> _proxyPool = new ConcurrentDictionary<string, ProxyInfo>();
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        private readonly HttpClient _directClient;
        private Timer _healthCheckTimer;
        private CancellationTokenSource _healthCheckCancellationTokenSource;
        private Task _healthCheckTask;
        private bool _isInitialized;
        private bool _isDisposed;
        private int _isDisposing;
        private int _isHealthCheckRunning;
        private readonly string[] _testUrls = { "https://html.duckduckgo.com/html/", "https://duckduckgo.com/" };

        #endregion Internal State

        #region Constructor

        /// <summary>
        /// Creates a new instance of ProxyEnabledHttpClient
        /// </summary>
        /// <param name="proxyListUrls">URLs to fetch proxy lists from</param>
        /// <param name="testTimeoutSeconds">Timeout for proxy testing in seconds</param>
        /// <param name="fetchTimeoutSeconds">Timeout for fetch operations in seconds</param>
        /// <param name="maxParallelTests">Maximum number of parallel proxy tests</param>
        /// <param name="maxRetries">Maximum number of retries per proxy</param>
        /// <param name="allowDirectFallback">Whether to allow direct connection if all proxies fail</param>
        /// <param name="userAgent">User agent string to use for requests</param>
        /// <param name="healthCheckIntervalMinutes">Interval for proxy health checks in minutes</param>
        public ProxyEnabledHttpClient(
            IEnumerable<string> proxyListUrls,
            int testTimeoutSeconds,
            int fetchTimeoutSeconds,
            int maxParallelTests,
            int maxRetries = 3,
            bool allowDirectFallback = false,
            string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
            int healthCheckIntervalMinutes = 30)
        {
            // Validate inputs
            _proxyListUrls = proxyListUrls ?? throw new ArgumentNullException(nameof(proxyListUrls));
            if (!_proxyListUrls.Any())
            {
                throw new ArgumentException("At least one proxy list URL must be provided.", nameof(proxyListUrls));
            }


            // Store configuration
            _testTimeoutSeconds = Math.Max(1, testTimeoutSeconds);
            _fetchTimeoutSeconds = Math.Max(5, fetchTimeoutSeconds);
            _maxParallelTests = Math.Max(1, Math.Min(500, maxParallelTests)); // Limit between 1 and 500
            _maxRetries = Math.Max(1, maxRetries);
            _healthCheckIntervalMinutes = Math.Max(5, healthCheckIntervalMinutes);
            _allowDirectFallback = allowDirectFallback;
            _userAgent = string.IsNullOrWhiteSpace(userAgent)
                ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
                : userAgent;

            if (_proxyListUrls.Any(url => !TryValidateTargetUri(url, out _)))
            {
                throw new ArgumentException("All proxy list URLs must be valid absolute HTTP/HTTPS URLs.", nameof(proxyListUrls));
            }

            // Set up direct client with compression support
            _directClient = new HttpClient(new HttpClientHandler
            {
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

            // Set up periodic health check
            _healthCheckTimer = new Timer(
                PerformHealthCheck,
                null,
                TimeSpan.FromMinutes(_healthCheckIntervalMinutes),
                TimeSpan.FromMinutes(_healthCheckIntervalMinutes));
            _healthCheckCancellationTokenSource = new CancellationTokenSource();
        }

        #endregion Constructor

        #region Public Methods

        /// <summary>
        /// Fetch HTML content from a URL using available proxies
        /// </summary>
        /// <param name="url">The URL to fetch</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>HTML content as string, or empty string if failed</returns>
        public async Task<string> FetchHtmlAsync(string url = "https://duckduckgo.com/", CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            if (!TryValidateTargetUri(url, out var targetUri))
            {
                throw new ArgumentException("URL must be a valid absolute HTTP/HTTPS URL.", nameof(url));
            }

            if (IsBlockedHost(targetUri.Host))
            {
                throw new ArgumentException("Target URL host is blocked by security policy.", nameof(url));
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                // Try with existing known healthy proxies first
                var result = await TryWithHealthyProxiesAsync(url, cancellationToken);
                if (!string.IsNullOrEmpty(result))
                {
                    LogInfo($"Successfully fetched {url} with known proxy in {stopwatch.ElapsedMilliseconds}ms");
                    return result;
                }

                // If no working proxies or all failed, find and test new ones
                result = await TryWithNewProxiesAsync(url, cancellationToken);
                if (!string.IsNullOrEmpty(result))
                {
                    LogInfo($"Successfully fetched {url} with new proxy in {stopwatch.ElapsedMilliseconds}ms");
                    return result;
                }

                // Try direct connection as a last resort if allowed
                if (_allowDirectFallback)
                {
                    LogWarning($"All proxies failed for {url}, trying direct connection");
                    result = await FetchWithDirectConnectionAsync(url, cancellationToken);
                    if (!string.IsNullOrEmpty(result))
                    {
                        LogInfo($"Successfully fetched {url} via direct connection in {stopwatch.ElapsedMilliseconds}ms");
                        return result;
                    }
                }

                LogError($"Failed to fetch {url} after trying all available proxies" +
                         (_allowDirectFallback ? " and direct connection" : ""));
                return string.Empty;
            }
            catch (OperationCanceledException)
            {
                LogInfo($"Operation for {url} was canceled after {stopwatch.ElapsedMilliseconds}ms");
                return string.Empty;
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error fetching {url}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Get statistics about the current proxy pool
        /// </summary>
        /// <returns>ProxyPoolStatistics object with current stats</returns>
        public ProxyPoolStatistics GetStatistics()
        {
            var snapshots = _proxyPool.Values
                .Select(p => p.GetSnapshot())
                .ToList();

            var stats = new ProxyPoolStatistics
            {
                TotalProxies = snapshots.Count,
                HealthyProxies = snapshots.Count(p => p.IsHealthy),
                IsInitialized = _isInitialized,
                TopProxies = snapshots
                    .OrderByDescending(p => p.ReliabilityScore)
                    .ThenBy(p => p.AverageResponseTime)
                    .Take(10)
                    .Select(p => new ProxyStatistics
                    {
                        Address = p.Address,
                        SuccessCount = p.SuccessCount,
                        FailureCount = p.FailureCount,
                        ReliabilityScore = p.ReliabilityScore,
                        AverageResponseTime = p.AverageResponseTime,
                        Type = p.Type.ToString()
                    })
                    .ToList()
            };

            return stats;
        }

        /// <summary>
        /// Returns a WebProxy configured with the best available healthy proxy, or null if none are available.
        /// </summary>
        public WebProxy? GetBestProxyWebProxy()
        {
            return GetHealthyProxyWebProxies(1).FirstOrDefault();
        }

        /// <summary>
        /// Fetches all proxy lists and tests every proxy, adding working ones to the pool as they are found.
        /// Designed to run in the background — cancel via the token to stop early.
        /// Progress is reported via <paramref name="onProgress"/> with (tested, working) counts.
        /// </summary>
        public async Task DiscoverProxiesAsync(
            Action<int, int>? onProgress = null,
            int delayBetweenStartsMs = 0,
            CancellationToken cancellationToken = default)
        {
            var addresses = await FetchAllProxyListsAsync(cancellationToken);
            if (addresses.Count == 0) return;

            LogInfo($"Discovery: testing {addresses.Count} proxies ({_maxParallelTests} in parallel)...");

            var semaphore = new SemaphoreSlim(_maxParallelTests);
            var triedProxies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int tested = 0;
            int working = 0;
            var tasks = new List<Task>();

            foreach (var address in addresses)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var parsed = ParseProxy(address);
                if (triedProxies.Contains(parsed.Address)) continue;
                if (_proxyPool.TryGetValue(parsed.Address, out var existing) && !existing.GetSnapshot().IsHealthy) continue;
                triedProxies.Add(parsed.Address);

                try { await semaphore.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { break; }

                if (delayBetweenStartsMs > 0)
                {
                    try { await Task.Delay(delayBetweenStartsMs, cancellationToken); }
                    catch (OperationCanceledException) { semaphore.Release(); break; }
                }

                var captured = parsed;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        bool ok = false;
                        foreach (var testUrl in _testUrls)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            try
                            {
                                using var tCts = new CancellationTokenSource(TimeSpan.FromSeconds(_testTimeoutSeconds));
                                using var linked = CancellationTokenSource.CreateLinkedTokenSource(tCts.Token, cancellationToken);
                                if (await TestProxyAsync(captured, testUrl, linked.Token))
                                {
                                    ok = true;
                                    break;
                                }
                            }
                            catch { }
                        }

                        if (ok)
                        {
                            captured.RecordSuccess();
                            _proxyPool.AddOrUpdate(captured.Address, captured, (_, old) => { old.RecordSuccess(); return old; });
                            Interlocked.Increment(ref working);
                            LogInfo($"√ Found working proxy: {captured.Host}:{captured.Port} ({captured.Type})");
                        }
                        else
                        {
                            _proxyPool.AddOrUpdate(captured.Address, captured, (_, old) => { old.RecordFailure(); return old; });
                        }

                        int t = Interlocked.Increment(ref tested);
                        int w = working; // snapshot
                        if (t % 100 == 0) LogInfo($"Checked {t} proxies, found {w} working");
                        onProgress?.Invoke(t, w);
                    }
                    finally { semaphore.Release(); }
                }, cancellationToken));
            }

            try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { }

            LogInfo($"Discovery complete: {tested} tested, {working} working.");
            _isInitialized = true;
        }

        /// <summary>
        /// Returns an ordered list of healthy proxies as WebProxy objects, best first.
        /// </summary>
        /// <param name="max">Maximum number of proxies to return.</param>
        public List<WebProxy> GetHealthyProxyWebProxies(int max = 10)
        {
            return _proxyPool.Values
                .Select(p => p.GetSnapshot())
                .Where(s => s.IsHealthy)
                .OrderByDescending(s => s.ReliabilityScore)
                .ThenBy(s => s.AverageResponseTime)
                .Take(max)
                .Select(s =>
                {
                    var proxy = new WebProxy(s.Address);
                    if (!string.IsNullOrEmpty(s.Username))
                        proxy.Credentials = new NetworkCredential(s.Username, s.Password);
                    return proxy;
                })
                .ToList();
        }

        /// <summary>
        /// Reports a successful use of a proxy address so its reliability score is updated.
        /// </summary>
        public void ReportProxySuccess(string proxyAddress)
        {
            if (_proxyPool.TryGetValue(proxyAddress, out var proxy))
                proxy.RecordSuccess();
        }

        /// <summary>
        /// Reports a failed use of a proxy address so its reliability score is updated.
        /// </summary>
        public void ReportProxyFailure(string proxyAddress)
        {
            if (_proxyPool.TryGetValue(proxyAddress, out var proxy))
                proxy.RecordFailure();
        }


        #endregion Public Methods

        #region Private Helper Methods

        private async Task<string> TryWithHealthyProxiesAsync(string url, CancellationToken cancellationToken)
        {
            // Get healthy proxies sorted by reliability
            var healthyProxies = _proxyPool.Values
                .Select(p => new { Proxy = p, Snapshot = p.GetSnapshot() })
                .Where(x => x.Snapshot.IsHealthy)
                .OrderByDescending(x => x.Snapshot.ReliabilityScore)
                .ThenBy(x => x.Snapshot.AverageResponseTime)
                .Take(5)
                .Select(x => x.Proxy)
                .ToList();

            // Try each healthy proxy
            foreach (var proxy in healthyProxies)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return string.Empty;
                }

                string result = await TryFetchWithProxyAsync(url, proxy, cancellationToken);
                if (!string.IsNullOrEmpty(result))
                {
                    return result;
                }
            }

            return string.Empty;
        }

        private async Task<string> TryWithNewProxiesAsync(string url, CancellationToken cancellationToken)
        {
            bool lockTaken = false;
            try
            {
                // Use a timeout to avoid waiting forever
                lockTaken = await _initLock.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                if (!lockTaken) return string.Empty;

                List<string> proxyAddresses;

                if (!_isInitialized)
                {
                    // Fetch proxy lists if not initialized
                    proxyAddresses = await FetchAllProxyListsAsync(cancellationToken);
                    LogInfo($"Found {proxyAddresses.Count} potential proxies to check");
                    _isInitialized = true;
                }
                else
                {
                    // Use untested proxies or those that haven't failed too many times
                    proxyAddresses = _proxyPool.Values
                        .Select(p => p.GetSnapshot())
                        .Where(s => !s.IsHealthy && s.FailureCount < 3)
                        .Select(s => s.Address)
                        .ToList();


                    proxyAddresses.Reverse();

                    // If we don't have many untested proxies, fetch more
                    if (proxyAddresses.Count < 100)
                    {
                        var newProxies = await FetchAllProxyListsAsync(cancellationToken);
                        proxyAddresses.AddRange(newProxies.Where(p => !_proxyPool.ContainsKey(p)));
                        proxyAddresses = proxyAddresses.Distinct().ToList();
                    }
                }

                // Release lock before potentially long operation
                _initLock.Release();
                lockTaken = false;

                // Find the best working proxy
                var workingProxy = await FindBestWorkingProxyAsync(
                    proxyAddresses,
                    cancellationToken,
                    minProxiesToCheck: 50,  // Check at least 50 proxies
                    maxTimeSeconds: 240       // Spend at most 240 seconds checking
                );

                if (workingProxy != null)
                {
                    // Try to fetch with the working proxy
                    string result = await FetchWithProxyAsync(url, workingProxy, cancellationToken);
                    if (!string.IsNullOrEmpty(result))
                    {
                        return result;
                    }
                }

                return string.Empty;
            }
            finally
            {
                // Make sure we release the lock if we still hold it
                if (lockTaken)
                {
                    _initLock.Release();
                }
            }
        }

        private async Task<ProxyInfo> FindBestWorkingProxyAsync(
            List<string> proxyAddresses,
            CancellationToken cancellationToken,
            int minProxiesToCheck,
            int maxTimeSeconds)
        {
            LogInfo($"Testing proxies (up to {_maxParallelTests} in parallel)...");

            // Create a thread-safe collection to store working proxies
            var workingProxies = new ConcurrentBag<ProxyInfo>();

            // Create a semaphore to limit parallel tests (don't use 'using' - we'll dispose it manually after all tasks complete)
            var semaphore = new SemaphoreSlim(_maxParallelTests);

            // Create a set to track which proxies we've already tried
            var triedProxies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Use a stopwatch to limit total time spent checking proxies
            var stopwatch = Stopwatch.StartNew();

            // Track how many proxies we've started checking
            int proxiesStartedChecking = 0;

            // Create a completion signal for when we have enough working proxies
            var earlyCompletionSource = new TaskCompletionSource<bool>();

            // Create a timeout task
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(maxTimeSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            // Create a collection to track all running tasks
            var tasks = new List<Task>();

            // Shuffle the proxy addresses to avoid testing them in the same order every time

            foreach (string address in proxyAddresses)
            {
                ProxyInfo parsedProxy = ParseProxy(address);
                string proxyKey = parsedProxy.Address;

                // Stop if operation was canceled or we've timed out
                if (linkedCts.Token.IsCancellationRequested || stopwatch.Elapsed.TotalSeconds > maxTimeSeconds)
                {
                    break;
                }

                // Stop if we've checked the minimum number of proxies AND have at least 5 working proxies
                if (proxiesStartedChecking >= minProxiesToCheck && workingProxies.Count >= 10)

                {
                    LogInfo($"Found {workingProxies.Count} working proxies after checking {proxiesStartedChecking} proxies, stopping search");
                    break;
                }

                // Skip if we already have this proxy in our pool and it's marked as unhealthy
                if (_proxyPool.TryGetValue(proxyKey, out var existingProxy) && !existingProxy.GetSnapshot().IsHealthy)
                {
                    continue;
                }

                // Skip if we've already tried this proxy in this session
                if (triedProxies.Contains(proxyKey))
                {
                    continue;
                }

                proxiesStartedChecking++;
                triedProxies.Add(proxyKey);

                // Wait for a slot in the semaphore
                try
                {
                    await semaphore.WaitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Start a task to test this proxy
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Skip if operation canceled
                        if (linkedCts.Token.IsCancellationRequested)
                        {
                            return;
                        }

                        ProxyInfo proxyInfo = parsedProxy;
                        bool isWorking = false;

                        // Try with multiple test URLs
                        foreach (var testUrl in _testUrls)
                        {
                            try
                            {
                                // Skip if operation canceled
                                if (linkedCts.Token.IsCancellationRequested)
                                {
                                    break;
                                }

                                using var testTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_testTimeoutSeconds));
                                using var testLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                                    testTimeoutCts.Token, linkedCts.Token);

                                if (await TestProxyAsync(proxyInfo, testUrl, testLinkedCts.Token))
                                {
                                    isWorking = true;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                // Log all exceptions and continue to next URL
                                LogDebug($"Error testing proxy {address} with {testUrl}: {ex.Message}");
                                continue;
                            }
                        }

                        if (isWorking)
                        {
                            proxyInfo.RecordSuccess();
                            _proxyPool.AddOrUpdate(proxyInfo.Address, proxyInfo, (key, old) =>
                            {
                                old.RecordSuccess();
                                return old;
                            });
                            LogInfo($"✓ Found working proxy: {address} ({proxyInfo.Type})");

                            // Add to the collection of working proxies
                            workingProxies.Add(proxyInfo);

                            // Signal early completion if we have enough working proxies
                            if (workingProxies.Count >= 5 && proxiesStartedChecking >= minProxiesToCheck)
                            {
                                earlyCompletionSource.TrySetResult(true);
                            }
                        }
                        else
                        {
                            // Add to pool as unhealthy to avoid rechecking
                            proxyInfo.RecordFailure();
                            _proxyPool.AddOrUpdate(proxyInfo.Address, proxyInfo, (key, old) =>
                            {
                                old.RecordFailure();
                                return old;
                            });
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        // Catch ALL exceptions, log, and continue
                        LogDebug($"Error processing proxy {address}: {ex.Message}");
                    }
                    finally
                    {
                        // Always release the semaphore
                        semaphore.Release();
                    }
                }, linkedCts.Token));

                // Every 100 proxies, clean up completed tasks to avoid memory issues
                if (proxiesStartedChecking % 500 == 0)
                {
                    // Wait for already completed tasks
                    var completedTasks = tasks.Where(t => t.IsCompleted).ToArray();
                    if (completedTasks.Length > 0)
                    {
                        await Task.WhenAll(completedTasks);
                        tasks.RemoveAll(t => t.IsCompleted);
                    }

                    LogDebug($"Checked {proxiesStartedChecking} proxies so far, found {workingProxies.Count} working ones");
                }

                // Check if we've hit early completion or timeout
                if (earlyCompletionSource.Task.IsCompleted || timeoutCts.Token.IsCancellationRequested)
                {
                    break;
                }
            }

            // Wait for all running tasks or timeout
            try
            {
                await Task.WhenAny(
                    Task.WhenAll(tasks),
                    earlyCompletionSource.Task,
                    Task.Delay(TimeSpan.FromSeconds(5), linkedCts.Token)
                );
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation exceptions
            }

            // IMPORTANT: Wait for ALL tasks to complete before disposing the semaphore
            // This prevents ObjectDisposedException when tasks try to release the semaphore
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Ignore all exceptions from tasks - we've already handled them
            }

            // Now it's safe to dispose the semaphore since all tasks have completed
            semaphore.Dispose();

            LogInfo($"Checked {proxiesStartedChecking} proxies and found {workingProxies.Count} working ones");

            // Return the best proxy based on reliability and response time
            return workingProxies
                .OrderByDescending(p => p.ReliabilityScore)
                .ThenBy(p => p.AverageResponseTime)
                .FirstOrDefault();
        }

        private async Task<bool> TestProxyAsync(ProxyInfo proxy, string testUrl, CancellationToken cancellationToken)
        {
            try
            {
                using var client = CreateHttpClient(proxy);
                var response = await client.GetAsync(testUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                // Just return false on any exception
                return false;
            }
        }

        private async Task<string> FetchWithProxyAsync(string url, ProxyInfo proxy, CancellationToken cancellationToken)
        {
            try
            {
                using var client = CreateHttpClient(proxy);

                // Use the configurable fetch timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_fetchTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                Stopwatch sw = Stopwatch.StartNew();
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, linkedCts.Token);
                sw.Stop();

                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync(linkedCts.Token);
                    proxy.RecordSuccess(sw.Elapsed);
                    return content;
                }

                proxy.RecordFailure();
                LogDebug($"Failed to fetch {url} with proxy {proxy.Address}. Status: {response.StatusCode}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                proxy.RecordFailure();
                LogDebug($"Error fetching {url} with proxy {proxy.Address}: {ex.Message}");
                return string.Empty;
            }
        }

        private async Task<string> TryFetchWithProxyAsync(string url, ProxyInfo proxy, CancellationToken cancellationToken)
        {
            // Use the configurable number of retries
            for (int attempt = 0; attempt < _maxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return string.Empty;
                }

                try
                {
                    string result = await FetchWithProxyAsync(url, proxy, cancellationToken);
                    if (!string.IsNullOrEmpty(result))
                    {
                        return result;
                    }

                    LogDebug($"Attempt {attempt + 1} with proxy {proxy.Address} failed for {url}");
                }
                catch (Exception ex)
                {
                    LogDebug($"Error during attempt {attempt + 1} with proxy {proxy.Address} for {url}: {ex.Message}");
                }

                // Exponential backoff before retry
                if (attempt < _maxRetries - 1)
                {
                    try
                    {
                        int delay = (int)(200 * Math.Pow(2, attempt));
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Operation was canceled during delay, exit retry loop
                        break;
                    }
                }
            }

            // All retries failed, mark proxy as failed
            proxy.RecordFailure();
            return string.Empty;
        }


        private ProxyInfo ParseProxy(string proxyAddress)
        {
            string normalizedAddress = proxyAddress?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedAddress))
            {
                return new ProxyInfo { Address = proxyAddress };
            }

            try
            {
                string candidate = normalizedAddress;
                if (!candidate.Contains("://", StringComparison.Ordinal))
                {
                    candidate = $"http://{candidate}";
                }

                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                {
                    string scheme = uri.Scheme.ToLowerInvariant();
                    ProxyType proxyType = scheme switch
                    {
                        "https" => ProxyType.Https,
                        "socks4" => ProxyType.Socks4,
                        "socks5" => ProxyType.Socks5,
                        _ => ProxyType.Http
                    };

                    string username = null;
                    string password = null;
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var credentials = uri.UserInfo.Split(':', 2);
                        username = Uri.UnescapeDataString(credentials[0]);
                        if (credentials.Length > 1)
                        {
                            password = Uri.UnescapeDataString(credentials[1]);
                        }
                    }

                    return new ProxyInfo
                    {
                        Address = BuildCanonicalProxyAddress(uri, proxyType),
                        Host = uri.Host,
                        Port = uri.IsDefaultPort
                            ? (proxyType == ProxyType.Https ? 443 : 80)
                            : uri.Port,
                        Type = proxyType,
                        Username = username,
                        Password = password
                    };
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error parsing proxy address {proxyAddress}: {ex.Message}");
            }

            if (Uri.CheckHostName(normalizedAddress) != UriHostNameType.Unknown)
            {
                return new ProxyInfo
                {
                    Address = $"http://{normalizedAddress.ToLowerInvariant()}:80",
                    Host = normalizedAddress,
                    Port = 80,
                    Type = ProxyType.Http
                };
            }

            return new ProxyInfo
            {
                Address = normalizedAddress,
                Host = normalizedAddress,
                Port = 80,
                Type = ProxyType.Http
            };
        }

        private static string BuildCanonicalProxyAddress(Uri uri, ProxyType proxyType)
        {
            string scheme = proxyType == ProxyType.Socks5 ? "socks5" :
                            proxyType == ProxyType.Socks4 ? "socks4" :
                            proxyType == ProxyType.Https ? "https" : "http";

            string defaultPort = proxyType == ProxyType.Https ? "443" : "80";
            string host = uri.IdnHost.Contains(':') ? $"[{uri.IdnHost.ToLowerInvariant()}]" : uri.IdnHost.ToLowerInvariant();
            string port = uri.IsDefaultPort ? defaultPort : uri.Port.ToString();
            string userInfo = string.IsNullOrEmpty(uri.UserInfo) ? string.Empty : $"{uri.UserInfo}@";

            return $"{scheme}://{userInfo}{host}:{port}";
        }

        private async Task<string> FetchWithDirectConnectionAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_fetchTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                var response = await _directClient.GetAsync(url, linkedCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(linkedCts.Token);
                }

                LogDebug($"Failed to fetch {url} with direct connection. Status: {response.StatusCode}");
                return string.Empty;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                LogDebug($"Error fetching {url} with direct connection: {ex.Message}");
                return string.Empty;
            }
        }

        private HttpClient CreateHttpClient(ProxyInfo proxy)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                UseCookies = true
            };

            if (proxy == null)
            {
                handler.UseProxy = false;
                var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(_fetchTimeoutSeconds)
                };
                AddDefaultHeaders(client);
                return client;
            }

            string scheme = proxy.Type == ProxyType.Socks5 ? "socks5" :
                            proxy.Type == ProxyType.Socks4 ? "socks4" :
                            proxy.Type == ProxyType.Https ? "https" : "http";
            var webProxy = new WebProxy($"{scheme}://{proxy.Host}:{proxy.Port}");

            if (!string.IsNullOrEmpty(proxy.Username) && !string.IsNullOrEmpty(proxy.Password))
            {
                webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
            }

            handler.Proxy = webProxy;
            handler.UseProxy = true;

            var proxyClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_fetchTimeoutSeconds)
            };

            AddDefaultHeaders(proxyClient);
            return proxyClient;
        }

        private void AddDefaultHeaders(HttpClient client)
        {
            client.DefaultRequestHeaders.Add("User-Agent", _userAgent);
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        }

        private void PerformHealthCheck(object state)
        {
            if (_isDisposed || Volatile.Read(ref _isDisposing) == 1)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _isHealthCheckRunning, 1, 0) != 0)
            {
                LogDebug("Skipping periodic proxy health check because a previous run is still active");
                return;
            }

            // We can't use async directly in the timer callback, so we'll start a task
            _healthCheckTask = Task.Run(async () =>
            {
                try
                {
                    LogDebug("Starting periodic proxy health check");

                    var healthCheckToken = _healthCheckCancellationTokenSource.Token;
                    int healthyBefore = _proxyPool.Values.Count(p => p.GetSnapshot().IsHealthy);

                    // Focus health checks on proxies that need verification
                    var proxiesToCheck = _proxyPool.Values
                        .Select(p => new { Proxy = p, Snapshot = p.GetSnapshot() })
                        .Where(x =>
                            // Proxies not checked recently
                            (DateTime.UtcNow - x.Snapshot.LastChecked) > TimeSpan.FromMinutes(30) ||
                            // Unhealthy proxies that might have recovered
                            (!x.Snapshot.IsHealthy && x.Snapshot.FailureCount < 5))
                        .Select(x => x.Proxy)
                        .ToList();

                    // Use a semaphore to limit concurrent health checks
                    using var semaphore = new SemaphoreSlim(_maxParallelTests);
                    var tasks = new List<Task>();

                    foreach (var proxy in proxiesToCheck)
                    {
                        await semaphore.WaitAsync(healthCheckToken);

                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                foreach (var testUrl in _testUrls)
                                {
                                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_testTimeoutSeconds));
                                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, healthCheckToken);
                                    bool isWorking = await TestProxyAsync(proxy, testUrl, linkedCts.Token);
                                    if (isWorking)
                                    {
                                        proxy.RecordSuccess();
                                        LogDebug($"Health check: Proxy {proxy.Address} is healthy");
                                        break;
                                    }
                                    else
                                    {
                                        proxy.RecordFailure();
                                        LogDebug($"Health check: Proxy {proxy.Address} is unhealthy");
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                LogDebug($"Health check canceled for {proxy.Address}");
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"Error during health check for {proxy.Address}: {ex.Message}");
                                proxy.RecordFailure();
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }, healthCheckToken));
                    }

                    // Wait for all health check tasks to complete with a timeout
                    try
                    {
                        await Task.WhenAny(
                            Task.WhenAll(tasks),
                            Task.Delay(TimeSpan.FromMinutes(5), healthCheckToken)
                        );
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error waiting for health check tasks: {ex.Message}");
                    }

                    // Clean up permanently failed proxies
                    RemoveFailedProxies();

                    int healthyAfter = _proxyPool.Values.Count(p => p.GetSnapshot().IsHealthy);
                    LogDebug($"Health check completed. Healthy proxies: {healthyBefore} -> {healthyAfter}");
                }
                catch (OperationCanceledException)
                {
                    LogDebug("Periodic proxy health check canceled");
                }
                catch (Exception ex)
                {
                    LogError($"Error in proxy health check: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _isHealthCheckRunning, 0);
                }
            });
        }

        private void RemoveFailedProxies()
        {
            // Remove proxies with excessive failures and low reliability
            var failedProxies = _proxyPool.Where(kv =>
                {
                    var snapshot = kv.Value.GetSnapshot();
                    return snapshot.FailureCount > 10 &&
                        snapshot.ReliabilityScore < 0.1 &&
                        (DateTime.UtcNow - snapshot.LastSuccess) > TimeSpan.FromHours(1);
                })
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in failedProxies)
            {
                _proxyPool.TryRemove(key, out _);
            }

            if (failedProxies.Count > 0)
            {
                LogInfo($"Removed {failedProxies.Count} permanently failed proxies from the pool");
            }
        }

        #endregion Private Helper Methods

        #region Logging Methods

        private void LogDebug(string message)
        {
            Debug.WriteLine($"[DEBUG] {DateTime.Now}: {message}");
        }

        private void LogInfo(string message)
        {
            Debug.WriteLine($"[INFO] {DateTime.Now}: {message}");
            Console.WriteLine($"[INFO] {message}");  // Also output to console
        }

        private void LogWarning(string message)
        {
            Debug.WriteLine($"[WARNING] {DateTime.Now}: {message}");
            Console.WriteLine($"[WARNING] {message}");  // Also output to console
        }

        private void LogError(string message)
        {
            Debug.WriteLine($"[ERROR] {DateTime.Now}: {message}");
            Console.WriteLine($"[ERROR] {message}");  // Also output to console
        }

        #endregion Logging Methods

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                Interlocked.Exchange(ref _isDisposing, 1);
                _healthCheckTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _healthCheckCancellationTokenSource?.Cancel();

                try
                {
                    _healthCheckTask?.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
                {
                    // Ignore expected cancellation
                }

                _initLock?.Dispose();
                _directClient?.Dispose();
                _healthCheckTimer?.Dispose();
                _healthCheckCancellationTokenSource?.Dispose();
            }

            _isDisposed = true;
        }

        #endregion IDisposable Implementation

        /// <summary>
        /// Fetches proxy lists from all configured sources
        /// </summary>
        private async Task<List<string>> FetchAllProxyListsAsync(CancellationToken cancellationToken)
        {
            var allProxies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var validSourceUrls = _proxyListUrls
                .Where(url => TryValidateTargetUri(url, out var uri) && !IsBlockedHost(uri.Host))
                .ToList();

            if (validSourceUrls.Count == 0)
            {
                LogWarning("No valid proxy list URLs are available after validation.");
                return allProxies.ToList();
            }

            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

            // Create tasks to fetch from all sources in parallel
            var fetchTasks = validSourceUrls.Select(url => Task.Run(async () =>
            {
                try
                {
                    string response = await client.GetStringAsync(url, cancellationToken);
                    var proxies = response
                        .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(p => p.Contains(':') && !string.IsNullOrWhiteSpace(p))
                        .Select(p => p.Trim())
                        .ToList();

                    Debug.WriteLine($"Fetched {proxies.Count} proxies from {url}");
                    return proxies;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error fetching from {url}: {ex.Message}");
                    return new List<string>();
                }
            }, cancellationToken)).ToList();

            try
            {
                // Wait for all tasks or until timeout
                await Task.WhenAll(fetchTasks);

                // Combine all results
                foreach (var task in fetchTasks)
                {
                    foreach (var proxy in task.Result)
                    {
                        allProxies.Add(proxy);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during proxy list fetching: {ex.Message}");
            }

            return allProxies.ToList();
        }

        private static bool TryValidateTargetUri(string url, out Uri uri)
        {
            uri = null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            {
                return false;
            }

            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        private static bool IsBlockedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return true;
            }

            if (_blockedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IPAddress.TryParse(host, out var ip))
            {
                return false;
            }

            if (IPAddress.IsLoopback(ip))
            {
                return true;
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                byte[] bytes = ip.GetAddressBytes();
                if (bytes[0] == 10)
                {
                    return true;
                }

                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }

                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && ip.IsIPv6LinkLocal)
            {
                return true;
            }

            return false;
        }

        #region Supporting Classes

        /// <summary>
        /// Represents information about a proxy server
        /// </summary>
        public class ProxyInfo
        {
            public string Address { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public ProxyType Type { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public double ReliabilityScore { get; set; } = 0.5;
            public int FailureCount { get; set; }
            public int SuccessCount { get; set; }
            public DateTime LastChecked { get; set; } = DateTime.UtcNow;
            public DateTime LastSuccess { get; set; } = DateTime.UtcNow;
            public TimeSpan AverageResponseTime { get; set; } = TimeSpan.FromSeconds(1);
            private readonly Queue<TimeSpan> _responseTimes = new Queue<TimeSpan>(10); // Track last 10 response times
            private readonly object _sync = new object();

            public bool IsHealthy
            {
                get
                {
                    lock (_sync)
                    {
                        return FailureCount < 3 && ReliabilityScore > 0.3;
                    }
                }
            }

            public ProxyInfoSnapshot GetSnapshot()
            {
                lock (_sync)
                {
                    return new ProxyInfoSnapshot
                    {
                        Address = Address,
                        Host = Host,
                        Port = Port,
                        Type = Type,
                        Username = Username,
                        Password = Password,
                        ReliabilityScore = ReliabilityScore,
                        FailureCount = FailureCount,
                        SuccessCount = SuccessCount,
                        LastChecked = LastChecked,
                        LastSuccess = LastSuccess,
                        AverageResponseTime = AverageResponseTime,
                        IsHealthy = FailureCount < 3 && ReliabilityScore > 0.3
                    };
                }
            }

            public void RecordSuccess(TimeSpan? responseTime = null)
            {
                lock (_sync)
                {
                    SuccessCount++;
                    FailureCount = Math.Max(0, FailureCount - 1); // Reduce failure count on success
                    ReliabilityScore = Math.Min(1.0, ReliabilityScore + 0.1);
                    LastChecked = DateTime.UtcNow;
                    LastSuccess = DateTime.UtcNow;

                    if (responseTime.HasValue)
                    {
                        // Update average response time
                        _responseTimes.Enqueue(responseTime.Value);
                        if (_responseTimes.Count > 10)
                        {
                            _responseTimes.Dequeue();
                        }

                        if (_responseTimes.Count > 0)
                        {
                            AverageResponseTime = TimeSpan.FromMilliseconds(
                                _responseTimes.Average(t => t.TotalMilliseconds));
                        }
                    }
                }
            }

            public void RecordFailure()
            {
                lock (_sync)
                {
                    FailureCount++;
                    ReliabilityScore = Math.Max(0.0, ReliabilityScore - 0.2);
                    LastChecked = DateTime.UtcNow;
                }
            }
        }

        public class ProxyInfoSnapshot
        {
            public string Address { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public ProxyType Type { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public double ReliabilityScore { get; set; }
            public int FailureCount { get; set; }
            public int SuccessCount { get; set; }
            public DateTime LastChecked { get; set; }
            public DateTime LastSuccess { get; set; }
            public TimeSpan AverageResponseTime { get; set; }
            public bool IsHealthy { get; set; }
        }

        /// <summary>
        /// Types of proxy servers
        /// </summary>
        public enum ProxyType
        {
            Http,
            Https,
            Socks4,
            Socks5
        }

        /// <summary>
        /// Statistics about the proxy pool
        /// </summary>
        public class ProxyPoolStatistics
        {
            public int TotalProxies { get; set; }
            public int HealthyProxies { get; set; }
            public bool IsInitialized { get; set; }
            public List<ProxyStatistics> TopProxies { get; set; } = new List<ProxyStatistics>();
        }

        /// <summary>
        /// Statistics about a specific proxy
        /// </summary>
        public class ProxyStatistics
        {
            public string Address { get; set; }
            public int SuccessCount { get; set; }
            public int FailureCount { get; set; }
            public double ReliabilityScore { get; set; }
            public TimeSpan AverageResponseTime { get; set; }
            public string Type { get; set; }
        }

        #endregion Supporting Classes
    }


    public class ProxyManager
    {
        private readonly ProxyEnabledHttpClient _proxyClient;
        private readonly List<string> _proxyListUrls;
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);
        private bool _isInitialized = false;
        private readonly int _minProxiesToFind;
        private readonly int _maxProxyTestTimeSeconds;

        public ProxyManager(
            List<string> proxyListUrls,
            int minProxiesToFind = 20,
            int maxProxyTestTimeSeconds = 120)
        {
            _proxyListUrls = proxyListUrls ?? throw new ArgumentNullException(nameof(proxyListUrls));
            _minProxiesToFind = Math.Max(5, minProxiesToFind);
            _maxProxyTestTimeSeconds = Math.Max(30, maxProxyTestTimeSeconds);

            // Create proxy client with configuration optimized for proxy discovery
            _proxyClient = new ProxyEnabledHttpClient(
                proxyListUrls: proxyListUrls,
                testTimeoutSeconds:7,          // Faster timeout for testing
                fetchTimeoutSeconds: 20,
                maxParallelTests: 50,          // Higher parallelism for faster testing
                maxRetries: 2,                  // Fewer retries during initialization
                allowDirectFallback: false
            );
        }

        /// <summary>
        /// Initializes the proxy pool by finding and validating proxies.
        /// This will be called automatically on first use, or can be called manually.
        /// </summary>
        public async Task<bool> InitializeProxyPoolAsync(CancellationToken cancellationToken = default)
        {
            // Use a lock to prevent multiple simultaneous initializations
            bool lockTaken = false;
            try
            {
                lockTaken = await _initializationLock.WaitAsync(TimeSpan.FromSeconds(40), cancellationToken);
                if (!lockTaken) return false;

                if (_isInitialized)
                {
                    Debug.WriteLine("Proxy pool is already initialized.");
                    return true;
                }

                Debug.WriteLine("Starting proxy pool initialization...");

                // Fetch proxy lists from all sources
                List<string> allProxies = await FetchAllProxyListsAsync(cancellationToken);
                Debug.WriteLine($"Fetched {allProxies.Count} proxy addresses from {_proxyListUrls.Count} sources");

                if (allProxies.Count == 0)
                {
                    Debug.WriteLine("Failed to fetch any proxies from the provided sources");
                    return false;
                }

                // Test proxies to find working ones
                await TestProxiesAsync(allProxies, cancellationToken);

                // Get statistics
                var stats = _proxyClient.GetStatistics();

                Debug.WriteLine($"Proxy pool initialization completed:");
                Debug.WriteLine($"- Total proxies tested: {allProxies.Count}");
                Debug.WriteLine($"- Working proxies found: {stats.HealthyProxies}");

                if (stats.HealthyProxies >= _minProxiesToFind)
                {
                    _isInitialized = true;
                    Debug.WriteLine("Proxy pool successfully initialized");

                    // Log the top working proxies
                    Debug.WriteLine("\nTop working proxies:");
                    foreach (var proxy in stats.TopProxies.Take(5))
                    {
                        Debug.WriteLine($"- {proxy.Address} (Reliability: {proxy.ReliabilityScore:F2}, Type: {proxy.Type})");
                    }

                    return true;
                }
                else
                {
                    Debug.WriteLine($"Warning: Only found {stats.HealthyProxies} working proxies, which is below the minimum threshold of {_minProxiesToFind}");
                    return stats.HealthyProxies > 0;
                }
            }
            finally
            {
                // Always release the lock
                if (lockTaken)
                {
                    _initializationLock.Release();
                }
            }
        }

        /// <summary>
        /// Fetches proxy lists from all configured sources
        /// </summary>
        private async Task<List<string>> FetchAllProxyListsAsync(CancellationToken cancellationToken)
        {
            var allProxies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

            // Create tasks to fetch from all sources in parallel
            var fetchTasks = _proxyListUrls.Select(url => Task.Run(async () =>
            {
                try
                {
                    string response = await client.GetStringAsync(url, cancellationToken);
                    var proxies = response
                        .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(p => p.Contains(':') && !string.IsNullOrWhiteSpace(p))
                        .Select(p => p.Trim())
                        .ToList();

                    Debug.WriteLine($"Fetched {proxies.Count} proxies from {url}");
                    return proxies;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error fetching from {url}: {ex.Message}");
                    return new List<string>();
                }
            }, cancellationToken)).ToList();

            try
            {
                // Wait for all tasks or until timeout
                await Task.WhenAll(fetchTasks);

                // Combine all results
                foreach (var task in fetchTasks)
                {
                    foreach (var proxy in task.Result)
                    {
                        allProxies.Add(proxy);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during proxy list fetching: {ex.Message}");
            }

            return allProxies.ToList();
        }

        /// <summary>
        /// Tests proxies to find working ones
        /// </summary>
        private async Task TestProxiesAsync(List<string> proxies, CancellationToken cancellationToken)
        {
            // Shuffle the proxies to avoid testing them all in the same order
            var shuffledProxies = proxies.OrderBy(_ => Guid.NewGuid()).ToList();

            Debug.WriteLine($"Testing {shuffledProxies.Count} proxies...");

            // To test proxies, we'll make a dummy request that will trigger the proxy testing
            string dummyUrl = "https://duckduckgo.com/";

            // Use a timeout for the entire testing process
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_maxProxyTestTimeSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            try
            {
                // This will trigger proxy testing internally
                string result = await _proxyClient.FetchHtmlAsync(dummyUrl, linkedCts.Token);

                if (string.IsNullOrEmpty(result))
                {
                    Debug.WriteLine("Warning: Initial proxy test failed to return content");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Proxy testing was canceled or timed out");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during proxy testing: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the proxy client, initializing it first if needed
        /// </summary>
        public async Task<ProxyEnabledHttpClient> GetProxyClientAsync(CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                await InitializeProxyPoolAsync(cancellationToken);
            }

            return _proxyClient;
        }

        /// <summary>
        /// Gets statistics about the current proxy pool
        /// </summary>
        public ProxyEnabledHttpClient.ProxyPoolStatistics GetStatistics()
        {
            return _proxyClient.GetStatistics();
        }

        /// <summary>
        /// Fetches HTML content from a URL using the proxy client
        /// </summary>
        public async Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                await InitializeProxyPoolAsync(cancellationToken);
            }

            return await _proxyClient.FetchHtmlAsync(url, cancellationToken);
        }
    }
}
