namespace ChatFreePT
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            rtbChat = new RichTextBox();
            chkSplitMessage = new CheckBox();
            lblMaxTokens = new Label();
            numMaxTokens = new NumericUpDown();
            txtInput = new TextBox();
            btnSend = new Button();
            grpProxy = new GroupBox();
            chkUseProxy = new CheckBox();
            lblProxyStatus = new Label();
            pbProxy = new ProgressBar();
            btnFindProxies = new Button();
            btnStopProxies = new Button();
            lblProxiesFound = new Label();
            lstFoundProxies = new ListBox();
            grpSources = new GroupBox();
            clbSources = new CheckedListBox();
            grpSettings = new GroupBox();
            lblParallel = new Label();
            numParallel = new NumericUpDown();
            lblTimeout = new Label();
            numTimeout = new NumericUpDown();
            lblDelay = new Label();
            numDelay = new NumericUpDown();
            grpConnection = new GroupBox();
            lblModel = new Label();
            cmbModel = new ComboBox();
            chkBrowserMode = new CheckBox();
            btnConnect = new Button();
            btnCancelConnect = new Button();
            btnNextProxy = new Button();
            btnReconnect = new Button();
            btnSeedTest = new Button();
            btnClear = new Button();
            btnExport = new Button();
            lblConnStatus = new Label();
            ((System.ComponentModel.ISupportInitialize)numMaxTokens).BeginInit();
            grpProxy.SuspendLayout();
            grpSources.SuspendLayout();
            grpSettings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numParallel).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numTimeout).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numDelay).BeginInit();
            grpConnection.SuspendLayout();
            SuspendLayout();
            // 
            // rtbChat
            // 
            rtbChat.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            rtbChat.BackColor = Color.FromArgb(20, 20, 20);
            rtbChat.BorderStyle = BorderStyle.None;
            rtbChat.Font = new Font("Segoe UI", 10F);
            rtbChat.ForeColor = Color.White;
            rtbChat.Location = new Point(8, 8);
            rtbChat.Name = "rtbChat";
            rtbChat.ReadOnly = true;
            rtbChat.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtbChat.Size = new Size(687, 629);
            rtbChat.TabIndex = 0;
            rtbChat.TabStop = false;
            rtbChat.Text = "";
            // 
            // chkSplitMessage
            // 
            chkSplitMessage.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            chkSplitMessage.BackColor = Color.Transparent;
            chkSplitMessage.ForeColor = Color.Silver;
            chkSplitMessage.Location = new Point(8, 639);
            chkSplitMessage.Name = "chkSplitMessage";
            chkSplitMessage.Size = new Size(152, 20);
            chkSplitMessage.TabIndex = 10;
            chkSplitMessage.Text = "Split large messages";
            chkSplitMessage.UseVisualStyleBackColor = false;
            chkSplitMessage.CheckedChanged += chkSplitMessage_CheckedChanged;
            // 
            // lblMaxTokens
            // 
            lblMaxTokens.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblMaxTokens.BackColor = Color.Transparent;
            lblMaxTokens.ForeColor = Color.DimGray;
            lblMaxTokens.Location = new Point(166, 642);
            lblMaxTokens.Name = "lblMaxTokens";
            lblMaxTokens.Size = new Size(100, 16);
            lblMaxTokens.TabIndex = 11;
            lblMaxTokens.Text = "Max tokens / part:";
            // 
            // numMaxTokens
            // 
            numMaxTokens.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            numMaxTokens.BackColor = Color.FromArgb(30, 30, 30);
            numMaxTokens.Enabled = false;
            numMaxTokens.ForeColor = Color.White;
            numMaxTokens.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            numMaxTokens.Location = new Point(270, 638);
            numMaxTokens.Maximum = new decimal(new int[] { 8000, 0, 0, 0 });
            numMaxTokens.Minimum = new decimal(new int[] { 100, 0, 0, 0 });
            numMaxTokens.Name = "numMaxTokens";
            numMaxTokens.Size = new Size(80, 23);
            numMaxTokens.TabIndex = 12;
            numMaxTokens.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // txtInput
            // 
            txtInput.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtInput.BackColor = Color.FromArgb(45, 45, 48);
            txtInput.BorderStyle = BorderStyle.FixedSingle;
            txtInput.Enabled = false;
            txtInput.Font = new Font("Segoe UI", 10F);
            txtInput.ForeColor = Color.White;
            txtInput.Location = new Point(8, 669);
            txtInput.Multiline = true;
            txtInput.Name = "txtInput";
            txtInput.PlaceholderText = "Type your message… (Enter to send, Shift+Enter for new line)";
            txtInput.Size = new Size(609, 72);
            txtInput.TabIndex = 1;
            // 
            // btnSend
            // 
            btnSend.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSend.BackColor = Color.FromArgb(0, 120, 215);
            btnSend.Enabled = false;
            btnSend.FlatAppearance.BorderSize = 0;
            btnSend.FlatStyle = FlatStyle.Flat;
            btnSend.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnSend.ForeColor = Color.White;
            btnSend.Location = new Point(623, 669);
            btnSend.Name = "btnSend";
            btnSend.Size = new Size(72, 72);
            btnSend.TabIndex = 2;
            btnSend.Text = "Send";
            btnSend.UseVisualStyleBackColor = false;
            btnSend.Click += btnSend_Click;
            // 
            // grpProxy
            // 
            grpProxy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            grpProxy.BackColor = Color.FromArgb(45, 45, 48);
            grpProxy.Controls.Add(chkUseProxy);
            grpProxy.Controls.Add(lblProxyStatus);
            grpProxy.Controls.Add(pbProxy);
            grpProxy.Controls.Add(btnFindProxies);
            grpProxy.Controls.Add(btnStopProxies);
            grpProxy.Controls.Add(lblProxiesFound);
            grpProxy.Controls.Add(lstFoundProxies);
            grpProxy.ForeColor = Color.White;
            grpProxy.Location = new Point(703, 8);
            grpProxy.Name = "grpProxy";
            grpProxy.Size = new Size(392, 230);
            grpProxy.TabIndex = 3;
            grpProxy.TabStop = false;
            grpProxy.Text = "Proxy";
            // 
            // chkUseProxy
            // 
            chkUseProxy.BackColor = Color.Transparent;
            chkUseProxy.ForeColor = Color.White;
            chkUseProxy.Location = new Point(12, 21);
            chkUseProxy.Name = "chkUseProxy";
            chkUseProxy.Size = new Size(100, 20);
            chkUseProxy.TabIndex = 0;
            chkUseProxy.Text = "Use Proxy";
            chkUseProxy.UseVisualStyleBackColor = false;
            chkUseProxy.CheckedChanged += chkUseProxy_CheckedChanged;
            // 
            // lblProxyStatus
            // 
            lblProxyStatus.BackColor = Color.Transparent;
            lblProxyStatus.ForeColor = Color.Silver;
            lblProxyStatus.Location = new Point(12, 43);
            lblProxyStatus.Name = "lblProxyStatus";
            lblProxyStatus.Size = new Size(364, 18);
            lblProxyStatus.TabIndex = 1;
            lblProxyStatus.Text = "Enable proxy above to begin.";
            // 
            // pbProxy
            // 
            pbProxy.Location = new Point(12, 63);
            pbProxy.Name = "pbProxy";
            pbProxy.Size = new Size(364, 14);
            pbProxy.Style = ProgressBarStyle.Continuous;
            pbProxy.TabIndex = 2;
            // 
            // btnFindProxies
            // 
            btnFindProxies.BackColor = Color.FromArgb(0, 120, 215);
            btnFindProxies.Enabled = false;
            btnFindProxies.FlatAppearance.BorderSize = 0;
            btnFindProxies.FlatStyle = FlatStyle.Flat;
            btnFindProxies.ForeColor = Color.White;
            btnFindProxies.Location = new Point(12, 85);
            btnFindProxies.Name = "btnFindProxies";
            btnFindProxies.Size = new Size(110, 28);
            btnFindProxies.TabIndex = 3;
            btnFindProxies.Text = "Find Proxies";
            btnFindProxies.UseVisualStyleBackColor = false;
            btnFindProxies.Click += btnFindProxies_Click;
            // 
            // btnStopProxies
            // 
            btnStopProxies.BackColor = Color.FromArgb(180, 50, 50);
            btnStopProxies.Enabled = false;
            btnStopProxies.FlatAppearance.BorderSize = 0;
            btnStopProxies.FlatStyle = FlatStyle.Flat;
            btnStopProxies.ForeColor = Color.White;
            btnStopProxies.Location = new Point(130, 85);
            btnStopProxies.Name = "btnStopProxies";
            btnStopProxies.Size = new Size(60, 28);
            btnStopProxies.TabIndex = 4;
            btnStopProxies.Text = "Stop";
            btnStopProxies.UseVisualStyleBackColor = false;
            btnStopProxies.Click += btnStopProxies_Click;
            // 
            // lblProxiesFound
            // 
            lblProxiesFound.BackColor = Color.Transparent;
            lblProxiesFound.ForeColor = Color.Silver;
            lblProxiesFound.Location = new Point(12, 119);
            lblProxiesFound.Name = "lblProxiesFound";
            lblProxiesFound.Size = new Size(364, 16);
            lblProxiesFound.TabIndex = 5;
            lblProxiesFound.Text = "Working: 0 | Tested: 0";
            // 
            // lstFoundProxies
            // 
            lstFoundProxies.BackColor = Color.FromArgb(28, 28, 28);
            lstFoundProxies.BorderStyle = BorderStyle.FixedSingle;
            lstFoundProxies.Font = new Font("Consolas", 8F);
            lstFoundProxies.ForeColor = Color.LimeGreen;
            lstFoundProxies.Location = new Point(12, 139);
            lstFoundProxies.Name = "lstFoundProxies";
            lstFoundProxies.Size = new Size(364, 80);
            lstFoundProxies.TabIndex = 6;
            lstFoundProxies.TabStop = false;
            // 
            // grpSources
            // 
            grpSources.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            grpSources.BackColor = Color.FromArgb(45, 45, 48);
            grpSources.Controls.Add(clbSources);
            grpSources.ForeColor = Color.White;
            grpSources.Location = new Point(703, 242);
            grpSources.Name = "grpSources";
            grpSources.Size = new Size(392, 144);
            grpSources.TabIndex = 5;
            grpSources.TabStop = false;
            grpSources.Text = "Proxy Sources";
            // 
            // clbSources
            // 
            clbSources.BackColor = Color.FromArgb(28, 28, 28);
            clbSources.BorderStyle = BorderStyle.FixedSingle;
            clbSources.CheckOnClick = true;
            clbSources.Font = new Font("Segoe UI", 8.5F);
            clbSources.ForeColor = Color.White;
            clbSources.Location = new Point(12, 22);
            clbSources.Name = "clbSources";
            clbSources.Size = new Size(364, 110);
            clbSources.TabIndex = 0;
            // 
            // grpSettings
            // 
            grpSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            grpSettings.BackColor = Color.FromArgb(45, 45, 48);
            grpSettings.Controls.Add(lblParallel);
            grpSettings.Controls.Add(numParallel);
            grpSettings.Controls.Add(lblTimeout);
            grpSettings.Controls.Add(numTimeout);
            grpSettings.Controls.Add(lblDelay);
            grpSettings.Controls.Add(numDelay);
            grpSettings.ForeColor = Color.White;
            grpSettings.Location = new Point(703, 396);
            grpSettings.Name = "grpSettings";
            grpSettings.Size = new Size(392, 100);
            grpSettings.TabIndex = 6;
            grpSettings.TabStop = false;
            grpSettings.Text = "Discovery Settings";
            // 
            // lblParallel
            // 
            lblParallel.BackColor = Color.Transparent;
            lblParallel.ForeColor = Color.Silver;
            lblParallel.Location = new Point(12, 30);
            lblParallel.Name = "lblParallel";
            lblParallel.Size = new Size(54, 16);
            lblParallel.TabIndex = 0;
            lblParallel.Text = "Parallel:";
            // 
            // numParallel
            // 
            numParallel.BackColor = Color.FromArgb(30, 30, 30);
            numParallel.ForeColor = Color.White;
            numParallel.Location = new Point(70, 27);
            numParallel.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
            numParallel.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numParallel.Name = "numParallel";
            numParallel.Size = new Size(66, 23);
            numParallel.TabIndex = 1;
            numParallel.Value = new decimal(new int[] { 200, 0, 0, 0 });
            // 
            // lblTimeout
            // 
            lblTimeout.BackColor = Color.Transparent;
            lblTimeout.ForeColor = Color.Silver;
            lblTimeout.Location = new Point(148, 30);
            lblTimeout.Name = "lblTimeout";
            lblTimeout.Size = new Size(76, 16);
            lblTimeout.TabIndex = 2;
            lblTimeout.Text = "Timeout (s):";
            // 
            // numTimeout
            // 
            numTimeout.BackColor = Color.FromArgb(30, 30, 30);
            numTimeout.ForeColor = Color.White;
            numTimeout.Location = new Point(228, 27);
            numTimeout.Maximum = new decimal(new int[] { 120, 0, 0, 0 });
            numTimeout.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numTimeout.Name = "numTimeout";
            numTimeout.Size = new Size(56, 23);
            numTimeout.TabIndex = 3;
            numTimeout.Value = new decimal(new int[] { 3, 0, 0, 0 });
            // 
            // lblDelay
            // 
            lblDelay.BackColor = Color.Transparent;
            lblDelay.ForeColor = Color.Silver;
            lblDelay.Location = new Point(12, 64);
            lblDelay.Name = "lblDelay";
            lblDelay.Size = new Size(72, 16);
            lblDelay.TabIndex = 4;
            lblDelay.Text = "Delay (ms):";
            // 
            // numDelay
            // 
            numDelay.BackColor = Color.FromArgb(30, 30, 30);
            numDelay.ForeColor = Color.White;
            numDelay.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numDelay.Location = new Point(88, 61);
            numDelay.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            numDelay.Name = "numDelay";
            numDelay.Size = new Size(76, 23);
            numDelay.TabIndex = 5;
            // 
            // grpConnection
            // 
            grpConnection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            grpConnection.BackColor = Color.FromArgb(45, 45, 48);
            grpConnection.Controls.Add(lblModel);
            grpConnection.Controls.Add(cmbModel);
            grpConnection.Controls.Add(chkBrowserMode);
            grpConnection.Controls.Add(btnConnect);
            grpConnection.Controls.Add(btnCancelConnect);
            grpConnection.Controls.Add(btnNextProxy);
            grpConnection.Controls.Add(btnReconnect);
            grpConnection.Controls.Add(btnSeedTest);
            grpConnection.Controls.Add(btnClear);
            grpConnection.Controls.Add(btnExport);
            grpConnection.Controls.Add(lblConnStatus);
            grpConnection.ForeColor = Color.White;
            grpConnection.Location = new Point(703, 507);
            grpConnection.Name = "grpConnection";
            grpConnection.Size = new Size(392, 260);
            grpConnection.TabIndex = 4;
            grpConnection.TabStop = false;
            grpConnection.Text = "Connection";
            // 
            // lblModel
            // 
            lblModel.BackColor = Color.Transparent;
            lblModel.ForeColor = Color.Silver;
            lblModel.Location = new Point(12, 24);
            lblModel.Name = "lblModel";
            lblModel.Size = new Size(44, 16);
            lblModel.TabIndex = 0;
            lblModel.Text = "Model:";
            // 
            // cmbModel
            // 
            cmbModel.BackColor = Color.FromArgb(30, 30, 30);
            cmbModel.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbModel.FlatStyle = FlatStyle.Flat;
            cmbModel.ForeColor = Color.White;
            cmbModel.Location = new Point(12, 44);
            cmbModel.Name = "cmbModel";
            cmbModel.Size = new Size(364, 23);
            cmbModel.TabIndex = 1;
            //
            // chkBrowserMode
            //
            chkBrowserMode.BackColor = Color.Transparent;
            chkBrowserMode.ForeColor = Color.Silver;
            chkBrowserMode.Location = new Point(12, 72);
            chkBrowserMode.Name = "chkBrowserMode";
            chkBrowserMode.Size = new Size(200, 20);
            chkBrowserMode.TabIndex = 2;
            chkBrowserMode.Text = "Browser Mode (Chromium)";
            chkBrowserMode.UseVisualStyleBackColor = false;
            chkBrowserMode.CheckedChanged += chkBrowserMode_CheckedChanged;
            //
            // btnConnect
            //
            btnConnect.BackColor = Color.FromArgb(0, 140, 0);
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.FlatStyle = FlatStyle.Flat;
            btnConnect.ForeColor = Color.White;
            btnConnect.Location = new Point(12, 98);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(88, 30);
            btnConnect.TabIndex = 2;
            btnConnect.Text = "Connect";
            btnConnect.UseVisualStyleBackColor = false;
            btnConnect.Click += btnConnect_Click;
            // 
            // btnCancelConnect
            // 
            btnCancelConnect.BackColor = Color.FromArgb(160, 60, 60);
            btnCancelConnect.Enabled = false;
            btnCancelConnect.FlatAppearance.BorderSize = 0;
            btnCancelConnect.FlatStyle = FlatStyle.Flat;
            btnCancelConnect.ForeColor = Color.White;
            btnCancelConnect.Location = new Point(108, 98);
            btnCancelConnect.Name = "btnCancelConnect";
            btnCancelConnect.Size = new Size(78, 30);
            btnCancelConnect.TabIndex = 3;
            btnCancelConnect.Text = "Cancel";
            btnCancelConnect.UseVisualStyleBackColor = false;
            btnCancelConnect.Click += btnCancelConnect_Click;
            // 
            // btnNextProxy
            // 
            btnNextProxy.BackColor = Color.FromArgb(160, 100, 0);
            btnNextProxy.Enabled = false;
            btnNextProxy.FlatAppearance.BorderSize = 0;
            btnNextProxy.FlatStyle = FlatStyle.Flat;
            btnNextProxy.ForeColor = Color.White;
            btnNextProxy.Location = new Point(194, 98);
            btnNextProxy.Name = "btnNextProxy";
            btnNextProxy.Size = new Size(100, 30);
            btnNextProxy.TabIndex = 4;
            btnNextProxy.Text = "Next Proxy";
            btnNextProxy.UseVisualStyleBackColor = false;
            btnNextProxy.Click += btnNextProxy_Click;
            // 
            // btnReconnect
            // 
            btnReconnect.BackColor = Color.FromArgb(80, 80, 80);
            btnReconnect.Enabled = false;
            btnReconnect.FlatAppearance.BorderSize = 0;
            btnReconnect.FlatStyle = FlatStyle.Flat;
            btnReconnect.ForeColor = Color.White;
            btnReconnect.Location = new Point(12, 136);
            btnReconnect.Name = "btnReconnect";
            btnReconnect.Size = new Size(100, 30);
            btnReconnect.TabIndex = 5;
            btnReconnect.Text = "Reconnect";
            btnReconnect.UseVisualStyleBackColor = false;
            btnReconnect.Click += btnReconnect_Click;
            // 
            // btnSeedTest
            // 
            btnSeedTest.BackColor = Color.FromArgb(80, 50, 120);
            btnSeedTest.Enabled = false;
            btnSeedTest.FlatAppearance.BorderSize = 0;
            btnSeedTest.FlatStyle = FlatStyle.Flat;
            btnSeedTest.ForeColor = Color.White;
            btnSeedTest.Location = new Point(120, 136);
            btnSeedTest.Name = "btnSeedTest";
            btnSeedTest.Size = new Size(100, 30);
            btnSeedTest.TabIndex = 6;
            btnSeedTest.Text = "Seed Test";
            btnSeedTest.UseVisualStyleBackColor = false;
            btnSeedTest.Click += btnSeedTest_Click;
            // 
            // btnClear
            // 
            btnClear.BackColor = Color.FromArgb(80, 80, 80);
            btnClear.FlatAppearance.BorderSize = 0;
            btnClear.FlatStyle = FlatStyle.Flat;
            btnClear.ForeColor = Color.White;
            btnClear.Location = new Point(12, 174);
            btnClear.Name = "btnClear";
            btnClear.Size = new Size(100, 30);
            btnClear.TabIndex = 6;
            btnClear.Text = "Clear Chat";
            btnClear.UseVisualStyleBackColor = false;
            btnClear.Click += btnClear_Click;
            // 
            // btnExport
            // 
            btnExport.BackColor = Color.FromArgb(80, 80, 80);
            btnExport.FlatAppearance.BorderSize = 0;
            btnExport.FlatStyle = FlatStyle.Flat;
            btnExport.ForeColor = Color.White;
            btnExport.Location = new Point(120, 174);
            btnExport.Name = "btnExport";
            btnExport.Size = new Size(100, 30);
            btnExport.TabIndex = 7;
            btnExport.Text = "Export";
            btnExport.UseVisualStyleBackColor = false;
            btnExport.Click += btnExport_Click;
            // 
            // lblConnStatus
            // 
            lblConnStatus.BackColor = Color.Transparent;
            lblConnStatus.Font = new Font("Segoe UI", 10F);
            lblConnStatus.ForeColor = Color.Silver;
            lblConnStatus.Location = new Point(12, 218);
            lblConnStatus.Name = "lblConnStatus";
            lblConnStatus.Size = new Size(364, 48);
            lblConnStatus.TabIndex = 8;
            lblConnStatus.Text = "● Not connected";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(32, 32, 32);
            ClientSize = new Size(1103, 775);
            Controls.Add(rtbChat);
            Controls.Add(chkSplitMessage);
            Controls.Add(lblMaxTokens);
            Controls.Add(numMaxTokens);
            Controls.Add(txtInput);
            Controls.Add(btnSend);
            Controls.Add(grpProxy);
            Controls.Add(grpSources);
            Controls.Add(grpSettings);
            Controls.Add(grpConnection);
            MinimumSize = new Size(900, 700);
            Name = "Form1";
            Text = "ChatFreePT";
            ((System.ComponentModel.ISupportInitialize)numMaxTokens).EndInit();
            grpProxy.ResumeLayout(false);
            grpSources.ResumeLayout(false);
            grpSettings.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)numParallel).EndInit();
            ((System.ComponentModel.ISupportInitialize)numTimeout).EndInit();
            ((System.ComponentModel.ISupportInitialize)numDelay).EndInit();
            grpConnection.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        // ── Chat area ──────────────────────────────────────────────────────────
        private System.Windows.Forms.RichTextBox     rtbChat;
        private System.Windows.Forms.CheckBox        chkSplitMessage;
        private System.Windows.Forms.Label           lblMaxTokens;
        private System.Windows.Forms.NumericUpDown   numMaxTokens;
        private System.Windows.Forms.TextBox         txtInput;
        private System.Windows.Forms.Button          btnSend;
        // ── Proxy group ────────────────────────────────────────────────────────
        private System.Windows.Forms.GroupBox        grpProxy;
        private System.Windows.Forms.CheckBox        chkUseProxy;
        private System.Windows.Forms.Label           lblProxyStatus;
        private System.Windows.Forms.ProgressBar     pbProxy;
        private System.Windows.Forms.Button          btnFindProxies;
        private System.Windows.Forms.Button          btnStopProxies;
        private System.Windows.Forms.Label           lblProxiesFound;
        private System.Windows.Forms.ListBox         lstFoundProxies;
        // ── Sources group ──────────────────────────────────────────────────────
        private System.Windows.Forms.GroupBox        grpSources;
        private System.Windows.Forms.CheckedListBox  clbSources;
        // ── Settings group ─────────────────────────────────────────────────────
        private System.Windows.Forms.GroupBox        grpSettings;
        private System.Windows.Forms.Label           lblParallel;
        private System.Windows.Forms.NumericUpDown   numParallel;
        private System.Windows.Forms.Label           lblTimeout;
        private System.Windows.Forms.NumericUpDown   numTimeout;
        private System.Windows.Forms.Label           lblDelay;
        private System.Windows.Forms.NumericUpDown   numDelay;
        // ── Connection group ───────────────────────────────────────────────────
        private System.Windows.Forms.GroupBox        grpConnection;
        private System.Windows.Forms.Label           lblModel;
        private System.Windows.Forms.ComboBox        cmbModel;
        private System.Windows.Forms.CheckBox        chkBrowserMode;
        private System.Windows.Forms.Button          btnConnect;
        private System.Windows.Forms.Button          btnCancelConnect;
        private System.Windows.Forms.Button          btnNextProxy;
        private System.Windows.Forms.Button          btnReconnect;
        private System.Windows.Forms.Button          btnSeedTest;
        private System.Windows.Forms.Button          btnClear;
        private System.Windows.Forms.Button          btnExport;
        private System.Windows.Forms.Label           lblConnStatus;
    }
}
