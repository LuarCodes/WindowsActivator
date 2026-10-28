using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinActivationAssistant
{
    public sealed class MainForm : Form
    {
        // Upgrade-edition page in the Microsoft Store (Home -> Pro, Pro -> Pro for Workstations).
        const string StoreUpgradeUri = "ms-windows-store://pdp/?productid=BF712690PL0G";
        const string StoreHomeUri = "ms-windows-store://home";
        const string ActivationSettingsUri = "ms-settings:activation";

        readonly ToolTip tip = new ToolTip { InitialDelay = 400, AutoPopDelay = 15000 };
        Card infoCard, extrasCard, tabCard, resultCard;
        Panel keyPanel, kmsPanel;
        ModernButton tabKey, tabKms, btnActivateKey, btnActivateKms, btnRefresh, btnSettings, btnStore, btnErase;
        InputBox keyInput, kmsInput;
        CheckBox chkGvlk, chkErase;
        Banner banner;
        Label vWindows, vEdition, vVersion, vBuild, vStatus, vChannel, vKey, vExpires, vKms;
        Label rChannel, rExpires, rCode, rMeaning;
        bool busy, formatting;
        OsInfo os;
        LicenseInfo lic;

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Windows Activation Assistant";
            ClientSize = new Size(980, 640);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Font = Theme.F(9.75f);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildHeader();
            BuildInfoCard();
            BuildExtrasCard();
            BuildTabs();
            BuildResultCard();
            SelectTab(false);
            ResumeLayout(false);

            Shown += async (s, e) => await RefreshInfo(true);
            FormClosing += (s, e) => { keyInput.Wipe(); kmsInput.Wipe(); };
        }

        // ------------------------------------------------------------------ layout
        static Label L(Control parent, string text, Font f, Color c, int x, int y, int w, int h,
                       ContentAlignment align = ContentAlignment.MiddleLeft, bool ellipsis = true)
        {
            var l = new Label
            {
                Text = text, Font = f, ForeColor = c,
                BackColor = parent is Card ? Theme.Card : parent.BackColor,
                Location = new Point(x, y), Size = new Size(w, h),
                TextAlign = align, AutoEllipsis = ellipsis, UseMnemonic = false
            };
            parent.Controls.Add(l);
            return l;
        }

        void BuildHeader()
        {
            L(this, "Windows Activation Assistant", Theme.Semi(17f), Theme.Text, 22, 12, 620, 36);
            L(this, "Check your Windows license and activate it with your own product key or your company's KMS server.",
              Theme.F(9.5f), Theme.SubText, 24, 46, 700, 22);
            var badge = L(this, "\u25CF  Running as administrator", Theme.F(9f), Theme.Good, 700, 22, 256, 22, ContentAlignment.MiddleRight);
            tip.SetToolTip(badge, "Administrator rights are needed to install keys and activate Windows.");
        }

        void BuildInfoCard()
        {
            infoCard = new Card { Location = new Point(24, 88), Size = new Size(360, 330) };
            Controls.Add(infoCard);
            L(infoCard, "This PC", Theme.Semi(12f), Theme.Text, 18, 12, 200, 30);
            btnRefresh = new ModernButton { Style = BtnStyle.Ghost, Icon = Glyph.Refresh, Location = new Point(312, 12), Size = new Size(32, 30) };
            tip.SetToolTip(btnRefresh, "Re-read edition and license status");
            btnRefresh.Click += async (s, e) => { if (!busy) await RefreshInfo(true); };
            infoCard.Controls.Add(btnRefresh);

            string[] names = { "Windows", "Edition ID", "Version", "Build", "Activation", "Channel", "Product key", "Expires", "KMS address saved" };
            var vals = new Label[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int y = 52 + i * 29;
                L(infoCard, names[i], Theme.F(9f), Theme.SubText, 18, y, 128, 24);
                vals[i] = L(infoCard, "\u2026", Theme.F(9.75f), Theme.Text, 148, y, 198, 24);
            }
            vWindows = vals[0]; vEdition = vals[1]; vVersion = vals[2]; vBuild = vals[3]; vStatus = vals[4];
            vChannel = vals[5]; vKey = vals[6]; vExpires = vals[7]; vKms = vals[8];
            vStatus.Font = Theme.Semi(9.75f);
        }

        void BuildExtrasCard()
        {
            extrasCard = new Card { Location = new Point(24, 430), Size = new Size(360, 186) };
            Controls.Add(extrasCard);
            L(extrasCard, "Helpful extras", Theme.Semi(12f), Theme.Text, 18, 12, 300, 30);

            btnSettings = new ModernButton { Text = "Open Windows Activation settings", Icon = Glyph.Settings, LeftAlign = true, Location = new Point(18, 50), Size = new Size(324, 36) };
            btnSettings.Click += (s, e) => Launch(ActivationSettingsUri, null);
            btnStore = new ModernButton { Text = "Buy or upgrade an edition (Microsoft Store)", Icon = Glyph.Store, LeftAlign = true, Location = new Point(18, 92), Size = new Size(324, 36) };
            btnStore.Click += (s, e) => Launch(StoreUpgradeUri, StoreHomeUri);
            btnErase = new ModernButton { Text = "Erase saved KMS address from Windows", Icon = Glyph.Delete, LeftAlign = true, Location = new Point(18, 134), Size = new Size(324, 36) };
            btnErase.Click += async (s, e) => await EraseKms();
            tip.SetToolTip(btnSettings, "Settings \u203a System \u203a Activation");
            tip.SetToolTip(btnStore, "Opens the Microsoft Store edition-upgrade page (e.g. Home \u2192 Pro)");
            tip.SetToolTip(btnErase, "Clears any KMS host name/port Windows has stored in its licensing settings");
            extrasCard.Controls.AddRange(new Control[] { btnSettings, btnStore, btnErase });
        }

        void BuildTabs()
        {
            tabKey = new ModernButton { Style = BtnStyle.Toggle, Text = "Product key", Icon = Glyph.Key, Surface = Theme.Bg, Location = new Point(400, 88), Size = new Size(200, 38) };
            tabKms = new ModernButton { Style = BtnStyle.Toggle, Text = "Company KMS server", Icon = Glyph.Server, Surface = Theme.Bg, Location = new Point(608, 88), Size = new Size(220, 38) };
            tabKey.Click += (s, e) => { if (!busy) SelectTab(false); };
            tabKms.Click += (s, e) => { if (!busy) SelectTab(true); };
            Controls.Add(tabKey);
            Controls.Add(tabKms);

            tabCard = new Card { Location = new Point(400, 134), Size = new Size(556, 226) };
            Controls.Add(tabCard);

            // ---- product key tab
            keyPanel = new Panel { Location = new Point(4, 4), Size = new Size(548, 218), BackColor = Theme.Card };
            tabCard.Controls.Add(keyPanel);
            L(keyPanel, "Enter a Windows product key you own", Theme.Semi(11f), Theme.Text, 16, 12, 520, 26);
            L(keyPanel, "25 characters \u2013 dashes are added automatically.", Theme.F(9f), Theme.SubText, 16, 38, 520, 20);
            keyInput = new InputBox(true) { Location = new Point(16, 64), Size = new Size(514, 40) };
            keyInput.Box.MaxLength = 29;
            keyInput.Box.Hint = "e.g. 12345-67890-12345-67890-12345";
            keyInput.Box.TextChanged += (s, e) => FormatKey();
            keyInput.Box.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ActivateKey(); } };
            keyPanel.Controls.Add(keyInput);
            L(keyPanel, "The key goes straight to Windows' licensing service \u2013 no files, logs, command lines or environment variables \u2013 and the field is wiped after each attempt.",
              Theme.F(9f), Theme.SubText, 16, 110, 522, 40, ContentAlignment.TopLeft, false);
            btnActivateKey = new ModernButton { Style = BtnStyle.Primary, Text = "Install key & activate", Icon = Glyph.Key, Location = new Point(16, 166), Size = new Size(240, 40) };
            btnActivateKey.Click += async (s, e) => await ActivateKey();
            keyPanel.Controls.Add(btnActivateKey);

            // ---- KMS tab
            kmsPanel = new Panel { Location = new Point(4, 4), Size = new Size(548, 218), BackColor = Theme.Card };
            tabCard.Controls.Add(kmsPanel);
            L(kmsPanel, "Your company's KMS host", Theme.Semi(11f), Theme.Text, 16, 12, 520, 26);
            L(kmsPanel, "e.g.  kms.company.local   or   10.0.0.5:1688     (default port is 1688)", Theme.F(9f), Theme.SubText, 16, 38, 520, 20);
            kmsInput = new InputBox(true) { Location = new Point(16, 64), Size = new Size(514, 40) };
            kmsInput.Box.Hint = "Use kms7u.msdn.com.cn:1688";
            kmsInput.Box.Text = "kms7u.msdn.com.cn:1688";
            kmsInput.Box.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ActivateKms(); } };
            kmsPanel.Controls.Add(kmsInput);
            chkGvlk = new CheckBox { Text = "Install Microsoft's KMS client setup key for this edition if needed", Checked = true, Location = new Point(18, 110), Size = new Size(520, 22), BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.F(9f) };
            chkErase = new CheckBox { Text = "Erase the KMS address from Windows right after activating", Checked = true, Location = new Point(18, 134), Size = new Size(520, 22), BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.F(9f) };
            tip.SetToolTip(chkGvlk, "KMS only activates PCs that have a volume-license (GVLK) key. If this PC has a retail/OEM key, the public Microsoft KMS client key for your edition is installed first.");
            tip.SetToolTip(chkErase, "Windows normally remembers the KMS host so it can renew every 7 days. With this on, the address is removed as soon as activation finishes.\nRenewal then relies on your network's KMS auto-discovery (DNS); otherwise just run this app again before the 180-day expiry.");
            kmsPanel.Controls.Add(chkGvlk);
            kmsPanel.Controls.Add(chkErase);
            btnActivateKms = new ModernButton { Style = BtnStyle.Primary, Text = "Activate with KMS", Icon = Glyph.Server, Location = new Point(16, 168), Size = new Size(240, 40) };
            btnActivateKms.Click += async (s, e) => await ActivateKms();
            kmsPanel.Controls.Add(btnActivateKms);
            L(kmsPanel, "Kept in memory only \u2013 never saved to disk, .env, settings or logs.", Theme.F(8.5f), Theme.SubText, 270, 168, 268, 40, ContentAlignment.MiddleLeft, false);
        }

        void BuildResultCard()
        {
            resultCard = new Card { Location = new Point(400, 372), Size = new Size(556, 244) };
            Controls.Add(resultCard);
            L(resultCard, "Result", Theme.Semi(12f), Theme.Text, 18, 10, 200, 30);
            banner = new Banner { Location = new Point(16, 44), Size = new Size(524, 60) };
            resultCard.Controls.Add(banner);
            string[] names = { "Channel", "Expires", "Error code" };
            var vals = new Label[3];
            for (int i = 0; i < 3; i++)
            {
                int y = 114 + i * 26;
                L(resultCard, names[i], Theme.F(9f), Theme.SubText, 18, y, 100, 22);
                vals[i] = L(resultCard, "\u2014", Theme.F(9.75f), Theme.Text, 120, y, 420, 22);
            }
            rChannel = vals[0]; rExpires = vals[1]; rCode = vals[2];
            rCode.Font = new Font("Consolas", 10f);
            L(resultCard, "What it means", Theme.F(9f), Theme.SubText, 18, 192, 100, 22);
            rMeaning = L(resultCard, "\u2014", Theme.F(9f), Theme.Text, 120, 194, 420, 46, ContentAlignment.TopLeft, false);
            banner.Set(BannerKind.Busy, "Reading license status\u2026", "Querying Windows' licensing service");
        }

        void SelectTab(bool kms)
        {
            tabKey.Selected = !kms;
            tabKms.Selected = kms;
            keyPanel.Visible = !kms;
            kmsPanel.Visible = kms;
            (kms ? kmsInput.Box : keyInput.Box).Focus();
        }

        void FormatKey()
        {
            if (formatting) return;
            formatting = true;
            try
            {
                var tb = keyInput.Box;
                char[] raw = tb.Text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).Take(25).ToArray();
                var sb = new StringBuilder(29);
                for (int i = 0; i < raw.Length; i++) { if (i > 0 && i % 5 == 0) sb.Append('-'); sb.Append(raw[i]); }
                string formatted = sb.ToString();
                if (tb.Text != formatted) { tb.Text = formatted; tb.SelectionStart = tb.TextLength; }
                Array.Clear(raw, 0, raw.Length);
                sb.Clear();
            }
            finally { formatting = false; }
        }

        // ------------------------------------------------------------------ status
        void SetVal(Label l, string text, Color? c = null)
        {
            l.Text = string.IsNullOrEmpty(text) ? "\u2014" : text;
            l.ForeColor = c ?? Theme.Text;
            tip.SetToolTip(l, l.Text);
        }

        async Task RefreshInfo(bool updateBanner)
        {
            btnRefresh.Enabled = false;
            if (updateBanner) banner.Set(BannerKind.Busy, "Reading license status\u2026", "Querying Windows' licensing service");
            var t = await Task.Run(() => Tuple.Create(Licensing.GetOs(), Licensing.SafeGetLicense()));
            os = t.Item1; lic = t.Item2;
            FillInfo();
            if (updateBanner) ShowSummary();
            btnRefresh.Enabled = !busy;
        }

        void FillInfo()
        {
            SetVal(vWindows, os.Caption);
            SetVal(vEdition, os.EditionId);
            SetVal(vVersion, os.DisplayVersion);
            SetVal(vBuild, os.Build + (string.IsNullOrEmpty(os.Architecture) ? "" : "  \u00B7  " + os.Architecture));
            if (lic == null)
            {
                SetVal(vStatus, "\u25CF  Couldn't read license", Theme.Warn);
                SetVal(vChannel, null); SetVal(vKey, null); SetVal(vExpires, null); SetVal(vKms, null);
                return;
            }
            Color sc = lic.IsActivated ? Theme.Good : (lic.Status >= 2 && lic.GraceMinutes > 0 ? Theme.Warn : Theme.Bad);
            SetVal(vStatus, "\u25CF  " + lic.StatusText, sc);
            SetVal(vChannel, lic.ChannelText);
            SetVal(vKey, string.IsNullOrEmpty(lic.PartialKey) ? null : "XXXXX-XXXXX-XXXXX-XXXXX-" + lic.PartialKey);
            SetVal(vExpires, lic.ExpiryText);
            if (lic.KmsHostStored) SetVal(vKms, "Yes \u2013 use \"Erase\" to remove", Theme.Warn);
            else SetVal(vKms, "No", Theme.Good);
        }

        void FillResultRows(LicenseInfo li, uint code, string meaning)
        {
            SetVal(rChannel, li?.ChannelText);
            SetVal(rExpires, li?.ExpiryText);
            SetVal(rCode, ErrorCodes.Format(code), code == 0 ? Theme.Text : Theme.Bad);
            rMeaning.Text = string.IsNullOrEmpty(meaning) ? "\u2014" : meaning;
            tip.SetToolTip(rMeaning, rMeaning.Text);
        }

        void ShowSummary()
        {
            if (lic == null)
            {
                banner.Set(BannerKind.Warn, "Couldn't read the license status", "The Software Protection service may be stopped. Try Refresh.");
                FillResultRows(null, 0, null);
                return;
            }
            if (lic.IsActivated)
            {
                banner.Set(BannerKind.Good, "Windows is activated", os.Caption + "  \u00B7  " + lic.ChannelText);
                FillResultRows(lic, 0, "Nothing to fix. You can still install a different key or switch to your company's KMS server.");
            }
            else
            {
                banner.Set(BannerKind.Bad, "Windows is not activated", "Pick \"Product key\" or \"Company KMS server\" above and enter your license.");
                FillResultRows(lic, lic.StatusReason, ErrorCodes.Explain(lic.StatusReason));
            }
        }

        void ShowResult(OpResult r)
        {
            if (r.Ok)
            {
                banner.Set(BannerKind.Good, "PASS \u2013 Windows is activated", "License re-checked: " + (r.After?.StatusText ?? "Activated"));
                FillResultRows(r.After, 0, "Activation succeeded and was confirmed by re-reading the license status.");
            }
            else
            {
                banner.Set(BannerKind.Bad, r.CustomTitle ?? "FAIL \u2013 activation didn't complete",
                    "Failed while " + r.FailedStep + ". Current status: " + (r.After?.StatusText ?? "unknown"));
                FillResultRows(r.After, r.Code, r.CustomMessage ?? ErrorCodes.Explain(r.Code));
            }
        }

        void ShowValidation(string title, string message)
        {
            banner.Set(BannerKind.Warn, title, message);
        }

        // ------------------------------------------------------------------ actions
        void SetBusy(bool b)
        {
            busy = b;
            foreach (var c in new Control[] { btnActivateKey, btnActivateKms, btnRefresh, btnErase, tabKey, tabKms, chkGvlk, chkErase })
                c.Enabled = !b;
            keyInput.Box.ReadOnly = b;
            kmsInput.Box.ReadOnly = b;
            UseWaitCursor = b;
        }

        async Task RunOp(Func<IProgress<string>, OpResult> work)
        {
            SetBusy(true);
            var progress = new Progress<string>(m => banner.Set(BannerKind.Busy, "Working\u2026", m));
            OpResult r;
            try { r = await Task.Run(() => work(progress)); }
            catch (Exception ex) { r = new OpResult { Code = Licensing.HResultOf(ex), FailedStep = "running the operation" }; }
            await Task.Delay(50); // let queued progress messages drain first
            try { await RefreshInfo(false); } catch { }
            ShowResult(r);
            SetBusy(false);
        }

        async Task ActivateKey()
        {
            if (busy) return;
            string key = keyInput.Box.Text.Trim().ToUpperInvariant();
            if (!Licensing.KeyPattern.IsMatch(key))
            {
                ShowValidation("That isn't a complete product key", "Enter all 25 characters (5 groups of 5).");
                keyInput.Box.Focus();
                return;
            }
            try { await RunOp(p => Licensing.ActivateWithKey(key, p)); }
            finally { keyInput.Wipe(); key = null; }
        }

        async Task ActivateKms()
        {
            if (busy) return;
            if (!TryParseKms(kmsInput.Box.Text, out string host, out int? port, out string error))
            {
                ShowValidation("Check the KMS address", error);
                kmsInput.Box.Focus();
                return;
            }
            bool gvlk = chkGvlk.Checked, erase = chkErase.Checked;
            try { await RunOp(p => Licensing.ActivateWithKms(host, port, gvlk, erase, p)); }
            finally { kmsInput.Wipe(); host = null; }
        }

        async Task EraseKms()
        {
            if (busy) return;
            SetBusy(true);
            banner.Set(BannerKind.Busy, "Working\u2026", "Erasing the stored KMS address from Windows\u2026");
            bool ok = await Task.Run(() => Licensing.EraseStoredKmsHost());
            await RefreshInfo(false);
            if (ok && (lic == null || !lic.KmsHostStored))
                banner.Set(BannerKind.Good, "No KMS address is stored in Windows", "Windows' licensing settings no longer contain a KMS host name or port.");
            else
                banner.Set(BannerKind.Warn, "Couldn't fully erase the KMS address", "Try again, or restart and press Erase once more.");
            SetBusy(false);
        }

        internal static bool TryParseKms(string input, out string host, out int? port, out string error)
        {
            host = null; port = null; error = null;
            string s = (input ?? "").Trim();
            s = Regex.Replace(s, @"^[a-zA-Z][a-zA-Z0-9+.\-]*://", "");
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);
            if (s.Length == 0) { error = "Enter your office's KMS host name or IP address."; return false; }

            string portStr = null;
            if (s.StartsWith("["))
            {
                int end = s.IndexOf(']');
                if (end < 0) { error = "The IPv6 address is missing a closing ']'."; return false; }
                host = s.Substring(1, end - 1);
                string rest = s.Substring(end + 1);
                if (rest.StartsWith(":")) portStr = rest.Substring(1);
                else if (rest.Length > 0) { error = "Unexpected text after the address."; return false; }
            }
            else if (s.Count(c => c == ':') == 1)
            {
                var parts = s.Split(':');
                host = parts[0]; portStr = parts[1];
            }
            else host = s;

            if (portStr != null)
            {
                if (!int.TryParse(portStr, out int pn) || pn < 1 || pn > 65535) { error = "The port must be a number between 1 and 65535."; return false; }
                port = pn;
            }
            if (host.Length == 0 || host.Length > 253 || !Regex.IsMatch(host, @"^[A-Za-z0-9.\-_:]+$"))
            { error = "The host name can only contain letters, numbers, dots and hyphens."; return false; }
            return true;
        }

        static void Launch(string uri, string fallback)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + uri + "\"") { UseShellExecute = false }); }
                catch
                {
                    if (fallback != null) Launch(fallback, null);
                    else MessageBox.Show("Windows couldn't open:\n" + uri, "Windows Activation Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
