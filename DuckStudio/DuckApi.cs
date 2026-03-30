using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using AngleSharp;
using AngleSharp.Html.Parser;

using PuppeteerSharp;
using PuppeteerSharp.Input;

/// <summary>
/// Browser-based client for interacting with Duck.ai's chat UI via Puppeteer.
/// Uses headless Chrome automation to send messages and capture streamed responses
/// by monitoring DOM changes in the Duck.ai web interface.
/// </summary>
public class DuckAIBrowserClient : IDisposable
{
    // Timing constants for polling and timeouts
    private const int PageLoadDelayMs = 2000;
    private const int OnboardingMaxRetries = 30;
    private const int OnboardingRetryDelayMs = 500;
    private const int OnboardingDismissDelayMs = 1000;
    private const int TextareaMaxRetries = 40;
    private const int TextareaRetryDelayMs = 500;
    private const int TextareaTimeoutSeconds = TextareaMaxRetries * TextareaRetryDelayMs / 1000;
    private const int ResponseNewContainerTimeoutMs = 30000;
    private const int ResponseOverallTimeoutSeconds = 60;
    private const int ResponsePollIntervalMs = 200;
    private const int ResponseStabilityThreshold = 15; // iterations at 200ms = ~3 seconds
    private const int TypingDelayMs = 100;

    private const string DuckAiUrl = "https://duckduckgo.com/?q=duckduckgo+ai+chat&ia=chat&duckai=1";
    private const string TextareaSelector = "textarea[name='user-prompt']";
    private const string ActiveResponseSelector = "div[data-activeresponse=\"true\"]";

    private readonly string? _proxyAddress;

    /// <summary>
    /// Creates a new browser client, optionally routing all traffic through a proxy.
    /// </summary>
    /// <param name="proxyAddress">
    /// Proxy address in <c>scheme://host:port</c> form (e.g. <c>http://1.2.3.4:8080</c>),
    /// or <c>null</c> to connect directly.
    /// </param>
    public DuckAIBrowserClient(string? proxyAddress = null)
    {
        _proxyAddress = proxyAddress;
    }

    /// <summary>The proxy address Chrome is using, or <c>null</c> if connecting directly.</summary>
    public string? CurrentProxyAddress => _proxyAddress;

    private IBrowser? _browser;
    private IPage? _page;
    private string _fullResponse = "";

    /// <summary>Raised when internal debug/diagnostic messages are generated.</summary>
    public event EventHandler<string>? OnDebugOutput;

    /// <summary>Raised for each incremental text chunk received from the AI response.</summary>
    public event EventHandler<string>? OnResponseChunk;

    /// <summary>Raised when the full AI response has been received.</summary>
    public event EventHandler<string>? OnResponseComplete;

    /// <summary>Raised when an error occurs during browser interaction.</summary>
    public event EventHandler<Exception>? OnError;

    private void Debug(string message) => OnDebugOutput?.Invoke(this, message);

    /// <summary>
    /// Launches a headless Chrome browser, navigates to Duck.ai, accepts the
    /// onboarding modal (if present), and waits for the chat textarea to become ready.
    /// </summary>
    /// <exception cref="TimeoutException">Thrown if the chat UI does not become ready in time.</exception>
    public async Task InitializeAsync()
    {
        try
        {
            Debug("Launching browser...");
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            var launchArgs = new List<string> { "--no-sandbox", "--disable-setuid-sandbox" };
            if (!string.IsNullOrEmpty(_proxyAddress))
            {
                launchArgs.Add($"--proxy-server={_proxyAddress}");
                Debug($"Using proxy: {_proxyAddress}");
            }

            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = false,
                Args = launchArgs.ToArray()
            });

            _page = await _browser.NewPageAsync();

            await _page.SetRequestInterceptionAsync(true);
            _page.Request += async (sender, e) =>
            {
                var url = e.Request.Url;
                Debug($"Request: {e.Request.Method} {url}");

                // Log the solved VQD token that the browser sends with chat requests
                if (url.Contains("/duckchat/v1/chat") && e.Request.Method == HttpMethod.Post)
                {
                    if (e.Request.Headers.TryGetValue("x-vqd-hash-1", out var solvedToken) && !string.IsNullOrEmpty(solvedToken))
                    {
                        Debug($"=== BROWSER SOLVED VQD TOKEN ===");
                        Debug($"Raw token length: {solvedToken.Length}");
                        try
                        {
                            var json = Encoding.UTF8.GetString(Convert.FromBase64String(solvedToken));
                            Debug($"Decoded JSON: {json}");

                            // Parse and pretty-print key fields
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("server_hashes", out var sh))
                                Debug($"  server_hashes: {sh}");
                            if (root.TryGetProperty("client_hashes", out var ch))
                                Debug($"  client_hashes: {ch}");
                            if (root.TryGetProperty("signals", out var sig))
                                Debug($"  signals: {sig}");
                            if (root.TryGetProperty("meta", out var meta))
                                Debug($"  meta: {meta}");
                        }
                        catch (Exception ex)
                        {
                            Debug($"Error decoding solved token: {ex.Message}");
                        }
                        Debug($"=== END BROWSER SOLVED VQD TOKEN ===");
                    }
                }

                await e.Request.ContinueAsync();
            };

            _page.Response += async (sender, e) =>
            {
                var url = e.Response.Url;

                // Log VQD challenge from status endpoint
                if (url.Contains("/duckchat/v1/status"))
                {
                    Debug($"Status Response: {e.Response.Status}");
                    var headers = e.Response.Headers;
                    if (headers.TryGetValue("x-vqd-hash-1", out var challenge) && !string.IsNullOrEmpty(challenge))
                    {
                        Debug($"=== VQD CHALLENGE FROM SERVER ===");
                        Debug($"Raw challenge length: {challenge.Length}");
                        try
                        {
                            var js = Encoding.UTF8.GetString(Convert.FromBase64String(challenge));
                            Debug($"Challenge JS length: {js.Length}");
                            // Log first 500 chars to see structure
                            Debug($"Challenge JS start: {js.Substring(0, Math.Min(500, js.Length))}");
                            // Log last 500 chars to see metadata
                            Debug($"Challenge JS end: {js.Substring(Math.Max(0, js.Length - 500))}");
                        }
                        catch (Exception ex)
                        {
                            Debug($"Error decoding challenge: {ex.Message}");
                        }
                        Debug($"=== END VQD CHALLENGE ===");
                    }
                }

                // Log VQD from chat response (next challenge)
                if (url.Contains("/duckchat/v1/chat"))
                {
                    Debug($"Chat Response Status: {e.Response.Status}");
                    var headers = e.Response.Headers;
                    if (headers.TryGetValue("x-vqd-hash-1", out var nextChallenge) && !string.IsNullOrEmpty(nextChallenge))
                    {
                        Debug($"=== NEXT VQD CHALLENGE FROM CHAT RESPONSE ===");
                        Debug($"Raw challenge length: {nextChallenge.Length}");
                        try
                        {
                            var js = Encoding.UTF8.GetString(Convert.FromBase64String(nextChallenge));
                            Debug($"Challenge JS length: {js.Length}");
                            Debug($"Challenge JS start: {js.Substring(0, Math.Min(500, js.Length))}");
                            Debug($"Challenge JS end: {js.Substring(Math.Max(0, js.Length - 500))}");
                        }
                        catch (Exception ex)
                        {
                            Debug($"Error decoding next challenge: {ex.Message}");
                        }
                        Debug($"=== END NEXT VQD CHALLENGE ===");
                    }

                    if (e.Response.Status != HttpStatusCode.OK)
                    {
                        try
                        {
                            var errorText = await e.Response.TextAsync();
                            Debug($"Error response: {errorText}");
                        }
                        catch { }
                    }
                }
            };

            _page.Console += (sender, e) => Debug($"Browser console: {e.Message.Text}");

            Debug("Navigating to Duck.ai...");
            await _page.GoToAsync(DuckAiUrl,
                new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

            Debug("Waiting for page to load...");
            await Task.Delay(PageLoadDelayMs);

            Debug("Looking for onboarding modal...");
            await AcceptOnboardingAsync();

            Debug("Waiting for chat input to be ready...");
            await WaitForTextareaReadyAsync();

            Debug("Chat UI loaded and ready.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            throw;
        }
    }

    /// <summary>
    /// Sends a question to the Duck.ai chat interface and streams back the AI response.
    /// Monitors the DOM for new response containers and polls for text changes until
    /// the response is complete (detected via send-button re-enablement or text stability).
    /// </summary>
    /// <param name="question">The user's question to send.</param>
    /// <returns>The complete AI response text.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the client has not been initialized.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="question"/> is null or empty.</exception>
    /// <exception cref="TimeoutException">Thrown if the response is not received within the timeout period.</exception>
    public async Task<string> AskAsync(string question)
    {
        if (_page is null)
            throw new InvalidOperationException("DuckAIBrowserClient is not initialized. Call InitializeAsync() first.");

        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be null or empty.", nameof(question));

        _fullResponse = "";
        try
        {
            await WaitForTextareaReadyAsync();

            // Count existing response divs before sending so we can find the NEW one
            var existingResponseCount = await _page.EvaluateFunctionAsync<int>(@"
                () => document.querySelectorAll('div[data-activeresponse=""true""]').length");

            await _page.FocusAsync(TextareaSelector);
            await _page.TypeAsync(TextareaSelector, question, new TypeOptions { Delay = TypingDelayMs });
            await _page.Keyboard.PressAsync("Enter");
            Debug("Message sent, waiting for response...");

            // Wait for a NEW response container to appear (one more than before)
            await _page.WaitForFunctionAsync(
                $"() => document.querySelectorAll('{ActiveResponseSelector}').length > {existingResponseCount}",
                new WaitForFunctionOptions { Timeout = ResponseNewContainerTimeoutMs });

            string previousText = "";
            int unchangedIterations = 0;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ResponseOverallTimeoutSeconds));
            while (!cts.Token.IsCancellationRequested)
            {
                // Extract the response text from the newest response container, stripping
                // the first and last child elements (header/footer chrome)
                var currentText = await _page.EvaluateFunctionAsync<string>($@"
                (expectedIndex) => {{
                    const messages = document.querySelectorAll('div[data-activeresponse=""true""]');
                    if (messages.length <= expectedIndex) return '';
                    const lastMsg = messages[expectedIndex];
                    const clone = lastMsg.cloneNode(true);
                    if (clone.children.length >= 3) {{
                        clone.removeChild(clone.children[0]);
                        clone.removeChild(clone.children[clone.children.length - 1]);
                    }}
                    return clone.innerText.trim();
                }}", existingResponseCount);

                if (currentText != previousText)
                {
                    string newChunk = currentText.Length > previousText.Length
                        ? currentText.Substring(previousText.Length)
                        : currentText;
                    OnResponseChunk?.Invoke(this, newChunk);
                    _fullResponse = currentText;
                    previousText = currentText;
                    unchangedIterations = 0;
                }
                else if (!string.IsNullOrEmpty(currentText))
                {
                    unchangedIterations++;
                }

                // Check if the send button is re-enabled (indicates response is done)
                var isComplete = await _page.EvaluateFunctionAsync<bool>(@"
                () => {
                    const sendButton = document.querySelector('button[type=""submit""][aria-label=""Send""]');
                    if (sendButton && !sendButton.disabled) return true;
                    const textarea = document.querySelector('textarea[name=""user-prompt""]');
                    const activeResponse = document.querySelector('div[data-activeresponse=""true""]');
                    if (textarea && !textarea.disabled && activeResponse) {
                        const style = window.getComputedStyle(textarea);
                        if (style.display !== 'none' && style.visibility !== 'hidden') return true;
                    }
                    return false;
                }");

                if (isComplete)
                    break;

                // Fallback: if text hasn't changed for ~3 seconds, assume response is done
                if (unchangedIterations >= ResponseStabilityThreshold)
                {
                    Debug("Response text stable for 3s, assuming complete.");
                    break;
                }

                await Task.Delay(ResponsePollIntervalMs, cts.Token);
            }

            if (cts.Token.IsCancellationRequested)
                throw new TimeoutException($"Response timed out after {ResponseOverallTimeoutSeconds} seconds.");

            OnResponseComplete?.Invoke(this, _fullResponse);

            // Ensure the input field is ready before returning so the caller can send the next message
            await WaitForTextareaReadyAsync();

            return _fullResponse;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            throw;
        }
    }

    /// <summary>
    /// Attempts to click the onboarding "Agree and Continue" button if the modal is present.
    /// Uses data-testid as the primary selector with a fallback to dialog role + button text matching.
    /// Silently continues if no modal is found (it may have been previously accepted).
    /// </summary>
    private async Task AcceptOnboardingAsync()
    {
        if (_page is null)
            throw new InvalidOperationException("DuckAIBrowserClient is not initialized.");

        for (int i = 0; i < OnboardingMaxRetries; i++)
        {
            var clicked = await _page.EvaluateFunctionAsync<bool>(@"
                () => {
                    // Primary: use data-testid (most reliable)
                    const agreeBtn = document.querySelector('button[data-testid=""DUCKAI_ONBOARDING_AGREE""]');
                    if (agreeBtn) {
                        agreeBtn.click();
                        return true;
                    }
                    // Fallback: find button inside a dialog with matching text
                    const dialog = document.querySelector('div[role=""dialog""]');
                    if (dialog) {
                        const buttons = dialog.querySelectorAll('button');
                        for (const btn of buttons) {
                            const label = (btn.innerText || '').trim().toLowerCase();
                            if (label.includes('agree') || label.includes('accept')) {
                                btn.click();
                                return true;
                            }
                        }
                    }
                    return false;
                }");

            if (clicked)
            {
                Debug("Clicked 'Agree and Continue' button.");
                await Task.Delay(OnboardingDismissDelayMs);
                return;
            }

            await Task.Delay(OnboardingRetryDelayMs);
        }

        Debug("No onboarding modal found - may already be accepted.");
    }

    /// <summary>
    /// Polls until the chat textarea element exists and is not disabled.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// Thrown if the textarea does not become ready within <see cref="TextareaTimeoutSeconds"/> seconds.
    /// </exception>
    private async Task WaitForTextareaReadyAsync()
    {
        if (_page is null)
            throw new InvalidOperationException("DuckAIBrowserClient is not initialized.");

        for (int i = 0; i < TextareaMaxRetries; i++)
        {
            var isReady = await _page.EvaluateFunctionAsync<bool>(@"
                () => {
                    const textarea = document.querySelector('textarea[name=""user-prompt""]');
                    if (!textarea || textarea.disabled) return false;
                    return true;
                }");

            if (isReady)
                return;

            await Task.Delay(TextareaRetryDelayMs);
        }

        throw new TimeoutException($"Chat textarea did not become ready within {TextareaTimeoutSeconds} seconds.");
    }

    /// <summary>
    /// Closes the browser page and browser instance, releasing all resources.
    /// </summary>
    public void Dispose()
    {
        _page?.CloseAsync().GetAwaiter().GetResult();
        _browser?.CloseAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// HTTP API client for DuckDuckGo's AI Chat service (Duck.ai).
/// Handles the full authentication flow including cookie setup, FE version extraction,
/// VQD security challenge solving, and Server-Sent Events (SSE) streaming.
/// </summary>
/// <remarks>
/// <para>
/// The initialization flow follows the real browser protocol:
/// 1. Set access_type cookie via redirect endpoint
/// 2. Load chat page to extract FE version and cookies
/// 3. Fetch auth token
/// 4. Obtain VQD token by solving an obfuscated JavaScript challenge
/// </para>
/// <para>
/// The VQD challenge is a JavaScript-based bot detection mechanism that requires
/// parsing an obfuscated string array, solving a rotation checksum, extracting
/// server parameters, computing a DOM fingerprint, and assembling a signed response.
/// This client solves the challenge entirely in C# without any JS runtime.
/// </para>
/// </remarks>
public class DuckAIClient : IDisposable
{
    // API endpoints and base URL
    private const string BaseUrl = "https://duck.ai";
    private const string DuckDuckGoUrl = "https://duckduckgo.com";
    private const string AccessTypeEndpoint = "/access-type-dev-01?duckai=1";
    private const string ChatPageEndpoint = "/chat?duckai=1";
    private const string AuthTokenEndpoint = "/duckchat/v1/auth/token";
    private const string StatusEndpoint = "/duckchat/v1/status";
    private const string ChatEndpoint = "/duckchat/v1/chat";
    private const string SerpUrl = "https://duckduckgo.com/?q=DuckDuckGo+AI+Chat&ia=chat&duckai=1";

    // Browser-like User-Agent string (must match the value used in VQD challenge solving)
    private const string ChromeUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    // Timeouts and retry
    private const int SendMessageTimeoutSeconds = 120;
    private const int MaxSessionRetries = 1; // retry once after re-init on 418

    // SSE protocol markers
    private const string SseDataPrefix = "data: ";
    private const string SseDoneMarker = "[DONE]";

    // VQD challenge response protocol version
    private const string VqdProtocolVersion = "4";
    private const string VqdDuration = "50";

    private HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private readonly List<ChatMessage> _conversationHistory = new();
    private readonly Random _random = new();
    private readonly ProxyPool.ProxyEnabledHttpClient? _proxyPool;
    private string? _currentProxyAddress;

    private string? _vqdToken;
    private string _activeModel = "gpt-4o-mini";
    private bool _isInitialized;
    private string? _feVersion;
    private string? _webpackHash;
    private int _messageCount;
    private long _sessionStartMs;

    /// <summary>Raised when internal debug/diagnostic messages are generated.</summary>
    public event EventHandler<string>? OnDebugOutput;

    /// <summary>Raised when an HTTP request is about to be sent (for debugging/logging).</summary>
    public event EventHandler<HttpRequestMessage>? OnRequestSending;

    /// <summary>Raised when an HTTP response is received (for debugging/logging).</summary>
    public event EventHandler<HttpResponseMessage>? OnResponseReceived;

    /// <summary>Raised for each incremental text chunk received from the AI response stream.</summary>
    public event EventHandler<string>? OnResponseChunk;

    /// <summary>Raised when the full AI response has been received and assembled.</summary>
    public event EventHandler<string>? OnResponseComplete;

    /// <summary>Raised when an error occurs during API interaction.</summary>
    public event EventHandler<Exception>? OnError;

    /// <summary>
    /// AI model identifiers available through the Duck.ai chat service.
    /// </summary>
    public static readonly string[] AvailableModels =
    {
        "gpt-4o-mini",
        "claude-3-haiku-20240307",
        "meta-llama/Meta-Llama-3.1-70B-Instruct-Turbo",
        "mistralai/Mixtral-8x7B-Instruct-v0.1",
        "o3-mini"
    };

    /// <summary>
    /// Creates a new DuckAIClient with browser-like HTTP headers and cookie management.
    /// </summary>
    /// <param name="proxyPool">Optional proxy pool to route requests through. The best available proxy will be selected automatically, and rotated on failure.</param>
    public DuckAIClient(ProxyPool.ProxyEnabledHttpClient? proxyPool = null)
    {
        _cookieContainer = new CookieContainer();
        _proxyPool = proxyPool;
        var proxy = proxyPool?.GetBestProxyWebProxy();
        _currentProxyAddress = (proxy?.Address as Uri)?.OriginalString;
        _httpClient = BuildHttpClient(proxy);

        Debug("DuckAIClient initialized");
    }

    /// <summary>
    /// Builds a new HttpClient with browser-like headers, optionally routed through the given proxy.
    /// </summary>
    private HttpClient BuildHttpClient(System.Net.IWebProxy? proxy)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = _cookieContainer,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            UseProxy = proxy != null,
            Proxy = proxy
        };

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", ChromeUserAgent);
        client.DefaultRequestHeaders.Add("Accept", "*/*");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        client.DefaultRequestHeaders.Add("DNT", "1");
        client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Google Chrome\";v=\"120\", \"Chromium\";v=\"120\", \"Not?A_Brand\";v=\"24\"");
        client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
        client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
        client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        client.DefaultRequestHeaders.Add("Pragma", "no-cache");
        return client;
    }

    /// <summary>
    /// Rotates to the next best proxy from the pool, skipping any addresses that have already failed.
    /// Returns true if a new proxy was applied; false if no more proxies are available.
    /// </summary>
    private bool RotateProxy(HashSet<string> failedAddresses)
    {
        if (_proxyPool == null) return false;

        var candidates = _proxyPool.GetHealthyProxyWebProxies(20);
        var next = candidates.FirstOrDefault(p =>
        {
            var addr = (p.Address as Uri)?.OriginalString ?? string.Empty;
            return !failedAddresses.Contains(addr);
        });

        if (next == null)
        {
            Debug("No more healthy proxies available to rotate to.");
            return false;
        }

        var oldClient = _httpClient;
        var newAddr = (next.Address as Uri)?.OriginalString ?? string.Empty;
        Debug($"Rotating proxy: {_currentProxyAddress} -> {newAddr}");
        _httpClient = BuildHttpClient(next);
        _currentProxyAddress = newAddr;
        oldClient.Dispose();

        // Reset session so it re-initialises through the new proxy
        _isInitialized = false;
        _vqdToken = null;
        return true;
    }

    private void Debug(string message)
    {
        OnDebugOutput?.Invoke(this, $"[DEBUG] {DateTime.Now:HH:mm:ss.fff} - {message}");
    }

    /// <summary>
    /// Logs detailed information about an outgoing HTTP request (method, URL, headers).
    /// </summary>
    private void DebugRequest(HttpRequestMessage request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-> Request: {request.Method} {request.RequestUri}");
        sb.AppendLine("  Headers:");
        foreach (var header in request.Headers)
            sb.AppendLine($"    {header.Key}: {string.Join(", ", header.Value)}");

        if (request.Content != null)
        {
            sb.AppendLine("  Content Headers:");
            foreach (var header in request.Content.Headers)
                sb.AppendLine($"    {header.Key}: {string.Join(", ", header.Value)}");
        }

        Debug(sb.ToString());
        OnRequestSending?.Invoke(this, request);
    }

    /// <summary>
    /// Logs detailed information about an HTTP response (status, headers, cookies, body preview).
    /// </summary>
    /// <param name="response">The HTTP response to log.</param>
    /// <param name="skipContent">
    /// If true, skips reading the response body (important for streaming responses
    /// where the body must be consumed by the caller).
    /// </param>
    private async Task DebugResponseAsync(HttpResponseMessage response, bool skipContent = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<- Response: {(int)response.StatusCode} {response.ReasonPhrase}");
        sb.AppendLine("  Headers:");
        foreach (var header in response.Headers)
            sb.AppendLine($"    {header.Key}: {string.Join(", ", header.Value)}");

        var cookies = _cookieContainer.GetCookies(new Uri(BaseUrl));
        if (cookies.Count > 0)
        {
            sb.AppendLine("  Cookies:");
            foreach (Cookie cookie in cookies)
                sb.AppendLine($"    {cookie.Name}={cookie.Value}");
        }

        if (!skipContent && response.Content != null)
        {
            var content = await response.Content.ReadAsStringAsync();
            sb.AppendLine(content.Length < 1000
                ? $"  Content: {content}"
                : $"  Content: [{content.Length} bytes]");
        }
        else if (skipContent)
        {
            sb.AppendLine("  Content: [streaming - not read]");
        }

        Debug(sb.ToString());
        OnResponseReceived?.Invoke(this, response);
    }

    /// <summary>
    /// Performs the full Duck.ai authentication and session setup sequence.
    /// This must be called (and succeed) before sending any chat messages.
    /// </summary>
    /// <returns>True if initialization succeeded and a VQD token was obtained; false otherwise.</returns>
    public async Task<bool> InitializeAsync()
    {
        var failedProxies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_currentProxyAddress != null) failedProxies.Add(_currentProxyAddress);

        while (true)
        {
            Debug("=== Starting Initialization ===");
            if (_currentProxyAddress != null)
                Debug($"Using proxy: {_currentProxyAddress}");

            bool rotated = false;
            try
            {
                // Step 1: Set access_type cookie via the redirect endpoint
                Debug("Setting access_type cookie...");
                using (var accessReq = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{AccessTypeEndpoint}"))
                {
                    accessReq.Headers.Add("Referer", $"{DuckDuckGoUrl}/");
                    var accessResp = await _httpClient.SendAsync(accessReq);
                    Debug($"Access type response: {(int)accessResp.StatusCode}");
                }

                // Step 2: Load the chat page to set cookies and extract the FE version
                Debug($"Loading chat page: {BaseUrl}{ChatPageEndpoint}");
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{ChatPageEndpoint}");
                request.Headers.Add("Referer", $"{DuckDuckGoUrl}/");
                DebugRequest(request);
                var initialResponse = await _httpClient.SendAsync(request);
                initialResponse.EnsureSuccessStatusCode();
                var htmlContent = await initialResponse.Content.ReadAsStringAsync();
                Debug($"HTML content length: {htmlContent.Length} characters");

                // Step 3: Extract FE version -- try chat page first, then duckduckgo.com SERP
                ExtractFeVersion(htmlContent);
                if (string.IsNullOrEmpty(_feVersion))
                {
                    Debug("FE version not in chat page, fetching from duckduckgo.com SERP...");
                    using var serpReq = new HttpRequestMessage(HttpMethod.Get, SerpUrl);
                    serpReq.Headers.Add("Referer", $"{DuckDuckGoUrl}/");
                    var serpResp = await _httpClient.SendAsync(serpReq);
                    if (serpResp.IsSuccessStatusCode)
                    {
                        var serpHtml = await serpResp.Content.ReadAsStringAsync();
                        ExtractFeVersion(serpHtml);
                    }
                }

                // Step 3b: Extract webpack bundle hash from script tag URLs
                var webpackMatch = Regex.Match(htmlContent, @"entry\.duckai\.([a-f0-9]{10,})\.js");
                if (webpackMatch.Success)
                {
                    _webpackHash = webpackMatch.Groups[1].Value;
                    Debug($"Extracted webpack hash: {_webpackHash}");
                }
                else
                {
                    Debug("Could not extract webpack hash from HTML");
                }

                // Step 4: Call auth/token endpoint to establish session
                Debug("Fetching auth token...");
                using (var authReq = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{AuthTokenEndpoint}"))
                {
                    authReq.Headers.Add("Referer", $"{BaseUrl}/");
                    var authResp = await _httpClient.SendAsync(authReq);
                    Debug($"Auth token response: {(int)authResp.StatusCode}");
                }

                // Step 5: Get VQD token -- try status endpoint first, fallback to HTML extraction
                if (!await FetchStatusAsync())
                {
                    Debug("Status endpoint failed, extracting VQD from HTML...");
                    ExtractVqdFromHtml(htmlContent);
                }

                if (string.IsNullOrEmpty(_vqdToken))
                {
                    Debug("Failed to obtain VQD token from any source");
                    // Treat this as a proxy failure and rotate
                    rotated = TryRotateProxyForInit(failedProxies);
                    if (rotated) continue;
                    return false;
                }

                _isInitialized = true;
                _messageCount = 0;
                _sessionStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Debug("=== Initialization Complete ===\n");
                return true;
            }
            catch (Exception ex)
            {
                Debug($"Initialization error: {ex.Message}");
                Debug($"Stack trace: {ex.StackTrace}");

                rotated = TryRotateProxyForInit(failedProxies);
                if (rotated)
                {
                    Debug("Retrying initialization with next proxy...");
                    continue;
                }

                OnError?.Invoke(this, ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Marks the current proxy as failed and rotates to the next one.
    /// Returns true if a new proxy was applied and we should retry init.
    /// </summary>
    private bool TryRotateProxyForInit(HashSet<string> failedProxies)
    {
        if (_proxyPool == null) return false;
        if (_currentProxyAddress != null)
        {
            _proxyPool.ReportProxyFailure(_currentProxyAddress);
            failedProxies.Add(_currentProxyAddress);
        }
        return RotateProxy(failedProxies);
    }

    /// <summary>
    /// Extracts the Duck.ai front-end version string from page HTML.
    /// Combines __DDG_BE_VERSION__ and __DDG_FE_CHAT_HASH__ if both are present.
    /// </summary>
    private void ExtractFeVersion(string htmlContent)
    {
        var beMatch = Regex.Match(htmlContent, @"__DDG_BE_VERSION__\s*=\s*""([^""]+)""");
        var feHashMatch = Regex.Match(htmlContent, @"__DDG_FE_CHAT_HASH__\s*=\s*""([^""]+)""");

        if (beMatch.Success && feHashMatch.Success)
        {
            _feVersion = $"{beMatch.Groups[1].Value}-{feHashMatch.Groups[1].Value}";
            Debug($"Extracted FE version: {_feVersion}");
        }
        else if (beMatch.Success)
        {
            _feVersion = beMatch.Groups[1].Value;
            Debug($"Extracted partial FE version (no chat hash): {_feVersion}");
        }
        else
        {
            Debug("Could not extract FE version from HTML");
        }
    }

    /// <summary>
    /// Attempts to extract a VQD token directly from page HTML using multiple regex patterns.
    /// This is a fallback when the status endpoint challenge flow fails.
    /// </summary>
    private void ExtractVqdFromHtml(string htmlContent)
    {
        var patterns = new[]
        {
            @"vqd\s*=\s*""([^""]+)""",
            @"vqd['""]?\s*[:=]\s*['""]([a-f0-9\-]+)['""]",
            @"""vqd""\s*:\s*""([^""]+)"""
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(htmlContent, pattern);
            if (match.Success)
            {
                _vqdToken = match.Groups[1].Value;
                Debug($"Extracted VQD token from HTML: {_vqdToken}");
                return;
            }
        }

        Debug("Could not extract VQD token from HTML");
    }

    /// <summary>
    /// Fetches the status endpoint to obtain a VQD challenge, solves it, and stores the resulting token.
    /// </summary>
    /// <returns>True if a VQD challenge was received and solved successfully.</returns>
    private async Task<bool> FetchStatusAsync()
    {
        Debug("Fetching status with VQD accept...");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{StatusEndpoint}");
        request.Headers.Add("Referer", $"{BaseUrl}/");
        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("x-vqd-accept", "1");
        request.Headers.Add("Cache-Control", "no-store");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        request.Headers.Add("Sec-Fetch-Mode", "cors");
        request.Headers.Add("Sec-Fetch-Dest", "empty");
        var response = await _httpClient.SendAsync(request);
        await DebugResponseAsync(response);

        if (response.Headers.TryGetValues("x-vqd-hash-1", out var values))
        {
            var challengeBase64 = values.FirstOrDefault();
            if (!string.IsNullOrEmpty(challengeBase64))
            {
                Debug($"Got VQD challenge (length: {challengeBase64.Length})");
                var solvedToken = SolveVqdChallenge(challengeBase64);
                if (!string.IsNullOrEmpty(solvedToken))
                {
                    _vqdToken = solvedToken;
                    Debug("VQD challenge solved successfully");
                    return true;
                }
                Debug("Failed to solve VQD challenge");
            }
        }

        Debug("Failed to get x-vqd-hash-1 header");
        return false;
    }

    #region VQD Challenge Solver

    /// <summary>
    /// Solves a VQD JavaScript challenge entirely in C#.
    /// <para>
    /// The challenge is a base64-encoded obfuscated JavaScript snippet. This method:
    /// 1. Decodes and parses the obfuscated string array
    /// 2. Solves the array rotation via checksum matching
    /// 3. Builds a lookup table to deobfuscate all string references
    /// 4. Extracts server hashes, challenge ID, timestamp, and XOR key
    /// 5. Computes a DOM fingerprint by parsing malformed HTML through AngleSharp
    /// 6. Computes bot detection results (simulating a real browser)
    /// 7. Hashes client fingerprint values with SHA-256
    /// 8. Assembles and base64-encodes the final response
    /// </para>
    /// </summary>
    /// <param name="challengeBase64">The base64-encoded JavaScript challenge from the x-vqd-hash-1 header.</param>
    /// <returns>The solved VQD token (base64-encoded JSON), or null if solving failed.</returns>
    private string? SolveVqdChallenge(string challengeBase64)
    {
        try
        {
            var challengeJs = Encoding.UTF8.GetString(Convert.FromBase64String(challengeBase64));
            Debug($"Challenge JS length: {challengeJs.Length} chars");

            // Step 1: Extract the string array from the obfuscated JS
            var stringArray = ExtractStringArray(challengeJs);
            if (stringArray == null || stringArray.Count == 0)
            {
                Debug("Failed to extract string array from challenge");
                return null;
            }
            Debug($"Extracted string array with {stringArray.Count} entries");

            // Step 2: Find the base offset for the lookup function (e.g., 0x11f)
            var baseOffsetMatch = Regex.Match(challengeJs, @"=_0x\w+-0x([0-9a-fA-F]+);");
            if (!baseOffsetMatch.Success)
            {
                Debug("Failed to find lookup base offset");
                return null;
            }
            int baseOffset = Convert.ToInt32(baseOffsetMatch.Groups[1].Value, 16);
            Debug($"Lookup base offset: 0x{baseOffset:x}");

            // Step 3: Find the rotation target and solve the array rotation
            // The rotation call looks like: }(_0x3f55,0x575dc)); -- note double )) from IIFE wrapper
            var rotationTargetMatch = Regex.Match(challengeJs, @"\(_0x\w+,\s*0x([0-9a-fA-F]+)\)\s*\)\s*;");
            if (!rotationTargetMatch.Success)
            {
                Debug("Failed to find rotation target");
                return null;
            }
            int rotationTarget = Convert.ToInt32(rotationTargetMatch.Groups[1].Value, 16);
            Debug($"Rotation target: {rotationTarget}");

            var rotatedArray = SolveRotation(challengeJs, stringArray, rotationTarget);
            if (rotatedArray == null)
            {
                Debug("Failed to solve string array rotation");
                return null;
            }

            // Build the index-to-string lookup table
            var lookup = new Dictionary<int, string>();
            for (int i = 0; i < rotatedArray.Count; i++)
                lookup[baseOffset + i] = rotatedArray[i];

            // Step 4: Extract server_hashes from the return statement
            var serverHashes = ExtractServerHashes(challengeJs, lookup);
            Debug($"Server hashes: {string.Join(", ", serverHashes)}");

            // Step 5: Extract challenge_id -- may be a literal string or a lookup call
            string challengeId = ExtractChallengeId(challengeJs, lookup);
            Debug($"Challenge ID: {challengeId}");

            // Step 6: Extract timestamp from lookup table
            string timestamp = ExtractTimestamp(challengeJs, lookup);
            Debug($"Timestamp: {timestamp}");

            // Step 7: Extract the XOR key used for the debug field computation
            string xorKey = ExtractXorKey(challengeJs, lookup);
            Debug($"XOR key: {xorKey}");

            // Step 8: Extract DOM base offset from the innerHTML-based DOM probe.
            // The challenge has two String(base+...) calls:
            //   DOM probe: String(0xNNN + el.innerHTML.length * el.querySelectorAll('*').length)
            //   Bot probe: String(0xNNN + arr.reduce(...))
            // Pattern A: hex literal -- String(0xNNN+...)
            // Pattern B: lookup call -- String(_0xLOOKUP(0xNN)+...) where the lookup resolves to a number
            int domBase = 0;
            int botBase = ExtractBotBase(challengeJs);
            Debug($"Bot base: {botBase}");

            // Pattern A: Direct hex literals in String(0xNNN+...)
            var allStringMatches = Regex.Matches(challengeJs, @"String\(0x([0-9a-fA-F]+)\+");
            foreach (Match sm in allStringMatches)
            {
                int val = Convert.ToInt32(sm.Groups[1].Value, 16);
                if (val == botBase) continue; // skip bot detection String()

                // Check that this isn't the bot detection one (near .reduce)
                string after = challengeJs.Substring(sm.Index, Math.Min(200, challengeJs.Length - sm.Index));
                if (after.Contains(".reduce(")) continue;

                domBase = val;
                break;
            }

            // Pattern B: Lookup calls in String(_0xLOOKUP(0xNN)+...)
            // Some challenges pass the DOM base through an obfuscated lookup function
            // instead of using a hex literal directly.
            if (domBase == 0)
            {
                var lookupStringMatches = Regex.Matches(challengeJs,
                    @"String\(_0x\w+\(0x([0-9a-fA-F]+)\)\s*\+");
                foreach (Match sm in lookupStringMatches)
                {
                    int lookupIdx = Convert.ToInt32(sm.Groups[1].Value, 16);
                    if (!lookup.TryGetValue(lookupIdx, out var resolvedValue)) continue;

                    // The resolved value should be a numeric string (the DOM base offset)
                    if (!int.TryParse(resolvedValue, out int val)) continue;
                    if (val == botBase) continue;

                    // Check that this isn't the bot detection one (near .reduce)
                    string after = challengeJs.Substring(sm.Index, Math.Min(200, challengeJs.Length - sm.Index));
                    if (after.Contains(".reduce(")) continue;

                    domBase = val;
                    Debug($"DOM base found via lookup call pattern (resolved from index 0x{lookupIdx:x})");
                    break;
                }
            }

            // Pattern C: Resolved lookup near innerHTML/querySelectorAll keywords
            // Some challenges compute the DOM fingerprint without String() wrapper,
            // e.g.: (baseVal + el.innerHTML.length * el.querySelectorAll('*').length)
            if (domBase == 0)
            {
                // Find 0xNNN near innerHTML or querySelectorAll (but not near .reduce)
                var innerHtmlMatches = Regex.Matches(challengeJs,
                    @"0x([0-9a-fA-F]+)\s*\+\s*\w+(?:\[_0x\w+\([^)]+\)\]|\.\w+)*\.(?:innerHTML|length)");
                foreach (Match sm in innerHtmlMatches)
                {
                    int val = Convert.ToInt32(sm.Groups[1].Value, 16);
                    if (val == botBase) continue;

                    string context = challengeJs.Substring(
                        Math.Max(0, sm.Index - 50),
                        Math.Min(300, challengeJs.Length - Math.Max(0, sm.Index - 50)));
                    if (context.Contains(".reduce(")) continue;

                    domBase = val;
                    Debug($"DOM base found near innerHTML/length keyword");
                    break;
                }
            }

            Debug($"DOM base: {domBase}");

            // Step 9: Find the malformed HTML and compute DOM fingerprint.
            // Search lookup table values for HTML-like content (with both < and >).
            string malformedHtml = lookup.Values.FirstOrDefault(v => v.Contains('<') && v.Contains('>')) ?? "";

            // Fallback 1: Search lookup for values with < but no > (unclosed tags like "<div")
            if (string.IsNullOrEmpty(malformedHtml))
            {
                malformedHtml = lookup.Values.FirstOrDefault(v => v.Contains('<') && v.Length > 3 &&
                    Regex.IsMatch(v, @"<[a-zA-Z]")) ?? "";
                if (!string.IsNullOrEmpty(malformedHtml))
                    Debug($"Malformed HTML found via relaxed < check (no > required)");
            }

            // Fallback 2: Trace the 'srcdoc' property assignment in the obfuscated code.
            // The challenge creates an iframe and sets: iframe[lookup('srcdoc')] = lookup(htmlIndex)
            // We find the srcdoc index in the lookup table, then find what value is assigned to it.
            if (string.IsNullOrEmpty(malformedHtml))
            {
                var srcdocEntry = lookup.FirstOrDefault(kv => kv.Value == "srcdoc");
                if (!srcdocEntry.Equals(default(KeyValuePair<int, string>)))
                {
                    string srcdocHex = srcdocEntry.Key.ToString("x");
                    // Look for: 0xSRCDOC_IDX)]=_0xLOOKUP(0xHTML_IDX)
                    // or: 0xSRCDOC_IDX))=_0xLOOKUP(0xHTML_IDX)
                    var srcdocAssign = Regex.Match(challengeJs,
                        $@"0x{srcdocHex}\)[\]\)]*\s*=\s*_0x\w+\(0x([0-9a-fA-F]+)\)");
                    if (srcdocAssign.Success)
                    {
                        int htmlIdx = Convert.ToInt32(srcdocAssign.Groups[1].Value, 16);
                        if (lookup.TryGetValue(htmlIdx, out var htmlValue) && !string.IsNullOrEmpty(htmlValue))
                        {
                            malformedHtml = htmlValue;
                            Debug($"Malformed HTML found via srcdoc assignment trace (index 0x{htmlIdx:x})");
                        }
                    }
                }
            }

            // Fallback 3: Trace the 'content' property assignment (meta element pattern).
            // Some challenges set: metaEl[lookup('content')] = lookup(htmlIndex)
            if (string.IsNullOrEmpty(malformedHtml))
            {
                var contentEntry = lookup.FirstOrDefault(kv => kv.Value == "content");
                if (!contentEntry.Equals(default(KeyValuePair<int, string>)))
                {
                    string contentHex = contentEntry.Key.ToString("x");
                    var contentAssign = Regex.Match(challengeJs,
                        $@"0x{contentHex}\)[\]\)]*\s*=\s*_0x\w+\(0x([0-9a-fA-F]+)\)");
                    if (contentAssign.Success)
                    {
                        int htmlIdx = Convert.ToInt32(contentAssign.Groups[1].Value, 16);
                        if (lookup.TryGetValue(htmlIdx, out var htmlValue) &&
                            !string.IsNullOrEmpty(htmlValue) &&
                            htmlValue.Contains('<'))
                        {
                            malformedHtml = htmlValue;
                            Debug($"Malformed HTML found via content assignment trace (index 0x{htmlIdx:x})");
                        }
                    }
                }
            }

            // Fallback 4: Search for literal HTML strings in the JS code
            if (string.IsNullOrEmpty(malformedHtml))
            {
                // Look for innerHTML/srcdoc assignment with a literal HTML string
                var htmlLiteralMatch = Regex.Match(challengeJs, @"='(<[a-zA-Z][^']*>(?:[^']*<[^']*)*)'");
                if (htmlLiteralMatch.Success)
                {
                    malformedHtml = htmlLiteralMatch.Groups[1].Value;
                    malformedHtml = Regex.Replace(malformedHtml, @"\\x([0-9a-fA-F]{2})",
                        em => ((char)Convert.ToInt32(em.Groups[1].Value, 16)).ToString());
                    malformedHtml = Regex.Replace(malformedHtml, @"\\u([0-9a-fA-F]{4})",
                        em => ((char)Convert.ToInt32(em.Groups[1].Value, 16)).ToString());
                    Debug($"Malformed HTML found as literal in JS code");
                }
            }

            // Fallback 5: Look for hex-escaped HTML literals (e.g., \x3cdiv\x3e)
            if (string.IsNullOrEmpty(malformedHtml))
            {
                var hexHtmlMatch = Regex.Match(challengeJs, @"='((?:\\x[0-9a-fA-F]{2})+[^']*)'");
                if (hexHtmlMatch.Success)
                {
                    var candidate = hexHtmlMatch.Groups[1].Value;
                    candidate = Regex.Replace(candidate, @"\\x([0-9a-fA-F]{2})",
                        em => ((char)Convert.ToInt32(em.Groups[1].Value, 16)).ToString());
                    candidate = Regex.Replace(candidate, @"\\u([0-9a-fA-F]{4})",
                        em => ((char)Convert.ToInt32(em.Groups[1].Value, 16)).ToString());
                    if (candidate.Contains('<'))
                    {
                        malformedHtml = candidate;
                        Debug($"Malformed HTML found via hex-escaped literal");
                    }
                }
            }

            Debug($"Malformed HTML: {malformedHtml}");

            string domResult = ComputeDomFingerprint(malformedHtml, domBase);
            Debug($"DOM fingerprint: {domResult}");

            // Step 11: Bot detection result (real browser reports base + 0 from reduce)
            string botResult = botBase.ToString();
            Debug($"Bot result: {botResult}");

            // Step 12: Get user agent (must match the HTTP header for consistency)
            string userAgent = ChromeUserAgent;
            if (_httpClient.DefaultRequestHeaders.TryGetValues("User-Agent", out var uaValues))
            {
                var ua = uaValues.FirstOrDefault();
                if (!string.IsNullOrEmpty(ua))
                    userAgent = ua;
            }

            // Step 13: Compute the debug field (XOR of "{}" with the challenge XOR key)
            string debugField = ComputeDebugField(xorKey);

            // Step 14: Hash client fingerprint values with SHA-256
            var clientHashes = new[]
            {
                Sha256Base64(userAgent),
                Sha256Base64(domResult),
                Sha256Base64(botResult)
            };

            // Step 15: Assemble the final challenge response
            var result = new
            {
                server_hashes = serverHashes.ToArray(),
                client_hashes = clientHashes,
                signals = new Dictionary<string, object>(),
                meta = new
                {
                    v = VqdProtocolVersion,
                    challenge_id = challengeId,
                    timestamp,
                    debug = debugField,
                    origin = BaseUrl,
                    stack = BuildStackTrace(),
                    duration = VqdDuration
                }
            };

            var json = JsonSerializer.Serialize(result);
            var solvedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            Debug($"Solved VQD token length: {solvedBase64.Length}");
            return solvedBase64;
        }
        catch (Exception ex)
        {
            Debug($"Error solving VQD challenge: {ex.Message}");
            Debug($"Stack: {ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Computes a SHA-256 hash of the input string and returns it as a base64-encoded string.
    /// Used for hashing client fingerprint values in the VQD challenge response.
    /// </summary>
    private static string Sha256Base64(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Extracts the string array literal from the obfuscated challenge JS.
    /// The challenge may contain multiple array functions (e.g., the rotation
    /// array and a secondary/decoy array). This method identifies the ROTATION
    /// array by finding the function name passed to the rotation IIFE, then
    /// extracting that specific function's array contents.
    /// Falls back to generic patterns if the targeted approach fails.
    /// Unescapes \xNN hex escapes and standard backslash escapes.
    /// </summary>
    /// <summary>
    /// Finds the content between a '[' at startIndex and its matching ']',
    /// correctly skipping over ']' characters inside single-quoted string literals.
    /// This is critical because arrays often contain strings like '[object Window]'
    /// or '[native code]' whose embedded ']' would prematurely terminate a naive regex.
    /// </summary>
    private string? ExtractBracketedContent(string js, int openBracketIndex)
    {
        if (openBracketIndex < 0 || openBracketIndex >= js.Length || js[openBracketIndex] != '[')
            return null;

        int depth = 0;
        bool inString = false;
        for (int i = openBracketIndex; i < js.Length; i++)
        {
            char c = js[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < js.Length)
                {
                    i++; // skip escaped character
                    continue;
                }
                if (c == '\'')
                    inString = false;
                continue;
            }

            if (c == '\'')
            {
                inString = true;
            }
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                    return js.Substring(openBracketIndex + 1, i - openBracketIndex - 1);
            }
        }
        return null;
    }

    private List<string>? ExtractStringArray(string js)
    {
        string? arrayContent = null;

        // BEST approach: identify the rotation array function by name from the IIFE call.
        // The rotation IIFE looks like: }(_0xARRAY_FUNC, 0xTARGET));
        // We need the array from _0xARRAY_FUNC, not any other array in the code.
        var rotationCallMatch = Regex.Match(js, @"\((_0x\w+),\s*0x[0-9a-fA-F]+\)\s*\)\s*;");
        if (rotationCallMatch.Success)
        {
            string rotationFuncName = rotationCallMatch.Groups[1].Value;
            Debug($"Rotation array function identified: {rotationFuncName}");

            // Look for this specific function's array declaration:
            // function _0xNAME(){const _0xNN=[...];_0xNAME=function(){return _0xNN;};...}
            var targetPattern = new Regex(
                @"function\s+" + Regex.Escape(rotationFuncName) +
                @"\s*\(\s*\)\s*\{(?:const|var|let)\s+_0x\w+=\s*\[");
            var targetMatch = targetPattern.Match(js);
            if (targetMatch.Success)
            {
                int bracketPos = targetMatch.Index + targetMatch.Length - 1; // position of '['
                arrayContent = ExtractBracketedContent(js, bracketPos);
                if (arrayContent != null)
                    Debug($"String array found via targeted rotation function lookup");
            }
        }

        // Fallback 1: const/var _0xNNNN=[...];_0xNNNN=function (common pattern)
        if (arrayContent == null)
        {
            var match = Regex.Match(js, @"(?:const|var|let)\s+_0x\w+=\[");
            if (match.Success)
            {
                int bracketPos = match.Index + match.Length - 1;
                var content = ExtractBracketedContent(js, bracketPos);
                if (content != null)
                {
                    // Verify this is followed by ];_0xNNNN=function
                    int endPos = bracketPos + 1 + content.Length + 1; // past the closing ]
                    if (endPos < js.Length && Regex.IsMatch(js.Substring(endPos, Math.Min(30, js.Length - endPos)), @"^;_0x\w+=function"))
                    {
                        arrayContent = content;
                        Debug($"String array found via pattern 1 (const/var declaration)");
                    }
                }
            }
        }

        // Fallback 2: function _0xNNNN(){...=[...]} (any function-wrapped array)
        if (arrayContent == null)
        {
            var match = Regex.Match(js, @"function\s+_0x\w+\(\)\s*\{\s*(?:const|var|let)\s+_0x\w+=\s*\[");
            if (match.Success)
            {
                int bracketPos = match.Index + match.Length - 1;
                arrayContent = ExtractBracketedContent(js, bracketPos);
                if (arrayContent != null)
                    Debug($"String array found via pattern 2 (function-wrapped)");
            }
        }

        // Fallback 3: const/var _0xNNNN=[...] (without adjacent function - broader match)
        if (arrayContent == null)
        {
            var match = Regex.Match(js, @"(?:const|var|let)\s+_0x\w+=\[");
            if (match.Success)
            {
                int bracketPos = match.Index + match.Length - 1;
                arrayContent = ExtractBracketedContent(js, bracketPos);
                if (arrayContent != null)
                    Debug($"String array found via pattern 3 (const/var declaration)");
            }
        }

        // Fallback 4: first large array of quoted strings (>10 elements)
        // Scan for any '[' followed by many single-quoted strings
        if (arrayContent == null)
        {
            int searchFrom = 0;
            while (searchFrom < js.Length)
            {
                int bracketPos = js.IndexOf('[', searchFrom);
                if (bracketPos < 0) break;

                var content = ExtractBracketedContent(js, bracketPos);
                if (content != null)
                {
                    // Count quoted strings to see if this is a large string array
                    int quoteCount = Regex.Matches(content, @"'(?:[^'\\]|\\.)*'").Count;
                    if (quoteCount >= 10)
                    {
                        arrayContent = content;
                        Debug($"String array found via pattern 4 (fallback large array scan, {quoteCount} strings)");
                        break;
                    }
                }
                searchFrom = bracketPos + 1;
            }
        }

        if (arrayContent == null)
        {
            Debug("No string array pattern matched the challenge JS");
            return null;
        }

        var items = new List<string>();
        var stringPattern = new Regex(@"'((?:[^'\\]|\\u[0-9a-fA-F]{4}|\\x[0-9a-fA-F]{2}|\\.)*)'");
        foreach (Match m in stringPattern.Matches(arrayContent))
        {
            var val = m.Groups[1].Value;
            // Unescape \x20 style hex escapes
            val = Regex.Replace(val, @"\\x([0-9a-fA-F]{2})",
                em => ((char)Convert.ToInt32(em.Groups[1].Value, 16)).ToString());
            // Unescape \u003c style unicode escapes
            val = Regex.Replace(val, @"\\u([0-9a-fA-F]{4})",
                em => ((char)Convert.ToInt32(em.Groups[1].Value, 16)).ToString());
            // Unescape standard JS escapes
            val = val.Replace("\\'", "'").Replace("\\\\", "\\");
            items.Add(val);
        }

        return items;
    }

    /// <summary>
    /// Solves the string array rotation by brute-forcing all possible rotations
    /// and evaluating the checksum expression until it matches the target value.
    /// The checksum is a series of parseInt terms with arithmetic operations
    /// extracted from the obfuscated JS.
    /// </summary>
    /// <param name="js">The full challenge JavaScript source.</param>
    /// <param name="originalArray">The extracted (unrotated) string array.</param>
    /// <param name="target">The target checksum value to match.</param>
    /// <returns>The correctly rotated string array, or null if no rotation matches.</returns>
    private List<string>? SolveRotation(string js, List<string> originalArray, int target)
    {
        // Extract the checksum expression (a series of parseInt calls with arithmetic)
        var checksumMatch = Regex.Match(js, @"const _0x\w+=(.+?);if\(_0x\w+===_0x\w+\)break;");
        if (!checksumMatch.Success)
        {
            Debug("Failed to extract checksum expression");
            return null;
        }

        var checksumExpr = checksumMatch.Groups[1].Value;

        // Parse each parseInt term: parseInt(_0xNNNN(0xMM))/0xN with optional multiplier
        var termPattern = new Regex(
            @"([+-]?)parseInt\(_0x\w+\(0x([0-9a-fA-F]+)\)\)/0x([0-9a-fA-F]+)(?:\*\(([+-]?)parseInt\(_0x\w+\(0x([0-9a-fA-F]+)\)\)/0x([0-9a-fA-F]+)\))?");

        var terms = new List<(int sign1, int idx1, int div1, int sign2, int idx2, int div2, bool hasMultiplier)>();
        foreach (Match m in termPattern.Matches(checksumExpr))
        {
            int sign1 = m.Groups[1].Value == "-" ? -1 : 1;
            int idx1 = Convert.ToInt32(m.Groups[2].Value, 16);
            int div1 = Convert.ToInt32(m.Groups[3].Value, 16);
            bool hasMultiplier = m.Groups[4].Success && !string.IsNullOrEmpty(m.Groups[5].Value);
            int sign2 = 0, idx2 = 0, div2 = 1;
            if (hasMultiplier)
            {
                sign2 = m.Groups[4].Value == "-" ? -1 : 1;
                idx2 = Convert.ToInt32(m.Groups[5].Value, 16);
                div2 = Convert.ToInt32(m.Groups[6].Value, 16);
            }
            terms.Add((sign1, idx1, div1, sign2, idx2, div2, hasMultiplier));
        }

        Debug($"Parsed {terms.Count} checksum terms");

        // Determine the base offset for building temporary lookup tables
        var arr = new List<string>(originalArray);
        int baseOffset = 0;
        var baseOffsetMatch = Regex.Match(js, @"=_0x\w+-0x([0-9a-fA-F]+);");
        if (baseOffsetMatch.Success)
            baseOffset = Convert.ToInt32(baseOffsetMatch.Groups[1].Value, 16);

        // Try every possible rotation until the checksum matches.
        // IMPORTANT: Use double (floating-point) arithmetic to match JavaScript's
        // Number type. Using integer division would truncate remainders and produce
        // incorrect checksums when terms don't divide evenly.
        for (int rotation = 0; rotation < arr.Count; rotation++)
        {
            var tempLookup = new Dictionary<int, string>();
            for (int i = 0; i < arr.Count; i++)
                tempLookup[baseOffset + i] = arr[i];

            try
            {
                double checksum = 0;
                bool valid = true;
                foreach (var term in terms)
                {
                    if (!tempLookup.ContainsKey(term.idx1))
                    { valid = false; break; }

                    double val1 = (double)JsParseInt(tempLookup[term.idx1]) / term.div1;

                    if (term.hasMultiplier)
                    {
                        if (!tempLookup.ContainsKey(term.idx2))
                        { valid = false; break; }
                        double val2 = (double)JsParseInt(tempLookup[term.idx2]) / term.div2;
                        checksum += term.sign1 * val1 * (term.sign2 * val2);
                    }
                    else
                    {
                        checksum += term.sign1 * val1;
                    }
                }

                if (valid && Math.Abs(checksum - target) < 0.5)
                {
                    Debug($"Found correct rotation at position {rotation} (checksum={checksum})");
                    return arr;
                }
            }
            catch { /* invalid rotation, continue to next */ }

            // Rotate: move first element to end
            var first = arr[0];
            arr.RemoveAt(0);
            arr.Add(first);
        }

        Debug("Could not solve rotation");
        return null;
    }

    /// <summary>
    /// JavaScript-style parseInt: parses leading digits, ignores trailing non-numeric characters.
    /// e.g., "646785NQdnvn" -> 646785
    /// </summary>
    private static long JsParseInt(string s)
    {
        var match = Regex.Match(s, @"^(\d+)");
        return match.Success ? long.Parse(match.Groups[1].Value) : 0;
    }

    /// <summary>
    /// Extracts the server_hashes array from the challenge JS return statement.
    /// Each element is either a lookup function call or a literal string.
    /// </summary>
    private List<string> ExtractServerHashes(string js, Dictionary<int, string> lookup)
    {
        var hashes = new List<string>();

        var returnMatch = Regex.Match(js, @"'server_hashes'\s*:\s*\[([^\]]+)\]");
        if (!returnMatch.Success)
            return hashes;

        var content = returnMatch.Groups[1].Value;

        // Match either a lookup call _0xNN(0xMM) or a literal 'string'
        var elemPattern = new Regex(@"_0x\w+\(0x([0-9a-fA-F]+)\)|'([^']*)'");
        foreach (Match m in elemPattern.Matches(content))
        {
            if (!string.IsNullOrEmpty(m.Groups[1].Value))
            {
                int idx = Convert.ToInt32(m.Groups[1].Value, 16);
                if (lookup.TryGetValue(idx, out var value))
                    hashes.Add(value);
            }
            else if (m.Groups[2].Success)
            {
                hashes.Add(m.Groups[2].Value);
            }
        }

        return hashes;
    }

    /// <summary>
    /// Extracts the challenge_id value, which may be a literal string or a lookup call.
    /// </summary>
    private static string ExtractChallengeId(string js, Dictionary<int, string> lookup)
    {
        var literalMatch = Regex.Match(js, @"'challenge_id'\s*:\s*'([^']+)'");
        if (literalMatch.Success)
            return literalMatch.Groups[1].Value;

        var lookupMatch = Regex.Match(js, @"'challenge_id'\s*:\s*_0x\w+\(0x([0-9a-fA-F]+)\)");
        if (lookupMatch.Success)
        {
            int cidIdx = Convert.ToInt32(lookupMatch.Groups[1].Value, 16);
            if (lookup.TryGetValue(cidIdx, out var value))
                return value;
        }

        return "";
    }

    /// <summary>
    /// Extracts the timestamp value from the challenge JS.
    /// Tries: 1) literal string in the return statement, 2) lookup call, 3) scan lookup for timestamp-like value.
    /// </summary>
    private string ExtractTimestamp(string js, Dictionary<int, string> lookup)
    {
        // Try 1: literal string timestamp (e.g., 'timestamp':'1771183251718')
        var literalMatch = Regex.Match(js, @"'timestamp'\s*:\s*'(\d{13,})'");
        if (literalMatch.Success)
            return literalMatch.Groups[1].Value;

        // Try 2: lookup call (e.g., 'timestamp':_0xNN(0xMM))
        var lookupMatch = Regex.Match(js, @"'timestamp'\s*:\s*_0x\w+\(0x([0-9a-fA-F]+)\)");
        if (lookupMatch.Success)
        {
            int tsIdx = Convert.ToInt32(lookupMatch.Groups[1].Value, 16);
            if (lookup.TryGetValue(tsIdx, out var value))
                return value;
            Debug($"Timestamp lookup index 0x{tsIdx:x} not found in lookup table (table has {lookup.Count} entries, range 0x{lookup.Keys.Min():x}-0x{lookup.Keys.Max():x})");
        }

        // Try 3: scan the lookup table for a 13-digit timestamp value
        var tsCandidate = lookup.Values.FirstOrDefault(v => v.Length == 13 && long.TryParse(v, out var ts) && ts > 1700000000000);
        if (tsCandidate != null)
        {
            Debug($"Timestamp found by scanning lookup table: {tsCandidate}");
            return tsCandidate;
        }

        return "";
    }

    /// <summary>
    /// Extracts the XOR key used for the debug field computation.
    /// The key is assigned after the empty signals object: ={},_0xNN=_0xLOOKUP(0xMM);
    /// </summary>
    private static string ExtractXorKey(string js, Dictionary<int, string> lookup)
    {
        // Primary pattern: after empty signals object
        var match = Regex.Match(js, @"=\{\}\s*,\s*_0x\w+=_0x\w+\(0x([0-9a-fA-F]+)\)\s*;");
        if (!match.Success)
        {
            // Fallback: immediately before for loop
            match = Regex.Match(js, @",_0x\w+=_0x\w+\(0x([0-9a-fA-F]+)\);for\(");
        }

        if (match.Success)
        {
            int xorIdx = Convert.ToInt32(match.Groups[1].Value, 16);
            if (lookup.TryGetValue(xorIdx, out var value))
                return value;
        }

        return "";
    }

    /// <summary>
    /// Extracts the bot detection base offset from a .reduce(..., 0xNNNN) expression.
    /// </summary>
    private static int ExtractBotBase(string js)
    {
        var match = Regex.Match(js, @"\.reduce\([^,]+,0x([0-9a-fA-F]+)\)");
        if (!match.Success)
            match = Regex.Match(js, @",0x([0-9a-fA-F]+)\)\);\}\(\)\)");

        return match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : 0;
    }

    /// <summary>
    /// Computes a DOM fingerprint by parsing malformed HTML through AngleSharp
    /// (an HTML5-compliant parser) to match Chrome's DOM normalization behavior.
    /// The fingerprint is: String(domBase + innerHTML.Length * querySelectorAll('*').length)
    /// </summary>
    private string ComputeDomFingerprint(string malformedHtml, int domBase)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var parser = context.GetService<IHtmlParser>() ?? new HtmlParser();

        var doc = parser.ParseDocument("<html><body><div></div></body></html>");
        var div = doc.QuerySelector("div")!;
        div.InnerHtml = malformedHtml;

        var normalizedHtml = div.InnerHtml;
        int elementCount = div.QuerySelectorAll("*").Length;

        Debug($"Normalized HTML: '{normalizedHtml}' (length: {normalizedHtml.Length}, elements: {elementCount})");

        int result = domBase + normalizedHtml.Length * elementCount;
        return result.ToString();
    }

    /// <summary>
    /// Computes the debug field by XOR-ing "{}" (JSON.stringify of empty signals object)
    /// with the XOR key extracted from the challenge.
    /// </summary>
    /// <summary>
    /// Builds a realistic browser-style stack trace for the VQD challenge response.
    /// Uses the extracted webpack bundle hash if available, with realistic column numbers.
    /// </summary>
    private string BuildStackTrace()
    {
        var fileName = !string.IsNullOrEmpty(_webpackHash)
            ? $"entry.duckai.{_webpackHash}.js"
            : "entry.duckai.js";

        // Use realistic column numbers that match minified webpack bundle offsets
        int col1 = 1000000 + _random.Next(200000);
        int col2 = 1000000 + _random.Next(200000);
        return $"Error\nat l ({BaseUrl}/dist/duckai-dist/{fileName}:2:{col1})\nat async {BaseUrl}/dist/duckai-dist/{fileName}:2:{col2}";
    }

    private static string ComputeDebugField(string xorKey)
    {
        if (string.IsNullOrEmpty(xorKey))
            return "";

        const string plaintext = "{}";
        var result = new StringBuilder(plaintext.Length);
        for (int i = 0; i < plaintext.Length; i++)
        {
            char xored = (char)(plaintext[i] ^ xorKey[i % xorKey.Length]);
            result.Append(xored);
        }
        return result.ToString();
    }

    #endregion

    /// <summary>
    /// Sends a chat message and streams the response via Server-Sent Events (SSE).
    /// Each response chunk triggers the <see cref="OnResponseChunk"/> event, and the
    /// complete response triggers <see cref="OnResponseComplete"/>.
    /// The VQD token is automatically refreshed from the response headers.
    /// If the server returns HTTP 418 (session expired), the client automatically
    /// re-initializes the session and retries the message once.
    /// </summary>
    /// <param name="message">The user's message to send.</param>
    /// <param name="model">
    /// Optional model override. If null, uses the currently active model.
    /// Must be one of <see cref="AvailableModels"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">Thrown if no VQD token is available.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="message"/> is null or empty.</exception>
    /// <exception cref="HttpRequestException">Thrown if the API returns an error status code after retry.</exception>
    public async Task StreamMessageAsync(string message, string? model = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be null or empty.", nameof(message));

        Debug("\n=== Sending Message ===");
        Debug($"Message: {message}");

        if (!_isInitialized)
        {
            Debug("Not initialized, initializing first...");
            await InitializeAsync();
        }

        if (string.IsNullOrEmpty(_vqdToken))
        {
            Debug("No VQD token available");
            throw new InvalidOperationException("Failed to obtain VQD token. Cannot proceed with chat.");
        }

        var selectedModel = model ?? _activeModel;
        Debug($"Using model: {selectedModel}");

        _conversationHistory.Add(new ChatMessage { Role = "user", Content = message, Timestamp = DateTime.UtcNow });

        var failedProxies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_currentProxyAddress != null) failedProxies.Add(_currentProxyAddress);

        bool sent = false;
        while (!sent)
        {
            try
            {
                await SendChatRequestAsync(selectedModel);
                if (_currentProxyAddress != null)
                    _proxyPool?.ReportProxySuccess(_currentProxyAddress);
                sent = true;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == (HttpStatusCode)418)
            {
                // HTTP 418 means the VQD token is invalid/expired.
                // Strategy: lightweight refresh → full reconnect → proxy rotation.
                Debug("Got HTTP 418 (stale VQD token). Waiting before retry to avoid rate limit...");
                await Task.Delay(2000);

                Debug("Attempting lightweight VQD refresh...");
                bool recovered = false;
                try
                {
                    if (await FetchStatusAsync())
                    {
                        Debug("Lightweight VQD refresh succeeded. Retrying message...");
                        await SendChatRequestAsync(selectedModel);
                        if (_currentProxyAddress != null)
                            _proxyPool?.ReportProxySuccess(_currentProxyAddress);
                        recovered = true;
                        sent = true;
                    }
                }
                catch (HttpRequestException retryEx) when (retryEx.StatusCode == (HttpStatusCode)418)
                {
                    Debug("Lightweight refresh still got 418. Escalating to full reconnect...");
                }
                catch (Exception retryEx)
                {
                    Debug($"Lightweight refresh failed: {retryEx.Message}. Escalating to full reconnect...");
                }

                if (!recovered)
                {
                    Debug("Waiting before full reconnect...");
                    await Task.Delay(3000);

                    Debug("Performing full session reconnect...");
                    OnError?.Invoke(this, new InvalidOperationException("Session expired (HTTP 418). Reconnecting..."));

                    try
                    {
                        var reconnected = await ReconnectAsync();
                        if (!reconnected || string.IsNullOrEmpty(_vqdToken))
                        {
                            throw new InvalidOperationException(
                                "Full reconnect failed: could not obtain a new VQD token. " +
                                "Try again later or use the 'reconnect' command.");
                        }
                        Debug("Full reconnect succeeded. Retrying message...");
                        await SendChatRequestAsync(selectedModel);
                        sent = true;
                    }
                    catch (Exception reconnectEx)
                    {
                        // Full reconnect failed. If we have a proxy pool, rotate to a fresh proxy and retry.
                        if (_proxyPool != null)
                        {
                            Debug($"Full reconnect failed ({reconnectEx.Message}). Rotating proxy...");
                            if (_currentProxyAddress != null)
                            {
                                _proxyPool.ReportProxyFailure(_currentProxyAddress);
                                failedProxies.Add(_currentProxyAddress);
                            }

                            if (RotateProxy(failedProxies))
                            {
                                OnError?.Invoke(this, new InvalidOperationException("418 — proxy flagged, rotating to next proxy..."));
                                Debug("Re-initializing through new proxy after 418...");
                                if (await InitializeAsync())
                                {
                                    continue; // retry the while (!sent) loop with new proxy
                                }
                                Debug("Re-initialization failed on new proxy, trying another...");
                                continue;
                            }
                        }

                        if (_conversationHistory.Count > 0 &&
                            _conversationHistory[^1].Role == "user" &&
                            _conversationHistory[^1].Content == message)
                        {
                            _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
                        }

                        Debug($"Full reconnect also failed: {reconnectEx.Message}");
                        OnError?.Invoke(this, reconnectEx);
                        throw;
                    }
                }
            }
            catch (Exception ex) when (_proxyPool != null &&
                (ex is HttpRequestException { StatusCode: null } ||
                 ex is TaskCanceledException ||
                 ex.InnerException is System.Net.Sockets.SocketException))
            {
                // Network-level failure — the proxy is likely dead. Try the next one.
                Debug($"Proxy error ({ex.Message}). Marking proxy failed and rotating...");
                if (_currentProxyAddress != null)
                {
                    _proxyPool.ReportProxyFailure(_currentProxyAddress);
                    failedProxies.Add(_currentProxyAddress);
                }

                if (!RotateProxy(failedProxies))
                {
                    // No more proxies — let the error surface
                    if (_conversationHistory.Count > 0 &&
                        _conversationHistory[^1].Role == "user" &&
                        _conversationHistory[^1].Content == message)
                    {
                        _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
                    }
                    OnError?.Invoke(this, ex);
                    throw;
                }

                // Re-initialize through the new proxy before retrying
                Debug("Re-initializing session through new proxy...");
                OnError?.Invoke(this, new InvalidOperationException($"Proxy failed, rotating to next proxy..."));
                if (!await InitializeAsync())
                {
                    Debug("Re-initialization failed on new proxy. Trying another...");
                    continue;
                }
            }
            catch (Exception ex)
            {
                Debug($"Error during streaming: {ex.Message}");
                Debug($"Stack trace: {ex.StackTrace}");
                OnError?.Invoke(this, ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Re-initializes the session by clearing stale state and performing the full
    /// authentication flow again. Call this if you encounter persistent errors,
    /// or it will be called automatically on HTTP 418 responses.
    /// Conversation history is preserved across reconnections.
    /// </summary>
    /// <returns>True if reconnection succeeded; false otherwise.</returns>
    public async Task<bool> ReconnectAsync()
    {
        Debug("=== Reconnecting (full session re-initialization) ===");
        _isInitialized = false;
        _vqdToken = null;
        _feVersion = null;

        // Clear cookies to start a completely fresh session
        // (CookieContainer doesn't have a Clear method, so we recreate via the handler)
        foreach (Cookie cookie in _cookieContainer.GetCookies(new Uri(BaseUrl)))
            cookie.Expired = true;
        foreach (Cookie cookie in _cookieContainer.GetCookies(new Uri(DuckDuckGoUrl)))
            cookie.Expired = true;

        return await InitializeAsync();
    }

    /// <summary>
    /// Builds and sends the chat HTTP request, parses the SSE stream, and fires chunk/complete events.
    /// This is the inner method called by <see cref="StreamMessageAsync"/> and its retry logic.
    /// </summary>
    private async Task SendChatRequestAsync(string selectedModel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{ChatEndpoint}");

        // Set browser-like fetch headers and VQD token
        request.Headers.Add("Origin", BaseUrl);
        request.Headers.Add("Referer", $"{BaseUrl}/");
        request.Headers.Add("x-vqd-hash-1", _vqdToken);
        request.Headers.Add("Accept", "text/event-stream");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        request.Headers.Add("Sec-Fetch-Mode", "cors");
        request.Headers.Add("Sec-Fetch-Dest", "empty");
        if (!string.IsNullOrEmpty(_feVersion))
            request.Headers.Add("x-fe-version", _feVersion);

        // Generate x-fe-signals header with realistic per-request timing values.
        // A real browser sends different event names and timing deltas each request.
        _messageCount++;
        var signalEventName = _messageCount == 1 ? "startNewChat_free" : "sendMessage_free";
        var signalDelta = 80 + _random.Next(100);   // realistic range: 80-180ms
        var signalEnd = 2500 + _random.Next(2000);   // realistic range: 2500-4500ms
        var signals = new
        {
            start = _sessionStartMs,
            events = new[] { new { name = signalEventName, delta = signalDelta } },
            end = signalEnd
        };
        var signalsJson = JsonSerializer.Serialize(signals);
        var signalsBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signalsJson));
        request.Headers.Add("x-fe-signals", signalsBase64);

        // Build conversation payload (matches real browser traffic)
        var payload = new
        {
            model = selectedModel,
            metadata = new
            {
                toolChoice = new
                {
                    NewsSearch = false,
                    VideosSearch = false,
                    LocalSearch = false,
                    WeatherForecast = false
                }
            },
            messages = BuildMessagesArray(),
            canUseTools = true,
            canUseApproxLocation = (object?)null
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Debug($"Request payload: {json}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        DebugRequest(request);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await DebugResponseAsync(response, skipContent: true);

        // Refresh VQD token from response headers (each response includes a new challenge)
        RefreshVqdToken(response);

        // Let 418 propagate to the caller for retry handling
        response.EnsureSuccessStatusCode();

        // Parse the SSE stream line by line
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var fullResponse = new StringBuilder();
        string? line;
        int chunkCount = 0;

        Debug("Starting to read response stream...");

        while ((line = await reader.ReadLineAsync()) != null)
        {
            Debug($"Raw stream line {++chunkCount}: '{line}'");

            if (!line.StartsWith(SseDataPrefix))
                continue;

            var data = line.Substring(SseDataPrefix.Length);
            Debug($"Parsed data: '{data}'");

            if (data == SseDoneMarker)
            {
                Debug("Received [DONE] marker");
                var completeResponse = fullResponse.ToString();
                _conversationHistory.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = completeResponse,
                    Timestamp = DateTime.UtcNow
                });
                Debug($"Complete response ({completeResponse.Length} chars)");
                OnResponseComplete?.Invoke(this, completeResponse);
                break;
            }

            // Filter out protocol markers that are not chat content.
            // These appear as non-JSON SSE data lines and must NOT be included
            // in the response or conversation history, as they would corrupt
            // subsequent API requests.
            if (data.StartsWith("[") && data.EndsWith("]") && !data.StartsWith("{"))
            {
                Debug($"Skipping protocol marker: '{data}'");
                continue;
            }

            try
            {
                var chunkData = JsonSerializer.Deserialize<ChatChunk>(data);
                if (!string.IsNullOrEmpty(chunkData?.Message))
                {
                    Debug($"Chunk content: '{chunkData.Message}'");
                    fullResponse.Append(chunkData.Message);
                    OnResponseChunk?.Invoke(this, chunkData.Message);
                }
                if (!string.IsNullOrEmpty(chunkData?.FinishReason))
                {
                    Debug($"Finish reason: {chunkData.FinishReason}");
                }
            }
            catch (JsonException ex)
            {
                // If it's not valid JSON and not a known marker, log it but do NOT
                // append to the response — unknown data could corrupt conversation history.
                Debug($"Skipping non-JSON SSE data: '{data}' (parse error: {ex.Message})");
            }
        }

        Debug($"=== Message Complete === (total chunks: {chunkCount})\n");
    }

    /// <summary>
    /// Attempts to solve a new VQD challenge from the response headers and update the stored token.
    /// Called after every chat response to keep the token fresh for the next request.
    /// </summary>
    private void RefreshVqdToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("x-vqd-hash-1", out var newVqdTokens))
            return;

        var newChallenge = newVqdTokens.FirstOrDefault();
        if (string.IsNullOrEmpty(newChallenge) || newChallenge == _vqdToken)
            return;

        Debug("Solving new VQD challenge from response...");
        var solved = SolveVqdChallenge(newChallenge);
        if (!string.IsNullOrEmpty(solved))
        {
            _vqdToken = solved;
            Debug("VQD token updated with solved challenge");
        }
        else
        {
            Debug("Warning: Could not solve response VQD challenge, keeping old token");
        }
    }

    /// <summary>
    /// Builds the messages array for the chat API payload from conversation history.
    /// </summary>
    private object[] BuildMessagesArray()
    {
        var messages = new List<object>();
        foreach (var msg in _conversationHistory)
        {
            messages.Add(new { role = msg.Role, content = msg.Content });
            Debug($"Adding to history: {msg.Role} - {msg.Content[..Math.Min(50, msg.Content.Length)]}...");
        }
        return messages.ToArray();
    }

    /// <summary>
    /// Sends a message and waits for the complete response (non-streaming convenience method).
    /// Internally uses <see cref="StreamMessageAsync"/> and collects the result.
    /// </summary>
    /// <param name="message">The user's message to send.</param>
    /// <param name="model">Optional model override.</param>
    /// <returns>The complete AI response text.</returns>
    public async Task<string> SendMessageAsync(string message, string? model = null)
    {
        var completionTcs = new TaskCompletionSource<string>();

        void OnComplete(object? s, string response)
        {
            completionTcs.TrySetResult(response);
        }

        OnResponseComplete += OnComplete;

        try
        {
            await StreamMessageAsync(message, model);
            return await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(SendMessageTimeoutSeconds));
        }
        finally
        {
            OnResponseComplete -= OnComplete;
        }
    }

    /// <summary>
    /// Replaces the conversation history with the supplied messages, exactly as if those
    /// turns had been sent through the normal API. Useful for testing or restoring a
    /// previously exported session. The messages are sent in the provided order with
    /// every subsequent chat request.
    /// </summary>
    /// <param name="messages">Alternating user / assistant turns to inject.</param>
    public void SeedConversation(IEnumerable<ChatMessage> messages)
    {
        _conversationHistory.Clear();
        foreach (var m in messages)
            _conversationHistory.Add(m);
        Debug($"Seeded conversation with {_conversationHistory.Count} messages");
    }

    /// <summary>
    /// Clears the conversation history, starting a fresh chat session.
    /// </summary>
    public void ClearConversation()
    {
        Debug("Clearing conversation history");
        _conversationHistory.Clear();
    }

    /// <summary>
    /// Switches the active AI model for subsequent messages.
    /// </summary>
    /// <param name="model">The model identifier. Must be one of <see cref="AvailableModels"/>.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="model"/> is not a recognized model.</exception>
    /// <summary>
    /// Marks the proxy currently in use as failed so the pool deprioritises it.
    /// Call this before reconnecting to force the pool to hand out a fresh proxy.
    /// </summary>
    /// <summary>The proxy address currently in use, or <c>null</c> if not using a proxy.</summary>
    public string? CurrentProxyAddress => _currentProxyAddress;

    public void ReportCurrentProxyAsFailed()
    {
        if (_currentProxyAddress != null)
            _proxyPool?.ReportProxyFailure(_currentProxyAddress);
    }

    public void SetModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model name cannot be null or empty.", nameof(model));

        if (!AvailableModels.Contains(model))
        {
            Debug($"Invalid model attempt: {model}");
            throw new ArgumentException(
                $"Unknown model '{model}'. Available models: {string.Join(", ", AvailableModels)}",
                nameof(model));
        }

        Debug($"Switching model from {_activeModel} to {model}");
        _activeModel = model;
    }

    /// <summary>
    /// Exports the conversation history as a formatted plain-text string.
    /// </summary>
    /// <returns>A formatted string containing all messages with timestamps and roles.</returns>
    public string ExportConversation()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CONVERSATION HISTORY ===");
        sb.AppendLine($"Total messages: {_conversationHistory.Count}");
        sb.AppendLine();

        foreach (var msg in _conversationHistory)
        {
            sb.AppendLine($"[{msg.Timestamp:yyyy-MM-dd HH:mm:ss}] {msg.Role.ToUpper()}:");
            sb.AppendLine(msg.Content);
            sb.AppendLine();
        }

        sb.AppendLine("============================");
        return sb.ToString();
    }

    /// <summary>
    /// Returns a read-only view of the conversation history.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetConversationHistory() => _conversationHistory.AsReadOnly();

    /// <summary>
    /// Disposes of the underlying HTTP client and releases network resources.
    /// </summary>
    public void Dispose()
    {
        Debug("Disposing DuckAIClient");
        _httpClient?.Dispose();
    }

    /// <summary>
    /// Represents a single chunk in the SSE response stream.
    /// </summary>
    private class ChatChunk
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }
}

/// <summary>
/// Represents a single message in a Duck.ai chat conversation.
/// </summary>
public class ChatMessage
{
    /// <summary>The role of the message sender ("user" or "assistant").</summary>
    public string Role { get; set; } = "";

    /// <summary>The text content of the message.</summary>
    public string Content { get; set; } = "";

    /// <summary>The UTC timestamp when the message was created.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// High-level convenience wrapper around <see cref="DuckAIClient"/> that handles
/// initialization automatically and forwards all events. Use this class for the
/// simplest integration path.
/// </summary>
/// <example>
/// <code>
/// using var chat = new DuckAIChat();
/// chat.OnResponseChunk += (s, chunk) => Console.Write(chunk);
/// if (await chat.EnsureInitializedAsync())
/// {
///     await chat.StreamAskAsync("What is DuckDuckGo?");
/// }
/// </code>
/// </example>
public class DuckAIChat : IDisposable
{
    private readonly DuckAIClient _client;
    private readonly Task<bool> _initializationTask;

    /// <summary>Raised when internal debug/diagnostic messages are generated.</summary>
    public event EventHandler<string>? OnDebugOutput;

    /// <summary>Raised for each incremental text chunk received from the AI response stream.</summary>
    public event EventHandler<string>? OnResponseChunk;

    /// <summary>Raised when the full AI response has been received.</summary>
    public event EventHandler<string>? OnResponseComplete;

    /// <summary>Raised when an error occurs during API interaction.</summary>
    public event EventHandler<Exception>? OnError;

    /// <summary>
    /// Creates a new DuckAIChat instance and begins initialization in the background.
    /// Call <see cref="EnsureInitializedAsync"/> before sending messages.
    /// </summary>
    /// <param name="proxyPool">Optional proxy pool to route requests through. The best available proxy will be selected automatically.</param>
    public DuckAIChat(ProxyPool.ProxyEnabledHttpClient? proxyPool = null)
    {
        _client = new DuckAIClient(proxyPool);

        _client.OnDebugOutput += (s, msg) => OnDebugOutput?.Invoke(s, msg);
        _client.OnResponseChunk += (s, chunk) => OnResponseChunk?.Invoke(s, chunk);
        _client.OnResponseComplete += (s, response) => OnResponseComplete?.Invoke(s, response);
        _client.OnError += (s, ex) => OnError?.Invoke(s, ex);

        _initializationTask = _client.InitializeAsync();
    }

    /// <summary>
    /// Waits for the background initialization to complete.
    /// </summary>
    /// <returns>True if initialization succeeded; false otherwise.</returns>
    public async Task<bool> EnsureInitializedAsync()
    {
        return await _initializationTask;
    }

    /// <summary>
    /// Sends a question and returns the complete response (non-streaming).
    /// </summary>
    /// <param name="question">The user's question.</param>
    /// <param name="model">Optional model override.</param>
    /// <returns>The complete AI response text.</returns>
    public async Task<string> AskAsync(string question, string? model = null)
    {
        await EnsureInitializedAsync();
        return await _client.SendMessageAsync(question, model);
    }

    /// <summary>
    /// Sends a question and streams the response via events.
    /// Subscribe to <see cref="OnResponseChunk"/> to receive incremental text.
    /// </summary>
    /// <param name="question">The user's question.</param>
    /// <param name="model">Optional model override.</param>
    public async Task StreamAskAsync(string question, string? model = null)
    {
        await EnsureInitializedAsync();
        await _client.StreamMessageAsync(question, model);
    }

    /// <inheritdoc cref="DuckAIClient.ClearConversation"/>
    public void ClearConversation() => _client.ClearConversation();

    /// <inheritdoc cref="DuckAIClient.SetModel"/>
    public void SetModel(string model) => _client.SetModel(model);

    /// <inheritdoc cref="DuckAIClient.CurrentProxyAddress"/>
    public string? CurrentProxyAddress => _client.CurrentProxyAddress;

    /// <inheritdoc cref="DuckAIClient.ReportCurrentProxyAsFailed"/>
    public void ReportCurrentProxyAsFailed() => _client.ReportCurrentProxyAsFailed();

    /// <inheritdoc cref="DuckAIClient.SeedConversation"/>
    public void SeedConversation(IEnumerable<ChatMessage> messages) => _client.SeedConversation(messages);

    /// <inheritdoc cref="DuckAIClient.ExportConversation"/>
    public string ExportConversation() => _client.ExportConversation();

    /// <inheritdoc cref="DuckAIClient.GetConversationHistory"/>
    public IReadOnlyList<ChatMessage> GetConversationHistory() => _client.GetConversationHistory();

    /// <inheritdoc cref="DuckAIClient.ReconnectAsync"/>
    public Task<bool> ReconnectAsync() => _client.ReconnectAsync();

    /// <summary>
    /// Disposes of the underlying client and releases resources.
    /// </summary>
    public void Dispose() => _client?.Dispose();
}
