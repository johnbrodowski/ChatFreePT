using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ChatFreePT
{
    public partial class Form1 : Form
    {
        // ── Proxy source catalogue ─────────────────────────────────────────────
        private static readonly (string Name, string Url, bool On)[] ProxySources =
        {
            ("TheSpeedX · HTTP",       "https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/http.txt",                                              true),
            ("TheSpeedX · SOCKS4",     "https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/socks4.txt",                                            false),
            ("TheSpeedX · SOCKS5",     "https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/socks5.txt",                                            false),
            ("ShiftyTR · HTTP",        "https://raw.githubusercontent.com/ShiftyTR/Proxy-List/master/http.txt",                                               true),
            ("ShiftyTR · SOCKS4",      "https://raw.githubusercontent.com/ShiftyTR/Proxy-List/master/socks4.txt",                                             false),
            ("ShiftyTR · SOCKS5",      "https://raw.githubusercontent.com/ShiftyTR/Proxy-List/master/socks5.txt",                                             false),
            ("clarketm · HTTP",        "https://raw.githubusercontent.com/clarketm/proxy-list/master/proxy-list-raw.txt",                                     false),
            ("monosans · SOCKS4",      "https://raw.githubusercontent.com/monosans/proxy-list/refs/heads/main/proxies/socks4.txt",                            false),
            ("monosans · SOCKS5",      "https://raw.githubusercontent.com/monosans/proxy-list/refs/heads/main/proxies/socks5.txt",                            false),
            ("monosans anon · SOCKS4", "https://raw.githubusercontent.com/monosans/proxy-list/refs/heads/main/proxies_anonymous/socks4.txt",                  false),
            ("monosans anon · SOCKS5", "https://raw.githubusercontent.com/monosans/proxy-list/refs/heads/main/proxies_anonymous/socks5.txt",                  false),
            ("roosterkid · SOCKS4",    "https://raw.githubusercontent.com/roosterkid/openproxylist/refs/heads/main/SOCKS4_RAW.txt",                           false),
            ("roosterkid · SOCKS5",    "https://raw.githubusercontent.com/roosterkid/openproxylist/refs/heads/main/SOCKS5_RAW.txt",                           false),
        };

        // ── State ──────────────────────────────────────────────────────────────
        private DuckAIChat?                       _chat;
        private DuckAIChat?                       _pendingChat;          // chat being initialised right now
        private DuckAIBrowserClient?              _browserClient;        // active browser session
        private DuckAIBrowserClient?              _pendingBrowserClient; // browser being initialised
        private bool                              _usingBrowser;         // which mode is live
        private int                               _connectGen;           // incremented each connect attempt
        private ProxyPool.ProxyEnabledHttpClient? _proxyPool;
        private CancellationTokenSource?          _proxyCts;
        private CancellationTokenSource?          _connectCts;
        private System.Windows.Forms.Timer?       _proxyTimer;
        private int                               _testedCount;
        private int                               _lastWorkingCount;
        private bool                              _isConnected;
        private bool                              _isSending;

        // ── Constructor ────────────────────────────────────────────────────────
        public Form1()
        {
            InitializeComponent();

            cmbModel.Items.AddRange(DuckAIClient.AvailableModels);
            cmbModel.SelectedIndex = 0;

            foreach (var (name, _, on) in ProxySources)
                clbSources.Items.Add(name, on);

            txtInput.KeyDown += TxtInput_KeyDown;
        }

        // ── Proxy Settings ─────────────────────────────────────────────────────

        private void chkUseProxy_CheckedChanged(object sender, EventArgs e)
        {
            bool on = chkUseProxy.Checked;
            btnFindProxies.Enabled = on && !btnStopProxies.Enabled;
            lblProxyStatus.Text    = on ? "Click \"Find Proxies\" to search." : "Enable proxy above to begin.";
        }

        private async void btnFindProxies_Click(object sender, EventArgs e)
        {
            // Collect checked proxy-list URLs
            var urls = ProxySources
                .Where((_, i) => clbSources.GetItemChecked(i))
                .Select(s => s.Url)
                .ToList();

            if (urls.Count == 0)
            {
                MessageBox.Show("Please tick at least one proxy source.", "No sources selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Read settings controls on the UI thread before entering Task.Run
            int parallel = (int)numParallel.Value;
            int timeout  = (int)numTimeout.Value;
            int delay    = (int)numDelay.Value;

            // Reset UI
            btnFindProxies.Enabled = false;
            btnStopProxies.Enabled = true;
            pbProxy.Style          = ProgressBarStyle.Marquee;
            lblProxiesFound.Text   = "Working: 0 | Tested: 0";
            lblProxyStatus.Text    = "Fetching proxy lists…";
            lstFoundProxies.Items.Clear();
            Interlocked.Exchange(ref _testedCount, 0);
            _lastWorkingCount = 0;

            // Fresh pool with user-chosen settings
            _proxyPool?.Dispose();
            _proxyPool = new ProxyPool.ProxyEnabledHttpClient(
                proxyListUrls:       urls,
                testTimeoutSeconds:  timeout,
                fetchTimeoutSeconds: 20,
                maxParallelTests:    parallel,
                maxRetries:          1,
                allowDirectFallback: false);

            _proxyCts = new CancellationTokenSource();
            StartProxyTimer();

            try
            {
                await Task.Run(async () =>
                    await _proxyPool.DiscoverProxiesAsync(
                        onProgress:            (tested, _) => Interlocked.Exchange(ref _testedCount, tested),
                        delayBetweenStartsMs:  delay,
                        cancellationToken:     _proxyCts.Token)
                    .ConfigureAwait(false));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppendSystem($"Proxy error: {ex.Message}"); }
            finally { StopProxyTimer(); }

            int healthy = _proxyPool.GetStatistics().HealthyProxies;
            pbProxy.Style          = ProgressBarStyle.Continuous;
            pbProxy.Value          = healthy > 0 ? 100 : 0;
            lblProxyStatus.Text    = healthy > 0
                ? $"✓ {healthy} working proxies ready."
                : "✗ No working proxies found.";
            btnFindProxies.Enabled = true;
            btnStopProxies.Enabled = false;
            RefreshProxyList();
        }

        private void btnStopProxies_Click(object sender, EventArgs e)
        {
            _proxyCts?.Cancel();
            btnStopProxies.Enabled = false;
        }

        // ── Proxy progress timer (UI thread, 250 ms) ───────────────────────────

        private void StartProxyTimer()
        {
            _proxyTimer       = new System.Windows.Forms.Timer { Interval = 250 };
            _proxyTimer.Tick += ProxyTimer_Tick;
            _proxyTimer.Start();
        }

        private void StopProxyTimer()
        {
            _proxyTimer?.Stop();
            _proxyTimer?.Dispose();
            _proxyTimer = null;
        }

        private void ProxyTimer_Tick(object? sender, EventArgs e)
        {
            if (_proxyPool == null) return;

            int tested  = Interlocked.CompareExchange(ref _testedCount, 0, 0);
            var stats   = _proxyPool.GetStatistics();
            int working = stats.HealthyProxies;

            lblProxiesFound.Text = $"Working: {working} | Tested: {tested}";
            lblProxyStatus.Text  = $"Testing… {working} found so far.";

            if (working != _lastWorkingCount)
            {
                _lastWorkingCount = working;
                RefreshProxyList();
            }
        }

        private void RefreshProxyList()
        {
            if (_proxyPool == null) return;
            var top = _proxyPool.GetStatistics().TopProxies;
            lstFoundProxies.BeginUpdate();
            lstFoundProxies.Items.Clear();
            foreach (var p in top)
                if (p.SuccessCount > 0)
                    lstFoundProxies.Items.Add($"{p.Address}  [{p.Type}]");
            lstFoundProxies.EndUpdate();
        }

        // ── Connection ─────────────────────────────────────────────────────────

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            int gen = Interlocked.Increment(ref _connectGen);

            SetConnectingState();
            _connectCts?.Cancel();
            _connectCts = new CancellationTokenSource();

            // Dispose whatever was previously active
            var oldChat    = _chat;    _chat    = null; oldChat?.Dispose();
            var oldBrowser = _browserClient; _browserClient = null; oldBrowser?.Dispose();

            bool usingBrowser = chkBrowserMode.Checked;
            var  pool         = chkUseProxy.Checked ? _proxyPool : null;
            var  model        = cmbModel.SelectedItem?.ToString() ?? DuckAIClient.AvailableModels[0];

            DuckAIChat?          newChat    = null;
            DuckAIBrowserClient? newBrowser = null;
            bool ok = false;

            try
            {
                if (usingBrowser)
                {
                    // Pick the best proxy address from the pool (Chrome needs it at launch)
                    string? proxyAddr = null;
                    if (pool != null)
                        proxyAddr = pool.GetBestProxyWebProxy()?.Address?.ToString();

                    newBrowser = new DuckAIBrowserClient(proxyAddr);
                    _pendingBrowserClient = newBrowser;
                    newBrowser.OnResponseChunk    += Browser_OnResponseChunk;
                    newBrowser.OnResponseComplete += Browser_OnResponseComplete;
                    newBrowser.OnError            += Browser_OnError;

                    await Task.Run(async () =>
                        await newBrowser.InitializeAsync().ConfigureAwait(false))
                        .WaitAsync(_connectCts.Token);

                    ok = true;
                }
                else
                {
                    newChat = new DuckAIChat(pool);
                    _pendingChat = newChat;
                    newChat.SetModel(model);
                    newChat.OnResponseChunk    += Chat_OnResponseChunk;
                    newChat.OnResponseComplete += Chat_OnResponseComplete;
                    newChat.OnError            += Chat_OnError;

                    ok = await Task.Run(async () =>
                        await newChat.EnsureInitializedAsync().ConfigureAwait(false))
                        .WaitAsync(_connectCts.Token);
                }

                if (gen != Interlocked.CompareExchange(ref _connectGen, gen, gen))
                { newChat?.Dispose(); newBrowser?.Dispose(); return; }

                _isConnected   = ok;
                _usingBrowser  = usingBrowser;
                _chat          = ok && !usingBrowser ? newChat    : null;
                _browserClient = ok &&  usingBrowser ? newBrowser : null;

                if (ok)
                {
                    SetStatus("● Connected", Color.LimeGreen);
                    AppendSystem($"Connected to Duck.ai ({(usingBrowser ? "Browser" : "API")} mode)");
                }
                else
                {
                    SetStatus("✗ Failed to connect", Color.OrangeRed);
                    AppendSystem("Connection failed. Try different proxies or reconnect.");
                    newChat?.Dispose();
                    newBrowser?.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                if (gen != Interlocked.CompareExchange(ref _connectGen, gen, gen))
                { newChat?.Dispose(); newBrowser?.Dispose(); return; }
                newChat?.Dispose(); newBrowser?.Dispose();
                SetStatus("✗ Cancelled", Color.DimGray);
                AppendSystem("Connection cancelled.");
            }
            catch (Exception ex)
            {
                if (gen != Interlocked.CompareExchange(ref _connectGen, gen, gen))
                { newChat?.Dispose(); newBrowser?.Dispose(); return; }
                newChat?.Dispose(); newBrowser?.Dispose();
                SetStatus("✗ Error", Color.OrangeRed);
                AppendSystem($"Connect error: {ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_pendingChat,          newChat))    _pendingChat          = null;
                if (ReferenceEquals(_pendingBrowserClient, newBrowser)) _pendingBrowserClient = null;

                if (gen == Interlocked.CompareExchange(ref _connectGen, gen, gen))
                {
                    btnConnect.Enabled       = true;
                    btnCancelConnect.Enabled = false;
                    btnReconnect.Enabled     = _isConnected;
                    btnNextProxy.Enabled     = chkUseProxy.Checked && _proxyPool != null
                                              && _proxyPool.GetStatistics().HealthyProxies > 1;
                    // Seed test only works in API mode (browser has no history API)
                    btnSeedTest.Enabled      = _isConnected && !usingBrowser;
                    // Model picker is irrelevant in browser mode (chosen in the UI)
                    cmbModel.Enabled         = !usingBrowser;
                    lblModel.Enabled         = !usingBrowser;
                    // Split messages work via API history seeding — not available in browser
                    chkSplitMessage.Enabled  = !usingBrowser;
                    UpdateSendControls();
                }
            }
        }

        // ── Browser event handlers ──────────────────────────────────────────────

        private void Browser_OnResponseChunk(object? sender, string chunk)
            => BeginInvokeIfRequired(() => AppendChunk(chunk));

        private void Browser_OnResponseComplete(object? sender, string _)
            => BeginInvokeIfRequired(() => { rtbChat.AppendText("\n"); rtbChat.ScrollToCaret(); });

        private void Browser_OnError(object? sender, Exception ex)
            => BeginInvokeIfRequired(() => AppendError(ex.Message));

        private void btnCancelConnect_Click(object sender, EventArgs e)
        {
            _connectCts?.Cancel();
            btnCancelConnect.Enabled = false;
        }

        private void btnNextProxy_Click(object sender, EventArgs e)
        {
            if (_proxyPool == null) return;

            // Report whichever proxy is currently active (connecting or connected).
            if (_usingBrowser)
            {
                var addr = (_pendingBrowserClient ?? _browserClient)?.CurrentProxyAddress;
                if (addr != null) _proxyPool.ReportProxyFailure(addr);
            }
            else
            {
                (_pendingChat ?? _chat)?.ReportCurrentProxyAsFailed();
            }

            // Cancel any in-flight connect so WaitAsync unblocks immediately.
            _connectCts?.Cancel();

            _chat?.Dispose();              _chat                 = null;
            _pendingChat?.Dispose();       _pendingChat          = null;
            _browserClient?.Dispose();     _browserClient        = null;
            _pendingBrowserClient?.Dispose(); _pendingBrowserClient = null;
            _isConnected = false;

            btnConnect_Click(sender, e);
        }

        private async void btnReconnect_Click(object sender, EventArgs e)
        {
            // Browser mode — close and relaunch (same proxy)
            if (_usingBrowser)
            {
                btnConnect_Click(sender, e);
                return;
            }

            if (_chat == null) { btnConnect_Click(sender, e); return; }

            btnReconnect.Enabled = false;
            SetStatus("⟳ Reconnecting…", Color.Silver);

            bool ok = await Task.Run(async () =>
                await _chat.ReconnectAsync().ConfigureAwait(false));

            _isConnected         = ok;
            SetStatus(ok ? "● Connected" : "✗ Reconnect failed",
                      ok ? Color.LimeGreen : Color.OrangeRed);
            btnReconnect.Enabled  = true;
            btnNextProxy.Enabled  = ok && chkUseProxy.Checked && _proxyPool != null;
            UpdateSendControls();
        }

        private void SetConnectingState()
        {
            _isConnected             = false;
            btnConnect.Enabled       = false;
            btnCancelConnect.Enabled = true;
            btnReconnect.Enabled     = false;
            btnNextProxy.Enabled     = chkUseProxy.Checked && _proxyPool != null
                                       && _proxyPool.GetStatistics().HealthyProxies > 1;
            btnSeedTest.Enabled      = false;
            SetStatus("⟳ Connecting…", Color.Silver);
            UpdateSendControls();
        }

        private void chkBrowserMode_CheckedChanged(object sender, EventArgs e)
        {
            bool browser = chkBrowserMode.Checked;
            // Model picker and split messages are API-only features
            cmbModel.Enabled        = !browser;
            lblModel.Enabled        = !browser;
            chkSplitMessage.Enabled = !browser;
            if (browser && chkSplitMessage.Checked)
                chkSplitMessage.Checked = false;
        }

        private void SetStatus(string text, Color color)
        {
            lblConnStatus.ForeColor = color;
            lblConnStatus.Text      = text;
        }

        // ── Chat event handlers (background thread) ────────────────────────────

        private void Chat_OnResponseChunk(object? sender, string chunk)
            => BeginInvokeIfRequired(() => AppendChunk(chunk));

        private void Chat_OnResponseComplete(object? sender, string _)
            => BeginInvokeIfRequired(() => { rtbChat.AppendText("\n"); rtbChat.ScrollToCaret(); });

        private void Chat_OnError(object? sender, Exception ex)
            => BeginInvokeIfRequired(() => AppendError(ex.Message));

        // ── Chat input ─────────────────────────────────────────────────────────

        private void TxtInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                btnSend_Click(sender!, e);
            }
        }

        private async void btnSend_Click(object sender, EventArgs e)
        {
            var message = txtInput.Text.Trim();
            if (string.IsNullOrEmpty(message) || _isSending || !_isConnected || _chat == null)
                return;

            _isSending = true;
            txtInput.Clear();
            UpdateSendControls();

            // Capture UI values before entering Task.Run
            var selectedModel  = cmbModel.SelectedItem?.ToString();
            bool splitEnabled  = chkSplitMessage.Checked;
            int  maxTokens     = (int)numMaxTokens.Value;

            try
            {
                // Browser mode — send directly; no splitting, no history seeding
                if (_usingBrowser && _browserClient != null)
                {
                    AppendUser(message);
                    AppendAiLabel();
                    await Task.Run(async () =>
                        await _browserClient.AskAsync(message).ConfigureAwait(false));
                    return;
                }

                var parts = splitEnabled
                    ? SplitIntoChunks(message, maxTokens)
                    : new List<string> { message };

                if (parts.Count == 1)
                {
                    AppendUser(message);
                    AppendAiLabel();
                    await Task.Run(async () =>
                        await _chat.StreamAskAsync(message, selectedModel)
                                   .ConfigureAwait(false));
                }
                else
                {
                    AppendSystem($"Splitting into {parts.Count} parts (~{maxTokens} tokens each)");

                    // Seed all intermediate parts with canned acknowledgments so we
                    // only need one real round-trip (the final part).
                    var toSeed = _chat.GetConversationHistory().ToList();
                    for (int i = 0; i < parts.Count - 1; i++)
                    {
                        string wrapped = WrapPart(parts[i], i + 1, parts.Count);
                        string ack     = MakeAck(i + 1, parts.Count);
                        toSeed.Add(new ChatMessage { Role = "user",      Content = wrapped, Timestamp = DateTime.UtcNow });
                        toSeed.Add(new ChatMessage { Role = "assistant", Content = ack,     Timestamp = DateTime.UtcNow });
                        AppendUser(wrapped);
                        AppendSeededAi(ack);
                    }
                    _chat.SeedConversation(toSeed);

                    // Only the final part is sent as a real request
                    string lastWrapped = WrapPart(parts[^1], parts.Count, parts.Count);
                    AppendUser(lastWrapped);
                    AppendAiLabel();
                    await Task.Run(async () =>
                        await _chat.StreamAskAsync(lastWrapped, selectedModel)
                                   .ConfigureAwait(false));
                }
            }
            catch (Exception ex)
            {
                AppendError(ex.Message);
            }
            finally
            {
                _isSending = false;
                UpdateSendControls();
            }
        }

        private void chkSplitMessage_CheckedChanged(object sender, EventArgs e)
        {
            numMaxTokens.Enabled  = chkSplitMessage.Checked;
            lblMaxTokens.ForeColor = chkSplitMessage.Checked
                ? System.Drawing.Color.Silver
                : System.Drawing.Color.DimGray;
        }

        // ── Message splitting helpers ──────────────────────────────────────────

        /// <summary>
        /// Splits <paramref name="message"/> into chunks where each chunk is at most
        /// <paramref name="approxTokens"/> tokens long (1 token ≈ 4 characters).
        /// Splits always happen on whitespace so no word is cut mid-way.
        /// </summary>
        private static List<string> SplitIntoChunks(string message, int approxTokens)
        {
            int maxChars = approxTokens * 4;
            var parts    = new List<string>();

            int pos = 0;
            while (pos < message.Length)
            {
                // Remaining text fits in one chunk
                if (pos + maxChars >= message.Length)
                {
                    parts.Add(message[pos..].Trim());
                    break;
                }

                // Walk back from the hard limit to the nearest word boundary
                int end = pos + maxChars;
                while (end > pos && message[end] != ' ' && message[end] != '\n' && message[end] != '\r')
                    end--;

                // No whitespace found — hard cut at the limit
                if (end == pos)
                    end = pos + maxChars;

                parts.Add(message[pos..end].Trim());

                // Advance past the boundary whitespace
                pos = end;
                while (pos < message.Length && char.IsWhiteSpace(message[pos]))
                    pos++;
            }

            return parts;
        }

        /// <summary>
        /// Returns the canned acknowledgment the AI would have sent for an intermediate part.
        /// Pre-seeding this into the history means we skip the real round-trip.
        /// </summary>
        private static string MakeAck(int partNum, int total)
        {
            int remaining = total - partNum;
            return remaining == 1
                ? $"Acknowledged part {partNum} of {total}. Ready for the final part."
                : $"Acknowledged part {partNum} of {total}. Waiting for the remaining {remaining} part(s) before processing.";
        }

        /// <summary>
        /// Wraps a chunk with the multi-part framing the model needs.
        /// </summary>
        private static string WrapPart(string content, int partNum, int total)
        {
            if (partNum == 1)
                return $"[Part {partNum} of {total}] This is a multi-part message. " +
                       $"Please acknowledge each part as it arrives but do not begin " +
                       $"processing or responding to the request until you have received " +
                       $"all {total} parts.\n\n{content}";

            if (partNum == total)
                return $"[Part {partNum} of {total} — FINAL PART] You now have the " +
                       $"complete message. Please process the full request now.\n\n{content}";

            return $"[Part {partNum} of {total}] Please acknowledge and wait for the remaining parts.\n\n{content}";
        }

        private async void btnSeedTest_Click(object sender, EventArgs e)
        {
            if (_chat == null || !_isConnected) return;

            // The pre-filled turns — user says first name early, last name only in the
            // final live message. The AI must recall both to answer correctly.
            var seeded = new[]
            {
                new ChatMessage { Role = "user",      Content = "Hi! My name is Jon." },
                new ChatMessage { Role = "assistant",  Content = "Hey Jon! Great to meet you. What's on your mind?" },
                new ChatMessage { Role = "user",      Content = "Can you help me remember things during our chat?" },
                new ChatMessage { Role = "assistant",  Content = "Absolutely! I'll keep track of anything you share with me." },
            };

            _chat.SeedConversation(seeded);

            // Paint the seeded history in the chat window
            rtbChat.Clear();
            AppendSystem("Seeded 4-turn history — sending live message now…");
            foreach (var m in seeded)
            {
                if (m.Role == "user")
                    AppendUser(m.Content);
                else
                    AppendSeededAi(m.Content);
            }

            // Live message that requires knowledge from turn 1 AND the final user turn
            const string liveMessage = "My last name is Doe. Based on everything I've told you, what is my full name?";

            _isSending = true;
            UpdateSendControls();
            AppendUser(liveMessage);
            AppendAiLabel();

            var selectedModel = cmbModel.SelectedItem?.ToString();
            try
            {
                await Task.Run(async () =>
                    await _chat.StreamAskAsync(liveMessage, selectedModel)
                               .ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                AppendError(ex.Message);
            }
            finally
            {
                _isSending = false;
                UpdateSendControls();
            }
        }

        // Paints a pre-filled AI turn in a slightly dimmed colour so it is visually
        // distinct from live responses.
        private void AppendSeededAi(string text)
        {
            rtbChat.SelectionStart  = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectionColor  = Color.DarkSeaGreen;
            rtbChat.SelectionFont   = new Font(rtbChat.Font, FontStyle.Bold);
            rtbChat.AppendText("\nDuck.ai [seeded]: ");
            rtbChat.SelectionColor  = Color.DarkSeaGreen;
            rtbChat.SelectionFont   = rtbChat.Font;
            rtbChat.AppendText(text + "\n");
            rtbChat.SelectionColor  = Color.White;
            rtbChat.ScrollToCaret();
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            rtbChat.Clear();
            if (!_usingBrowser) _chat?.ClearConversation();
            AppendSystem("Conversation cleared.");
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog
            {
                Filter     = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName   = $"ChatFreePT_{DateTime.Now:yyyyMMdd_HHmmss}",
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                File.WriteAllText(dlg.FileName, _chat?.ExportConversation() ?? rtbChat.Text, Encoding.UTF8);
        }

        // ── UI helpers ─────────────────────────────────────────────────────────

        private void UpdateSendControls()
        {
            bool canSend     = _isConnected && !_isSending;
            btnSend.Enabled  = canSend;
            txtInput.Enabled = canSend;
            if (canSend) txtInput.Focus();
        }

        private void AppendUser(string text)
        {
            rtbChat.SelectionStart  = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectionColor  = Color.DodgerBlue;
            rtbChat.SelectionFont   = new Font(rtbChat.Font, FontStyle.Bold);
            rtbChat.AppendText("\nYou: ");
            rtbChat.SelectionColor  = Color.LightSkyBlue;
            rtbChat.SelectionFont   = rtbChat.Font;
            rtbChat.AppendText(text + "\n");
            rtbChat.ScrollToCaret();
        }

        private void AppendAiLabel()
        {
            rtbChat.SelectionStart  = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectionColor  = Color.MediumSpringGreen;
            rtbChat.SelectionFont   = new Font(rtbChat.Font, FontStyle.Bold);
            rtbChat.AppendText("\nDuck.ai: ");
            rtbChat.SelectionColor  = Color.White;
            rtbChat.SelectionFont   = rtbChat.Font;
            rtbChat.ScrollToCaret();
        }

        private void AppendChunk(string chunk)
        {
            rtbChat.SelectionStart  = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectionColor  = Color.White;
            rtbChat.AppendText(chunk);
            rtbChat.ScrollToCaret();
        }

        private void AppendError(string message)
        {
            rtbChat.SelectionStart  = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectionColor  = Color.OrangeRed;
            rtbChat.AppendText($"\n[Error] {message}\n");
            rtbChat.SelectionColor  = Color.White;
            rtbChat.ScrollToCaret();
        }

        private void AppendSystem(string message)
        {
            rtbChat.SelectionStart  = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectionColor  = Color.DimGray;
            rtbChat.AppendText($"[{message}]\n");
            rtbChat.SelectionColor  = Color.White;
            rtbChat.ScrollToCaret();
        }

        private void BeginInvokeIfRequired(Action action)
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }

        // ── Cleanup ────────────────────────────────────────────────────────────

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopProxyTimer();
            _connectCts?.Cancel();
            _proxyCts?.Cancel();
            _chat?.Dispose();
            _pendingChat?.Dispose();
            _browserClient?.Dispose();
            _pendingBrowserClient?.Dispose();
            _proxyPool?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
