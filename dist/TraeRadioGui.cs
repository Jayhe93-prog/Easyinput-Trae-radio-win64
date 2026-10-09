using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TraeRadio
{
    static class App
    {
        public static string TraeCli = "";
        public static string ScriptPath = "";
        public static string StateDir = "";
        public static string DefaultWorkDir = "";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            StateDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TraeRadio");
            Directory.CreateDirectory(StateDir);
            ScriptPath = Path.Combine(StateDir, "radio.ps1");
            try { Extract("radio.ps1", ScriptPath); } catch { }

            DefaultWorkDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            TraeCli = LocateTraeCli();

            foreach (string a in args)
                if (a == "--selftest") { SelfTest(); return; }
            foreach (string a in args)
                if (a == "--logintest") { LoginTest(); return; }

            Application.Run(new MainForm());
        }

        // 无人值守自检：把结果写到 %LOCALAPPDATA%\TraeRadio\selftest.txt
        static void SelfTest()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TraeRadio self-test @ " + DateTime.Now.ToString("s"));
            sb.AppendLine("TraeCli  = " + (TraeCli.Length > 0 ? TraeCli : "(not found)"));
            sb.AppendLine("Script   = " + ScriptPath + "  exists=" + File.Exists(ScriptPath));
            sb.AppendLine("StateDir = " + StateDir);

            if (TraeCli.Length > 0)
            {
                int c;
                string st = RunCmd(TraeCli, "login status", out c);
                sb.AppendLine("login status: exit=" + c + " -> " + st.Replace("\n", " | "));
                string raw;
                sb.AppendLine("IsLoggedIn = " + IsLoggedIn(out raw));
            }
            else
            {
                sb.AppendLine("IsLoggedIn = (skipped, no cli)");
            }

            string tmp = Path.Combine(StateDir, "selftest-ws");
            Directory.CreateDirectory(tmp);
            sb.AppendLine("");
            sb.AppendLine("=== engine: status (mock) ===");
            sb.AppendLine(RunRadio("status -Backend mock"));
            sb.AppendLine("=== engine: new 1 (mock) ===");
            sb.AppendLine(RunRadio("new 1 \"" + tmp + "\" -Backend mock"));
            sb.AppendLine("=== engine: say 1 (mock, 中文回环) ===");
            sb.AppendLine(RunRadio("say 1 \"中文编码回环测试：任务电台\" -Backend mock"));
            sb.AppendLine("=== engine: status (mock) ===");
            sb.AppendLine(RunRadio("status -Backend mock"));

            File.WriteAllText(Path.Combine(StateDir, "selftest.txt"), sb.ToString(), Encoding.UTF8);
        }

        // 无人值守登录自检：拉起企业 SSO 登录，抓取链接后立即收尾（不完成真实登录）
        static void LoginTest()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TraeRadio login-test @ " + DateTime.Now.ToString("s"));
            sb.AppendLine("TraeCli = " + (TraeCli.Length > 0 ? TraeCli : "(not found)"));
            string dest = Path.Combine(StateDir, "logintest.txt");

            if (TraeCli.Length == 0)
            {
                sb.AppendLine("verdict = FAIL: 未检测到 traecli");
                File.WriteAllText(dest, sb.ToString(), Encoding.UTF8);
                return;
            }

            string raw;
            sb.AppendLine("IsLoggedIn(before) = " + IsLoggedIn(out raw));
            sb.AppendLine("login status -> " + raw.Replace("\n", " | "));

            Process p;
            try { p = StartCli("login", null, null); }
            catch (Exception ex)
            {
                sb.AppendLine("verdict = FAIL: 启动 login 失败 -> " + ex.Message);
                File.WriteAllText(dest, sb.ToString(), Encoding.UTF8);
                return;
            }

            string url = "";
            var got = new ManualResetEvent(false);
            var t = new Thread(delegate ()
            {
                try
                {
                    p.StandardOutput.ReadToEndAsync();
                    string line;
                    while ((line = p.StandardError.ReadLine()) != null)
                    {
                        string s = line.Trim();
                        if (s.Length == 0) continue;
                        sb.AppendLine("cli: " + s);
                        if (url.Length == 0 && (s.StartsWith("http://") || s.StartsWith("https://"))) { url = s; got.Set(); }
                    }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();

            got.WaitOne(10000);
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.WaitForExit(3000); } catch { }

            sb.AppendLine("captured_url = " + (url.Length > 0 ? url : "(none)"));
            bool pass = url.Contains("enterprise.trae.cn") && url.Contains("authorization");
            sb.AppendLine("verdict = " + (pass
                ? "PASS: 企业 SSO 登录链接已生成（浏览器登录通道可用）"
                : "FAIL: 未捕获企业 SSO 链接"));
            File.WriteAllText(dest, sb.ToString(), Encoding.UTF8);
        }

        static void Extract(string res, string dest)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
            {
                if (s == null) return;
                using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                    s.CopyTo(fs);
            }
        }

        // 依次在 releases / bin / PATH 中定位 traecli
        public static string LocateTraeCli()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string rel = Path.Combine(local, @"TraeCLI\releases");
            if (Directory.Exists(rel))
            {
                string best = "";
                foreach (string d in Directory.GetDirectories(rel))
                {
                    string e = Path.Combine(d, "traex.exe");
                    if (File.Exists(e)) best = e;
                }
                if (best.Length > 0) return best;
            }

            string a = Path.Combine(local, @"Programs\TraeCLI\bin\traecli.cmd");
            if (File.Exists(a)) return a;

            string p = Where("traecli");
            if (p.Length > 0) return p;

            return "";
        }

        static string Where(string name)
        {
            int code;
            string outp = RunCmd("where.exe", name, out code);
            if (code == 0)
                foreach (string line in outp.Split('\n'))
                {
                    string t = line.Trim();
                    if (t.Length > 0) return t;
                }
            return "";
        }

        static ProcessStartInfo BuildPsi(string file, string args)
        {
            var psi = new ProcessStartInfo();
            if (file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c \"\"" + file + "\" " + args + "\"";
            }
            else
            {
                psi.FileName = file;
                psi.Arguments = args;
            }
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            return psi;
        }

        public static string RunCmd(string file, string args, out int exit)
        {
            return RunCmdEnv(file, args, null, null, out exit);
        }

        // 可选注入一个环境变量（用于企业 PAT：TRAECLI_PERSONAL_ACCESS_TOKEN）
        public static string RunCmdEnv(string file, string args, string envKey, string envVal, out int exit)
        {
            try
            {
                var psi = BuildPsi(file, args);
                if (envKey != null) psi.EnvironmentVariables[envKey] = envVal;
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    exit = p.ExitCode;
                    string all = o;
                    if (!string.IsNullOrEmpty(e)) all += (all.Length > 0 ? "\n" : "") + e;
                    return all.Trim();
                }
            }
            catch (Exception ex)
            {
                exit = -1;
                return "[错误] " + ex.Message;
            }
        }

        // 启动一个长驻 CLI 进程（登录用）：重定向 stdout/stderr，便于抓取 SSO 链接
        public static Process StartCli(string args, string envKey, string envVal)
        {
            var psi = new ProcessStartInfo();
            if (TraeCli.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                TraeCli.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c \"\"" + TraeCli + "\" " + args + "\"";
            }
            else
            {
                psi.FileName = TraeCli;
                psi.Arguments = args;
            }
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            if (envKey != null) psi.EnvironmentVariables[envKey] = envVal;
            return Process.Start(psi);
        }

        public static bool IsLoggedIn(out string raw)
        {
            raw = "";
            if (string.IsNullOrEmpty(TraeCli)) return false;
            int code;
            raw = RunCmd(TraeCli, "login status", out code);
            string low = (raw ?? "").ToLowerInvariant();
            bool notLogged = low.Contains("not logged") || low.Contains("not_logged_in");
            return !notLogged;
        }

        // 调 PowerShell 执行内嵌电台脚本；innerArgs 形如：say 1 "文本"
        public static string RunRadio(string innerArgs)
        {
            string args = "-NoProfile -ExecutionPolicy Bypass -File \"" + ScriptPath + "\" " + innerArgs;
            int code;
            return RunCmd("powershell.exe", args, out code);
        }
    }

    class InputDialog : Form
    {
        public string Value = "";

        public InputDialog(string prompt, string def)
        {
            Text = "发送到槽位";
            Width = 540;
            Height = 190;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            var l = new Label();
            l.Text = prompt;
            l.SetBounds(14, 14, 500, 20);
            Controls.Add(l);

            var tb = new TextBox();
            tb.SetBounds(14, 42, 500, 24);
            tb.Text = def;
            Controls.Add(tb);

            var ok = new Button();
            ok.Text = "发送";
            ok.SetBounds(340, 84, 80, 30);
            ok.DialogResult = DialogResult.OK;
            ok.Click += delegate { Value = tb.Text; };
            Controls.Add(ok);

            var ca = new Button();
            ca.Text = "取消";
            ca.SetBounds(432, 84, 80, 30);
            ca.DialogResult = DialogResult.Cancel;
            Controls.Add(ca);

            AcceptButton = ok;
            CancelButton = ca;
            tb.SelectAll();
            tb.Focus();
        }
    }

    class PatDialog : Form
    {
        public string Pat = "";
        public string BaseUrl = "";

        public PatDialog()
        {
            Text = "企业 PAT 登录";
            Width = 560;
            Height = 260;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            var l1 = new Label();
            l1.Text = "企业个人访问令牌（PAT）";
            l1.SetBounds(14, 14, 500, 20);
            Controls.Add(l1);

            var tbPat = new TextBox();
            tbPat.SetBounds(14, 38, 520, 24);
            tbPat.UseSystemPasswordChar = true;
            Controls.Add(tbPat);

            var l2 = new Label();
            l2.Text = "企业 API Base URL（可留空，用默认）";
            l2.SetBounds(14, 74, 500, 20);
            Controls.Add(l2);

            var tbUrl = new TextBox();
            tbUrl.SetBounds(14, 98, 520, 24);
            Controls.Add(tbUrl);

            var hint = new Label();
            hint.Text = "PAT 由企业管理员在 Trae 企业控制台签发。令牌仅用于本次登录，不会写入程序。";
            hint.ForeColor = Color.DimGray;
            hint.SetBounds(14, 132, 520, 40);
            Controls.Add(hint);

            var ok = new Button();
            ok.Text = "登录";
            ok.SetBounds(360, 180, 80, 30);
            ok.DialogResult = DialogResult.OK;
            ok.Click += delegate { Pat = tbPat.Text; BaseUrl = tbUrl.Text; };
            Controls.Add(ok);

            var ca = new Button();
            ca.Text = "取消";
            ca.SetBounds(452, 180, 80, 30);
            ca.DialogResult = DialogResult.Cancel;
            Controls.Add(ca);

            AcceptButton = ok;
            CancelButton = ca;
            tbPat.Focus();
        }
    }

    class MainForm : Form
    {
        Label statusLabel;
        TextBox workDirBox;
        TextBox outputBox;
        CheckBox speakChk;
        Button loginBtn;
        Button patBtn;
        Label[] slotLabels = new Label[5];

        public MainForm()
        {
            Text = "Trae 任务电台";
            ClientSize = new Size(780, 580);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);

            statusLabel = new Label();
            statusLabel.SetBounds(14, 12, 496, 20);
            statusLabel.Text = "● 初始化…";
            statusLabel.ForeColor = Color.DimGray;
            Controls.Add(statusLabel);

            loginBtn = new Button();
            loginBtn.Text = "登录 Trae";
            loginBtn.SetBounds(520, 9, 110, 26);
            loginBtn.Click += delegate { DoLogin(); };
            Controls.Add(loginBtn);

            patBtn = new Button();
            patBtn.Text = "企业 PAT 登录";
            patBtn.SetBounds(638, 9, 126, 26);
            patBtn.Click += delegate { DoLoginPat(); };
            Controls.Add(patBtn);

            var wl = new Label();
            wl.Text = "工作目录";
            wl.SetBounds(14, 42, 60, 20);
            Controls.Add(wl);

            workDirBox = new TextBox();
            workDirBox.SetBounds(78, 40, 560, 24);
            workDirBox.Text = App.DefaultWorkDir;
            Controls.Add(workDirBox);

            var browse = new Button();
            browse.Text = "浏览";
            browse.SetBounds(648, 39, 60, 26);
            browse.Click += delegate
            {
                var d = new FolderBrowserDialog();
                if (d.ShowDialog() == DialogResult.OK) workDirBox.Text = d.SelectedPath;
            };
            Controls.Add(browse);

            int y = 78;
            for (int i = 1; i <= 4; i++)
            {
                int idx = i;
                var lab = new Label();
                lab.Text = "槽位 " + i + "：-";
                lab.SetBounds(14, y + 3, 190, 20);
                Controls.Add(lab);
                slotLabels[i] = lab;

                var nb = new Button();
                nb.Text = "新建";
                nb.SetBounds(210, y, 64, 26);
                nb.Click += delegate { DoNew(idx); };
                Controls.Add(nb);

                var sb = new Button();
                sb.Text = "说";
                sb.SetBounds(280, y, 64, 26);
                sb.Click += delegate { DoSay(idx); };
                Controls.Add(sb);

                var hb = new Button();
                hb.Text = "听";
                hb.SetBounds(350, y, 64, 26);
                hb.Click += delegate { DoHear(idx); };
                Controls.Add(hb);

                y += 32;
            }

            speakChk = new CheckBox();
            speakChk.Text = "播报结果";
            speakChk.SetBounds(430, y - 2, 100, 24);
            Controls.Add(speakChk);

            var st = new Button();
            st.Text = "刷新台账";
            st.SetBounds(540, y - 3, 90, 26);
            st.Click += delegate { RefreshStatus(); };
            Controls.Add(st);

            var clr = new Button();
            clr.Text = "清空";
            clr.SetBounds(636, y - 3, 60, 26);
            clr.Click += delegate { outputBox.Clear(); };
            Controls.Add(clr);

            outputBox = new TextBox();
            outputBox.Multiline = true;
            outputBox.ReadOnly = true;
            outputBox.ScrollBars = ScrollBars.Both;
            outputBox.WordWrap = false;
            outputBox.SetBounds(14, y + 32, 752, 400);
            outputBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            outputBox.BackColor = Color.FromArgb(28, 28, 28);
            outputBox.ForeColor = Color.Gainsboro;
            outputBox.Font = new Font("Consolas", 9F);
            Controls.Add(outputBox);

            Shown += OnShown;
        }

        void SetStatus(string text, Color c)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = c;
        }

        void Log(string s)
        {
            outputBox.AppendText(s + Environment.NewLine);
        }

        string BaseArgs(string inner)
        {
            string a = inner;
            if (!string.IsNullOrEmpty(App.TraeCli)) a += " -Cli \"" + App.TraeCli + "\"";
            string wd = workDirBox.Text.Trim();
            if (wd.Length > 0) a += " -WorkDir \"" + wd + "\"";
            return a;
        }

        static string PsQuote(string s)
        {
            return s.Replace("`", "``").Replace("\"", "`\"").Replace("$", "`$");
        }

        void OnShown(object sender, EventArgs e)
        {
            Log("Trae 任务电台 v0.2  ·  引擎 radio.ps1  ·  台账 " + Path.Combine(App.StateDir, "ledger.json"));
            Log("traecli: " + (App.TraeCli.Length > 0 ? App.TraeCli : "(未检测到)"));
            Log("");

            if (App.TraeCli.Length == 0)
            {
                SetStatus("● 未检测到 TraeCode CLI", Color.Firebrick);
                Log("未找到 traecli。请先安装后再重开本程序：");
                Log("  PowerShell 执行：irm https://trae.cn/trae-cli/install_v2.ps1 | iex");
                MessageBox.Show(this,
                    "未检测到 TraeCode CLI (traecli)。\n\n请先在 PowerShell 中执行安装：\n\n" +
                    "  irm https://trae.cn/trae-cli/install_v2.ps1 | iex\n\n安装后重新打开本程序。",
                    "缺少 TraeCode CLI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string raw;
            if (App.IsLoggedIn(out raw))
            {
                SetStatus("● 已登录 Trae", Color.SeaGreen);
                Log(raw);
                Log("");
                RefreshStatus();
                return;
            }

            SetStatus("● 未登录 Trae", Color.Firebrick);
            var r = MessageBox.Show(this,
                "检测到尚未登录 Trae。\n\n" +
                "点「确定」后自动打开浏览器，请用你的【Trae 企业账号和密码】完成登录（企业 SSO）；\n" +
                "若浏览器通道不可用，可改点主界面的「企业 PAT 登录」。\n\n" +
                "登录成功后本程序会自动继续。",
                "请登录 Trae 企业账号", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (r == DialogResult.OK) DoLogin();
        }

        void DoLogin()
        {
            if (App.TraeCli.Length == 0)
            {
                MessageBox.Show(this, "未检测到 traecli，无法登录。请先安装。", "缺少 TraeCode CLI",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetStatus("● 等待浏览器登录…", Color.DarkOrange);
            Log("→ 启动 traecli login（企业 SSO，浏览器）…");

            Process p;
            try { p = App.StartCli("login", null, null); }
            catch (Exception ex)
            {
                SetStatus("● 登录启动失败", Color.Firebrick);
                Log("[错误] " + ex.Message);
                return;
            }

            // CLI 会把企业 SSO 链接打到 stderr；浏览器若未自动打开，用户可手动访问
            var reader = new Thread(delegate ()
            {
                try
                {
                    p.StandardOutput.ReadToEndAsync();
                    string line;
                    while ((line = p.StandardError.ReadLine()) != null)
                    {
                        string s = line.Trim();
                        if (s.Length == 0) continue;
                        string c = s;
                        try { BeginInvoke((Action)delegate () { Log("  " + c); }); } catch { }
                    }
                }
                catch { }
            });
            reader.IsBackground = true;
            reader.Start();

            var poll = new Thread(delegate ()
            {
                string raw = "";
                bool ok = false;
                for (int i = 0; i < 150; i++)
                {
                    Thread.Sleep(2000);
                    if (App.IsLoggedIn(out raw)) { ok = true; break; }
                }
                try { if (!p.HasExited) p.Kill(); } catch { }
                try
                {
                    BeginInvoke((Action)delegate ()
                    {
                        if (ok)
                        {
                            SetStatus("● 已登录 Trae", Color.SeaGreen);
                            Log("✓ 登录成功。");
                            Log(raw);
                            Log("");
                            RefreshStatus();
                        }
                        else
                        {
                            SetStatus("● 仍未登录", Color.Firebrick);
                            Log("登录未完成（等待超时）。可点「登录 Trae」重试。");
                        }
                    });
                }
                catch { }
            });
            poll.IsBackground = true;
            poll.Start();
        }

        // 企业 ToB 免交互登录：PAT 经环境变量注入，不落到命令行与磁盘
        void DoLoginPat()
        {
            if (App.TraeCli.Length == 0)
            {
                MessageBox.Show(this, "未检测到 traecli，无法登录。请先安装。", "缺少 TraeCode CLI",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pat, baseUrl;
            using (var d = new PatDialog())
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                pat = d.Pat.Trim();
                baseUrl = d.BaseUrl.Trim();
            }
            if (pat.Length == 0) return;

            string args = "login --with-trae-pat";
            if (baseUrl.Length > 0) args += " --base-url \"" + baseUrl.Replace("\"", "") + "\"";

            SetStatus("● 正在用企业 PAT 登录…", Color.DarkOrange);
            Log("→ traecli " + args);

            var t = new Thread(delegate ()
            {
                int code;
                string outp = App.RunCmdEnv(App.TraeCli, args, "TRAECLI_PERSONAL_ACCESS_TOKEN", pat, out code);
                string raw;
                bool ok = App.IsLoggedIn(out raw);
                try
                {
                    BeginInvoke((Action)delegate ()
                    {
                        if (outp.Length > 0) Log(outp);
                        if (ok)
                        {
                            SetStatus("● 已登录 Trae", Color.SeaGreen);
                            Log("✓ PAT 登录成功。");
                            Log("");
                            RefreshStatus();
                        }
                        else
                        {
                            SetStatus("● PAT 登录失败", Color.Firebrick);
                            Log("✗ 未能登录（exit=" + code + "）。请核对 PAT 与企业 API Base URL。");
                        }
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void RefreshStatus()
        {
            Log("$ status");
            Log(App.RunRadio(BaseArgs("status")));
            Log("");
            ParseSlots(App.RunRadio(BaseArgs("status")));
        }

        // 从 status 文本里抓每槽位的 session 短号，回填到槽位标签
        void ParseSlots(string statusText)
        {
            foreach (string line in statusText.Split('\n'))
            {
                string s = line.Trim();
                if (s.Length < 4 || s[0] != '[') continue;
                int close = s.IndexOf(']');
                if (close < 2) continue;
                int slot;
                if (!int.TryParse(s.Substring(1, close - 1), out slot)) continue;
                if (slot < 1 || slot > 4) continue;
                int si = s.IndexOf("session ");
                if (si >= 0)
                {
                    string rest = s.Substring(si + 8);
                    int sp = rest.IndexOfAny(new char[] { ' ', '…' });
                    string sid = sp > 0 ? rest.Substring(0, sp) : rest;
                    slotLabels[slot].Text = "槽位 " + slot + "：" + sid;
                }
                else
                {
                    slotLabels[slot].Text = "槽位 " + slot + "：-";
                }
            }
        }

        void DoNew(int slot)
        {
            string wd = workDirBox.Text.Trim();
            Log("$ new " + slot + " " + wd);
            Log(App.RunRadio(BaseArgs("new " + slot + " \"" + wd + "\"")));
            Log("");
            RefreshStatus();
        }

        void DoSay(int slot)
        {
            using (var d = new InputDialog("对槽位 " + slot + " 说：", ""))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string text = d.Value.Trim();
                if (text.Length == 0) return;
                string extra = speakChk.Checked ? " -Speak" : "";
                Log("$ say " + slot + " \"" + text + "\"");
                Log(App.RunRadio(BaseArgs("say " + slot + " \"" + PsQuote(text) + "\"" + extra)));
                Log("");
                ParseSlots(App.RunRadio(BaseArgs("status")));
            }
        }

        void DoHear(int slot)
        {
            Log("$ hear " + slot);
            Log(App.RunRadio(BaseArgs("hear " + slot)));
            Log("");
        }
    }
}