using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("아이피 변경")]
[assembly: AssemblyProduct("아이피 변경")]
[assembly: AssemblyVersion("1.3.1.0")]

namespace IpChanger
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            // 네트워크 사용/사용안함 전환에는 관리자 권한이 필요 → 처음 켤 때 한 번 요청
            if (!MainForm.IsAdmin())
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" });
                    return;
                }
                catch { /* 거부하면 권한 없이 실행 (전환 기능 제한) */ }
            }
            bool updated = Array.IndexOf(Environment.GetCommandLineArgs(), "--updated") >= 0;

            // 하나만 실행: 이미 켜져 있으면 그 창을 띄우고 끝 (업데이트 직후엔 이전 것이 꺼질 때까지 기다림)
            bool created;
            Mutex mutex = new Mutex(true, "IpChanger_SingleInstance", out created);
            if (!created)
            {
                bool got = false;
                if (updated) { try { got = mutex.WaitOne(20000); } catch (AbandonedMutexException) { got = true; } }
                if (!got)
                {
                    try { EventWaitHandle.OpenExisting("IpChanger_Show").Set(); } catch { }
                    return;
                }
            }

            // 업데이트 때 남은 이전 파일 정리
            try { string old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }

            try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(updated));
            GC.KeepAlive(mutex);
            try { mutex.ReleaseMutex(); } catch { }
        }
    }

    // ---------- 큰 동그란 버튼 ----------
    enum BtnState { Ready, Busy, Wired }

    class RoundButton : Control
    {
        BtnState state = BtnState.Ready;
        string busyText = "변경중";
        float press = 0f, target = 0f;
        System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();

        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            anim.Interval = 15;
            anim.Tick += delegate {
                float step = 0.12f;
                if (Math.Abs(press - target) <= step) { press = target; anim.Stop(); }
                else press += press < target ? step : -step;
                Invalidate();
            };
        }

        public BtnState State { get { return state; } }

        bool hover, down;
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; if (state == BtnState.Ready) { target = 0f; anim.Start(); } Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (state != BtnState.Ready || e.Button != MouseButtons.Left) return;
            down = true; target = 0.5f; anim.Start();   // 누르는 순간 살짝 들어감
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            down = false;
            if (state == BtnState.Ready) { target = 0f; anim.Start(); }
        }

        static Color Light(Color c, float k) { return Color.FromArgb((int)(c.R + (255 - c.R) * k), (int)(c.G + (255 - c.G) * k), (int)(c.B + (255 - c.B) * k)); }

        public void SetState(BtnState s, string text = null)
        {
            state = s;
            if (text != null) busyText = text;
            target = s == BtnState.Ready ? 0f : (s == BtnState.Busy ? 1f : 0.6f);
            Cursor = s == BtnState.Ready ? Cursors.Hand : (s == BtnState.Busy ? Cursors.WaitCursor : Cursors.No);
            anim.Start();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

            int size = Math.Min(Width, Height);
            float pad = size * 0.06f;
            float shrink = size * 0.05f * press;
            float lift = size * 0.035f * (1 - press);
            RectangleF r = new RectangleF(pad + shrink, pad + shrink, size - 2 * (pad + shrink), size - 2 * (pad + shrink) - lift);

            Color top, bottom, edge;
            if (state == BtnState.Busy) { top = Color.FromArgb(235, 80, 70); bottom = Color.FromArgb(185, 35, 35); edge = Color.FromArgb(140, 25, 25); }
            else if (state == BtnState.Wired) { top = Color.FromArgb(80, 140, 230); bottom = Color.FromArgb(40, 95, 190); edge = Color.FromArgb(30, 70, 150); }
            else { top = Color.FromArgb(52, 199, 105); bottom = Color.FromArgb(26, 150, 75); edge = Color.FromArgb(18, 110, 55); }

            bool hot = hover && state == BtnState.Ready && !down;
            if (hot)
            {
                // 마우스 올리면 살짝 밝아지고 둘레에 은은한 빛
                top = Light(top, 0.15f); bottom = Light(bottom, 0.12f);
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(45, 52, 199, 105)))
                    g.FillEllipse(glow, r.X - size * 0.035f, r.Y - size * 0.035f, r.Width + size * 0.07f, r.Height + size * 0.07f + lift);
            }

            if (lift > 0.5f)
                using (SolidBrush sb = new SolidBrush(edge)) g.FillEllipse(sb, r.X, r.Y + lift, r.Width, r.Height);
            using (LinearGradientBrush lb = new LinearGradientBrush(r, press > 0.5f ? bottom : top, press > 0.5f ? top : bottom, 90f))
                g.FillEllipse(lb, r);
            if (press > 0.05f)
                using (Pen p = new Pen(Color.FromArgb((int)(90 * press), 0, 0, 0), size * 0.025f))
                    g.DrawEllipse(p, r.X + size * 0.012f, r.Y + size * 0.012f, r.Width - size * 0.024f, r.Height - size * 0.024f);

            string text = state == BtnState.Busy ? busyText : (state == BtnState.Wired ? "유선 사용중" : "변경");
            float fs = state == BtnState.Wired ? 0.11f : 0.14f;
            using (Font f = new Font("Malgun Gothic", size * fs, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, f, Brushes.White, r, sf);
        }
    }

    // ---------- 상단 스위치 (유선 모뎀 / 테더링) ----------
    class ModeSwitch : Control
    {
        string[] names = { "유선 모뎀", "테더링" };
        int sel = 0;
        public event Action<int> Clicked;

        public ModeSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public int Selected { get { return sel; } set { sel = value; Invalidate(); } }

        int hover = -1, down = -1;

        // 전환 중인 쪽 (-1 = 없음). 전환이 끝나야 Selected가 옮겨감
        int pending = -1;
        float pulse = 0f;
        System.Windows.Forms.Timer pulseTimer;
        public int Pending
        {
            get { return pending; }
            set
            {
                pending = value;
                if (pulseTimer == null)
                {
                    pulseTimer = new System.Windows.Forms.Timer { Interval = 40 };
                    pulseTimer.Tick += delegate { pulse += 0.12f; Invalidate(); };
                }
                if (value >= 0) pulseTimer.Start(); else pulseTimer.Stop();
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = e.X < Width / 2 ? 0 : 1;
            if (h != hover) { hover = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; down = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!Enabled || e.Button != MouseButtons.Left) return;
            down = e.X < Width / 2 ? 0 : 1; Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            int d = down; down = -1; Invalidate();
            if (!Enabled || d < 0) return;
            int i = e.X < Width / 2 ? 0 : 1;
            if (i == d && i != sel && Clicked != null) Clicked(i);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        static Color Lighten(Color c, float k) { return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * k), (int)(c.G + (255 - c.G) * k), (int)(c.B + (255 - c.B) * k)); }
        static Color Darken(Color c, float k) { return Color.FromArgb(c.A, (int)(c.R * (1 - k)), (int)(c.G * (1 - k)), (int)(c.B * (1 - k))); }

        static GraphicsPath Pill(RectangleF r, float rad, bool leftRound, bool rightRound)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            if (leftRound) { p.AddArc(r.X, r.Y, d, d, 180, 90); }
            else p.AddLine(r.X, r.Y, r.X, r.Y);
            if (rightRound) { p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); }
            else { p.AddLine(r.Right, r.Y, r.Right, r.Bottom); }
            if (leftRound) p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            else p.AddLine(r.X, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);

            RectangleF all = new RectangleF(1, 1, Width - 3, Height - 3);
            float rad = all.Height / 2;
            float half = all.Width / 2;
            Color blue = Color.FromArgb(37, 99, 235), red = Color.FromArgb(220, 60, 60);
            for (int i = 0; i < 2; i++)
            {
                RectangleF r = new RectangleF(all.X + half * i, all.Y, half, all.Height);
                bool on = i == sel;
                bool hov = Enabled && hover == i;
                bool pressed = Enabled && down == i;
                Color c = on ? blue : red;
                int alpha = on ? 255 : (Enabled ? (hov ? 230 : 170) : 110);
                if (!Enabled && on) alpha = 160;
                if (hov && !pressed) c = Lighten(c, 0.15f);      // 마우스 올리면 살짝 밝게
                if (pressed) c = Darken(c, 0.2f);                 // 누르면 어둡게
                bool isPending = i == pending;
                if (isPending)
                {
                    // 전환중: 주황색으로 천천히 깜빡임
                    float k = (float)(0.5 + 0.5 * Math.Sin(pulse));
                    c = Color.FromArgb((int)(245 - 30 * k), (int)(150 - 40 * k), (int)(30 + 10 * k));
                    alpha = 255;
                }
                else if (pending >= 0 && on) alpha = 255;
                using (GraphicsPath p = Pill(r, rad, i == 0, i == 1))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, c))) g.FillPath(b, p);
                    if (pressed)   // 눌린 느낌: 안쪽 위 그림자
                        using (Pen ip = new Pen(Color.FromArgb(90, 0, 0, 0), 3f)) { g.SetClip(p); g.DrawPath(ip, p); g.ResetClip(); }
                    else if (hov)  // 마우스 올리면 하얀 테두리
                        using (Pen hp = new Pen(Color.FromArgb(150, 255, 255, 255), 1.5f)) { g.SetClip(p); g.DrawPath(hp, p); g.ResetClip(); }
                }
                RectangleF tr = pressed ? new RectangleF(r.X, r.Y + 1.5f, r.Width, r.Height) : r;
                using (Font f = new Font("Malgun Gothic", Height * 0.33f, on ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(isPending ? "전환중" + new string('.', 1 + (int)(pulse / 1.5f) % 3) : (on ? "● " : "") + names[i], f, Brushes.White, tr, sf);
            }
            using (Pen pen = new Pen(Color.White, 2)) g.DrawLine(pen, all.X + half, all.Y, all.X + half, all.Bottom);
        }
    }

    // ---------- 작업표시줄 상태 상자 (알림 아이콘 바로 왼쪽) ----------
    class StatusBox : Form
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        string text = "";
        Color color = Color.Gray;
        float sc;
        public event Action Clicked;
        Point downAt; bool dragging, mouseDown;

        public StatusBox(float scale)
        {
            sc = scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Gray;
            Font = new Font("Malgun Gothic", 9f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("창 열기", null, delegate { if (Clicked != null) Clicked(); });
            m.Items.Add("상자 위치 처음으로", null, delegate { SaveOffset(null); Tick(); });
            ContextMenuStrip = m;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8;   // 도구창(Alt+Tab 안 뜸) + 포커스 안 뺏음 + 항상 위
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        int S(float v) { return (int)Math.Round(v * sc); }

        public void SetStatus(string t, Color c)
        {
            if (t == text && c == color) return;
            text = t; color = c;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush b = new SolidBrush(color)) g.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, Font, Brushes.White, ClientRectangle, sf);
        }

        // 끌어서 옮기기 / 클릭하면 창 열기
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            mouseDown = true; dragging = false; downAt = Cursor.Position;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!mouseDown) return;
            Point p = Cursor.Position;
            if (!dragging && (Math.Abs(p.X - downAt.X) > 4 || Math.Abs(p.Y - downAt.Y) > 4)) dragging = true;
            if (dragging) { Left += p.X - downAt.X; Top += p.Y - downAt.Y; downAt = p; }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!mouseDown) return;
            mouseDown = false;
            if (dragging)
            {
                RECT tb;
                IntPtr t = FindWindow("Shell_TrayWnd", null);
                if (t != IntPtr.Zero && GetWindowRect(t, out tb)) SaveOffset((tb.R - Right) + "," + (Top - tb.T));
            }
            else if (Clicked != null) Clicked();
            dragging = false;
        }

        static string OffFile() { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IpChanger"), "boxpos.txt"); }
        static void SaveOffset(string v)
        {
            try { if (v == null) File.Delete(OffFile()); else { Directory.CreateDirectory(Path.GetDirectoryName(OffFile())); File.WriteAllText(OffFile(), v); } } catch { }
        }
        static int[] LoadOffset()
        {
            try { string[] p = File.ReadAllText(OffFile()).Split(','); return new[] { int.Parse(p[0]), int.Parse(p[1]) }; } catch { return null; }
        }

        bool FullscreenAppActive()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Handle) return false;
            StringBuilder sb = new StringBuilder(64); GetClassName(fg, sb, 64);
            string cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            RECT r; if (!GetWindowRect(fg, out r)) return false;
            Rectangle scr = Screen.FromHandle(fg).Bounds;
            return r.L <= scr.Left && r.T <= scr.Top && r.R >= scr.Right && r.B >= scr.Bottom;
        }

        // 1초마다 위치 맞추고 항상 맨 위로
        public void Tick()
        {
            if (mouseDown) return;
            IntPtr tbw = FindWindow("Shell_TrayWnd", null);
            RECT tb;
            if (tbw == IntPtr.Zero || !GetWindowRect(tbw, out tb)) { if (Visible) Hide(); return; }

            if (FullscreenAppActive()) { if (Visible) Hide(); return; }   // 전체화면 게임·영상 땐 숨김
            if (!Visible) Show();

            bool horizontal = (tb.R - tb.L) > (tb.B - tb.T);
            int tbH = tb.B - tb.T;
            int h = horizontal ? Math.Max(S(22), Math.Min(S(30), tbH - S(12))) : S(26);
            int w;
            using (Graphics g = CreateGraphics()) w = (int)g.MeasureString(text, Font).Width + S(18);
            if (!horizontal) w = Math.Min(w, Math.Max(S(60), tb.R - tb.L - S(6)));

            int x, y;
            int[] off = LoadOffset();
            if (off != null) { x = tb.R - off[0] - w; y = tb.T + off[1]; }
            else
            {
                RECT nr = new RECT();
                IntPtr nw = FindWindowEx(tbw, IntPtr.Zero, "TrayNotifyWnd", null);
                bool hasTray = nw != IntPtr.Zero && GetWindowRect(nw, out nr) && nr.R - nr.L > 0;
                if (!hasTray) { nr.L = tb.R - S(240); nr.T = tb.B - S(120); nr.R = tb.R; nr.B = tb.B; }
                if (horizontal) { x = nr.L - w - S(6); y = tb.T + (tbH - h) / 2; }
                else { x = tb.L + (tb.R - tb.L - w) / 2; y = nr.T - h - S(6); }
            }
            SetWindowPos(Handle, new IntPtr(-1), x, y, w, h, 0x0010 | 0x0040);   // 항상 위, 포커스 안 뺏음
        }
    }

    // ---------- 어댑터 정보 ----------
    class AdapterInfo
    {
        public string Name, Desc;
        public bool Enabled, Connected;
        public override string ToString() { return Name + "  (" + Desc + ")" + (Enabled ? "" : " · 사용 안 함"); }
    }

    // ---------- 메인 창 ----------
    class MainForm : Form
    {
        RoundButton btn;
        ModeSwitch sw;
        Label lblModemSt, lblTetherSt, lblTitle, lblPhone, lblPing, lblCurIp, lblNewIp, lblSettings;
        TextBox log;
        string adb;
        volatile string phoneState = "";
        volatile bool busy;
        volatile bool tetherMode;        // false = 유선 모뎀 사용중, true = 테더링 사용중
        string modemName, phoneName;
        string lastNewIp;
        System.Threading.Timer poller, pinger;
        float sc = 1f;
        int tick;

        int S(float v) { return (int)Math.Round(v * sc); }

        public static bool IsAdmin()
        {
            try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        NotifyIcon tray;
        StatusBox box;
        bool reallyExit, trayTipShown, justUpdated;
        EventWaitHandle showSignal;
        System.Windows.Forms.Timer boxTimer;
        volatile int lastPingMs = -1;     // -1 = 아직 없음, -2 = 응답 없음

        public static string Version
        {
            get { Version v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public MainForm(bool updated)
        {
            justUpdated = updated;
            using (Graphics g = CreateGraphics()) sc = g.DpiX / 96f;

            Text = "아이피 변경";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 247, 249);
            Font = new Font("Malgun Gothic", 9f);
            ClientSize = new Size(S(440), S(690));
            int W = ClientSize.Width;

            // 상단 스위치
            sw = new ModeSwitch();
            sw.SetBounds(S(60), S(14), S(320), S(42));
            sw.Clicked += i => AskSwitch(i == 1);

            lblSettings = MakeLabel("설정", 8.5f, FontStyle.Underline, Color.Gray);
            lblSettings.SetBounds(S(388), S(24), S(44), S(22));
            lblSettings.TextAlign = ContentAlignment.MiddleCenter;
            lblSettings.Cursor = Cursors.Hand;
            lblSettings.Click += delegate { if (!busy) ShowSettings(); };

            lblModemSt = MakeLabel("확인 중...", 8.5f, FontStyle.Regular, Color.Gray);
            lblModemSt.SetBounds(S(60), S(58), S(160), S(20)); lblModemSt.TextAlign = ContentAlignment.MiddleCenter;
            lblTetherSt = MakeLabel("확인 중...", 8.5f, FontStyle.Regular, Color.Gray);
            lblTetherSt.SetBounds(S(220), S(58), S(160), S(20)); lblTetherSt.TextAlign = ContentAlignment.MiddleCenter;

            lblTitle = MakeLabel("아이피 변경", 15f, FontStyle.Bold, Color.FromArgb(30, 30, 30));
            lblTitle.SetBounds(0, S(86), W, S(34)); lblTitle.TextAlign = ContentAlignment.MiddleCenter;

            lblPhone = MakeLabel("폰 연결 확인 중...", 9.5f, FontStyle.Regular, Color.Gray);
            lblPhone.SetBounds(0, S(120), W, S(22)); lblPhone.TextAlign = ContentAlignment.MiddleCenter;

            lblPing = MakeLabel("", 9.5f, FontStyle.Bold, Color.Gray);
            lblPing.SetBounds(0, S(142), W, S(22)); lblPing.TextAlign = ContentAlignment.MiddleCenter;

            Panel left = MakeCard(S(16), S(172), S(185), S(70));
            Label c1 = MakeLabel("현재 아이피", 9f, FontStyle.Regular, Color.Gray);
            c1.SetBounds(0, S(8), left.Width, S(20)); c1.TextAlign = ContentAlignment.MiddleCenter;
            lblCurIp = MakeLabel("-", 12f, FontStyle.Bold, Color.FromArgb(30, 30, 30));
            lblCurIp.SetBounds(0, S(30), left.Width, S(30)); lblCurIp.TextAlign = ContentAlignment.MiddleCenter;
            left.Controls.Add(c1); left.Controls.Add(lblCurIp);

            Label arrow = MakeLabel("→", 14f, FontStyle.Bold, Color.Silver);
            arrow.SetBounds(S(201), S(187), S(38), S(40)); arrow.TextAlign = ContentAlignment.MiddleCenter;

            Panel right = MakeCard(S(239), S(172), S(185), S(70));
            Label c2 = MakeLabel("변경된 아이피", 9f, FontStyle.Regular, Color.Gray);
            c2.SetBounds(0, S(8), right.Width, S(20)); c2.TextAlign = ContentAlignment.MiddleCenter;
            lblNewIp = MakeLabel("-", 12f, FontStyle.Bold, Color.FromArgb(26, 150, 75));
            lblNewIp.SetBounds(0, S(30), right.Width, S(30)); lblNewIp.TextAlign = ContentAlignment.MiddleCenter;
            right.Controls.Add(c2); right.Controls.Add(lblNewIp);

            btn = new RoundButton();
            btn.BackColor = BackColor;
            btn.SetBounds((W - S(190)) / 2, S(254), S(190), S(190));
            btn.Click += delegate { StartChange(); };

            Label logCap = MakeLabel("상태 로그", 9f, FontStyle.Bold, Color.DimGray);
            logCap.SetBounds(S(16), S(456), S(200), S(20));

            log = new TextBox();
            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = Color.FromArgb(28, 30, 34); log.ForeColor = Color.FromArgb(200, 230, 210);
            log.BorderStyle = BorderStyle.None;
            log.Font = new Font("Consolas", 9f);
            log.SetBounds(S(16), S(478), W - S(32), ClientSize.Height - S(494));

            Controls.AddRange(new Control[] { sw, lblSettings, lblModemSt, lblTetherSt, lblTitle, lblPhone, lblPing, left, arrow, right, btn, logCap, log });

            modemName = LoadSetting("modem");
            phoneName = LoadSetting("phone");
            tetherMode = LoadSetting("mode") == "tether";
            ApplyModeUi();
            sw.Enabled = false;

            // ---- 알림 영역(트레이) 아이콘 ----
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("열기", null, delegate { ShowMain(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { ExitApp(); });
            tray = new NotifyIcon { Icon = Icon, Text = "아이피 변경", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

            // X 눌러도 꺼지지 않고 알림 영역으로
            FormClosing += (s, e) =>
            {
                if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    if (!trayTipShown)
                    {
                        trayTipShown = true;
                        tray.ShowBalloonTip(3000, "아이피 변경", "알림 영역에서 계속 실행 중이에요. 끄려면 아이콘 우클릭 → 종료", ToolTipIcon.Info);
                    }
                    return;
                }
                if (poller != null) poller.Dispose();
                if (pinger != null) pinger.Dispose();
                if (updTimer != null) updTimer.Dispose();
                if (boxTimer != null) boxTimer.Stop();
                if (box != null) box.Close();
                tray.Visible = false; tray.Dispose();
            };

            // 다시 실행하면 이미 켜진 창을 띄움
            try
            {
                showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "IpChanger_Show");
                new Thread(() => { while (true) { showSignal.WaitOne(); Ui(ShowMain); } }) { IsBackground = true }.Start();
            }
            catch { }

            // ---- 작업표시줄 상태 상자 ----
            box = new StatusBox(sc);
            box.Clicked += ShowMain;
            boxTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            boxTimer.Tick += delegate { UpdateBox(); };

            Shown += delegate
            {
                box.Show();
                UpdateBox();
                boxTimer.Start();
                if (justUpdated) tray.ShowBalloonTip(4000, "아이피 변경", "v" + Version + "(으)로 업데이트했어요", ToolTipIcon.Info);
                new Thread(Init) { IsBackground = true }.Start();
            };
        }

        void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void ExitApp()
        {
            reallyExit = true;
            Close();
        }

        // 작업표시줄 상자 내용 갱신
        void UpdateBox()
        {
            string ping = lastPingMs >= 0 ? lastPingMs + "ms" : (lastPingMs == -2 ? "응답 없음" : "--");
            string text; Color c;
            if (busy && sw.Pending >= 0) { text = "전환중..."; c = Color.FromArgb(230, 130, 20); }
            else if (busy) { text = "IP 변경중..."; c = Color.FromArgb(230, 130, 20); }
            else if (lastPingMs == -2) { text = (tetherMode ? "테더링" : "유선") + " · 응답 없음"; c = Color.FromArgb(205, 45, 45); }
            else if (tetherMode) { text = "테더링 · " + ping; c = Color.FromArgb(124, 58, 237); }
            else { text = "유선 접속중 · " + ping; c = Color.FromArgb(26, 150, 75); }
            box.SetStatus(text, c);
            box.Tick();
            if (tray != null)
            {
                string tip = "아이피 변경 — " + text;
                tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            }
        }

        // ---------- 자동 업데이트 (GitHub 릴리스) ----------
        const string RepoApi = "https://api.github.com/repos/kakario777-netizen/ip-changer/releases/latest";
        System.Threading.Timer updTimer;
        int updating;

        void StartUpdater()
        {
            // 켜고 30초 뒤 한 번, 이후 3시간마다
            updTimer = new System.Threading.Timer(_ => CheckUpdate(), null, 30000, 3 * 3600 * 1000);
        }

        void CheckUpdate()
        {
            // 테더링 데이터를 아끼기 위해 유선 모뎀 사용 중일 때만 확인/다운로드
            if (tetherMode || busy) return;
            if (Interlocked.Exchange(ref updating, 1) == 1) return;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(RepoApi);
                req.UserAgent = "ip-changer-updater";
                req.Accept = "application/vnd.github+json";
                req.Timeout = 10000;
                string json;
                using (WebResponse res = req.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();

                var tm = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                var um = System.Text.RegularExpressions.Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.exe)\"");
                if (!tm.Success || !um.Success) return;
                // 내 저장소 릴리스의 HTTPS 주소에서만 받음
                if (!um.Groups[1].Value.StartsWith("https://github.com/kakario777-netizen/ip-changer/releases/download/")) { Log("업데이트 주소가 올바르지 않아 건너뜀"); return; }
                Version latest = new Version(tm.Groups[1].Value), cur = new Version(Version);
                if (latest <= cur) return;

                Log("새 버전 v" + latest + " 받는 중...");
                string exe = Application.ExecutablePath;
                string tmp = exe + ".new";
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ip-changer-updater");
                    wc.DownloadFile(um.Groups[1].Value, tmp);
                }
                if (new FileInfo(tmp).Length < 100000) { File.Delete(tmp); Log("업데이트 파일이 올바르지 않아 건너뜀"); return; }

                // 작업 중이면 끝날 때까지 기다렸다가 교체
                while (busy) Thread.Sleep(1000);

                // 실행 중인 exe는 이름 변경이 가능 → 바꿔치기 후 새 버전으로 다시 시작
                string old = exe + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                File.Move(tmp, exe);
                Log("v" + latest + " 업데이트 완료 → 다시 시작");
                Ui(() =>
                {
                    try { Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true }); }
                    catch { try { File.Move(exe, tmp); File.Move(old, exe); } catch { } Log("재시작 실패"); return; }
                    ExitApp();
                });
            }
            catch (Exception ex) { Log("업데이트 확인 실패: " + ex.Message); }
            finally { Interlocked.Exchange(ref updating, 0); }
        }

        Label MakeLabel(string t, float size, FontStyle st, Color c)
        {
            return new Label { Text = t, Font = new Font("Malgun Gothic", size, st), ForeColor = c, BackColor = Color.Transparent, AutoSize = false };
        }

        Panel MakeCard(int x, int y, int w, int h)
        {
            Panel p = new Panel { BackColor = Color.White };
            p.SetBounds(x, y, w, h);
            p.Paint += (s, e) => { using (Pen pen = new Pen(Color.FromArgb(225, 228, 232))) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); };
            return p;
        }

        void Ui(Action a) { if (IsDisposed) return; try { if (InvokeRequired) BeginInvoke(a); else a(); } catch { } }

        void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n";
            Ui(() => log.AppendText(line));
        }

        void SetLbl(Label l, string t, Color c) { Ui(() => { l.Text = t; l.ForeColor = c; }); }

        static readonly Color GREEN = Color.FromArgb(26, 150, 75), ORANGE = Color.FromArgb(220, 130, 20), RED = Color.FromArgb(210, 50, 50);

        // ---------- 시작 ----------
        void Init()
        {
            Log("프로그램 시작 (v" + Version + ")");
            StartUpdater();
            if (!IsAdmin()) Log("관리자 권한이 없어 유선/테더링 전환이 안 돼요 (다시 실행 후 '예')");

            try { adb = EnsureAdb(); }
            catch (Exception ex) { Log("ADB 준비 실패: " + ex.Message); SetLbl(lblPhone, "ADB 준비 실패 (백신 차단 여부 확인)", RED); }
            if (adb != null) { Log("ADB 서버 시작"); Run("start-server", 15000); }

            DetectAdapters(true);

            // 실제 네트워크 상태로 현재 모드 판단
            AdapterInfo m = GetAdapter(modemName), p = GetAdapter(phoneName);
            if (m != null && p != null)
            {
                if (m.Enabled && !p.Enabled) tetherMode = false;
                else if (p.Enabled && !m.Enabled) tetherMode = true;
            }
            else if (m == null && p != null && p.Enabled) tetherMode = true;
            SaveSetting("mode", tetherMode ? "tether" : "modem");
            Ui(() => { ApplyModeUi(); sw.Enabled = true; });
            Log("현재 사용: " + (tetherMode ? "테더링" : "유선 모뎀"));

            poller = new System.Threading.Timer(_ => Poll(), null, 0, 1000);
            pinger = new System.Threading.Timer(_ => PingOnce(), null, 1000, 3000);

            string ip = GetPublicIp(6);
            Ui(() => { lblCurIp.Text = ip ?? "확인 실패"; });
            Log("현재 아이피: " + (ip ?? "확인 실패"));
        }

        void ApplyModeUi()
        {
            sw.Selected = tetherMode ? 1 : 0;
            if (busy) return;
            if (tetherMode) { btn.SetState(BtnState.Ready); Text = "아이피 변경 (테더링 사용중)"; }
            else { btn.SetState(BtnState.Wired); Text = "아이피 변경 (유선 모뎀 사용중)"; }
        }

        // ---------- 네트워크 어댑터 ----------
        List<AdapterInfo> ListAdapters()
        {
            List<AdapterInfo> list = new List<AdapterInfo>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT NetConnectionID, Description, NetEnabled, NetConnectionStatus FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        AdapterInfo a = new AdapterInfo();
                        a.Name = Convert.ToString(o["NetConnectionID"]);
                        a.Desc = Convert.ToString(o["Description"]);
                        a.Enabled = o["NetEnabled"] != null && (bool)o["NetEnabled"];
                        a.Connected = o["NetConnectionStatus"] != null && Convert.ToInt32(o["NetConnectionStatus"]) == 2;
                        string d = a.Desc.ToLower();
                        if (d.Contains("virtual") || d.Contains("vmware") || d.Contains("hyper-v") || d.Contains("bluetooth") ||
                            d.Contains("wan miniport") || d.Contains("tap-") || d.Contains("vpn") || d.Contains("loopback") ||
                            d.Contains("kernel debug") || d.Contains("teredo") || d.Contains("isatap") || d.Contains("npcap") ||
                            d.Contains("zerotier") || d.Contains("tailscale") || d.Contains("wintun") || d.Contains("wireguard") ||
                            d.Contains("docker") || d.Contains("virtualbox") || d.Contains("wi-fi direct") || d.Contains("microsoft") && d.Contains("adapter") && !d.Contains("ethernet")) continue;
                        list.Add(a);
                    }
                }
            }
            catch { }
            return list;
        }

        AdapterInfo GetAdapter(string name)
        {
            if (name == null) return null;
            foreach (AdapterInfo a in ListAdapters()) if (a.Name == name) return a;
            return null;
        }

        static bool IsPhoneDesc(string d)
        {
            d = d.ToLower();
            return d.Contains("ndis") || d.Contains("ncm") || d.Contains("android") || d.Contains("samsung") || d.Contains("usb tether");
        }

        static bool IsWireless(string d)
        {
            d = d.ToLower();
            return d.Contains("wi-fi") || d.Contains("wifi") || d.Contains("wireless") || d.Contains("802.11") || d.Contains("wlan");
        }

        void DetectAdapters(bool logIt)
        {
            List<AdapterInfo> list = ListAdapters();
            if (phoneName == null)
                foreach (AdapterInfo a in list) if (IsPhoneDesc(a.Desc)) { phoneName = a.Name; SaveSetting("phone", phoneName); break; }
            if (modemName == null)
            {
                AdapterInfo best = null;
                foreach (AdapterInfo a in list)
                {
                    if (a.Name == phoneName || IsPhoneDesc(a.Desc) || IsWireless(a.Desc)) continue;
                    if (best == null || (a.Connected && !best.Connected)) best = a;
                }
                if (best != null) { modemName = best.Name; SaveSetting("modem", modemName); }
            }
            if (logIt)
            {
                Log("유선 모뎀: " + (modemName ?? "못 찾음"));
                Log("테더링(폰): " + (phoneName ?? "못 찾음 (폰 테더링을 켜고 '설정'에서 선택)"));
            }
        }

        NetworkInterface FindNi(string name)
        {
            try { foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) if (ni.Name == name) return ni; } catch { }
            return null;
        }

        bool AdapterReady(string name)
        {
            NetworkInterface ni = FindNi(name);
            if (ni == null || ni.OperationalStatus != OperationalStatus.Up) return false;
            try
            {
                foreach (GatewayIPAddressInformation g in ni.GetIPProperties().GatewayAddresses)
                    if (g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) return true;
            }
            catch { }
            return false;
        }

        bool SetAdapter(string name, bool enable)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh", "interface set interface name=\"" + name + "\" admin=" + (enable ? "enabled" : "disabled"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        bool WaitReady(string name, int seconds, string what)
        {
            for (int i = 0; i < seconds; i++)
            {
                if (AdapterReady(name)) return true;
                if (i > 0 && i % 10 == 0) Log(what + " 신호 기다리는 중... (" + i + "초)");
                Thread.Sleep(1000);
            }
            return false;
        }

        // ---------- 유선 / 테더링 전환 ----------
        void AskSwitch(bool toTether)
        {
            if (busy) return;
            string q = toTether ? "테더링을 사용하시겠습니까?" : "유선 모뎀을 사용하시겠습니까?";
            if (MessageBox.Show(this, q, "네트워크 전환", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (!IsAdmin()) { Log("관리자 권한이 없어 전환할 수 없어요 (프로그램을 다시 켜고 '예')"); return; }
            if (modemName == null || phoneName == null) { Log("유선 모뎀/테더링 네트워크를 '설정'에서 먼저 선택해 주세요"); ShowSettings(); return; }

            busy = true;
            sw.Enabled = false;
            sw.Pending = toTether ? 1 : 0;
            btn.SetState(BtnState.Busy, "전환중");
            new Thread(() => DoSwitch(toTether)) { IsBackground = true }.Start();
        }

        void DoSwitch(bool toTether)
        {
            string onName = toTether ? phoneName : modemName;
            string offName = toTether ? modemName : phoneName;
            string onWhat = toTether ? "테더링" : "유선 모뎀";
            string offWhat = toTether ? "유선 모뎀" : "테더링";
            bool ok = false;
            try
            {
                Log("──────── " + onWhat + "(으)로 전환 ────────");
                if (toTether && adb != null && phoneState == "device" && !TetherOn())
                {
                    Log("폰 테더링이 꺼져 있어요 → 폰에서 USB 테더링을 켜주세요 (전환 취소, 유선 유지)");
                    return;
                }

                // 1) 새로 쓸 쪽을 먼저 켬 (끊김 없게)
                AdapterInfo a = GetAdapter(onName);
                bool wasEnabled = a != null && a.Enabled;
                if (!wasEnabled)
                {
                    Log(onWhat + " 사용함으로 변경 중...");
                    if (!SetAdapter(onName, true)) Log(onWhat + " 사용함 변경 실패");
                }

                // 2) 신호 들어올 때까지 기다림
                Log(onWhat + " 신호 기다리는 중...");
                if (!WaitReady(onName, 45, onWhat))
                {
                    Log(onWhat + " 신호가 안 들어와서 전환을 취소했어요 (" + offWhat + " 계속 사용)");
                    if (!wasEnabled) SetAdapter(onName, false);
                    return;
                }
                Log(onWhat + " 신호 확인");
                Thread.Sleep(1500);

                // 3) 그때 기존 쪽을 사용 안 함 (폰 테더링 자체는 끄지 않음)
                Log(offWhat + " 사용 안 함으로 변경 중..." + (toTether ? "" : " (폰 테더링은 그대로 켜둠)"));
                if (!SetAdapter(offName, false)) Log(offWhat + " 사용 안 함 변경 실패 (둘 다 사용 중)");
                ok = true;
                Log(onWhat + " 사용으로 전환 완료");
            }
            catch (Exception ex) { Log("오류: " + ex.Message); }
            finally
            {
                if (ok) { tetherMode = toTether; SaveSetting("mode", toTether ? "tether" : "modem"); ResetPing(); }
                Ui(() => { busy = false; sw.Pending = -1; sw.Enabled = true; ApplyModeUi(); });
                if (ok)
                {
                    string ip = GetPublicIp(5);
                    Ui(() => { lblCurIp.Text = ip ?? "확인 실패"; lblNewIp.Text = "-"; lastNewIp = null; });
                    Log("현재 아이피: " + (ip ?? "확인 실패"));
                }
            }
        }

        // ---------- 설정 (어댑터 선택) ----------
        // 어댑터 상태: 0 인터넷 됨(초록) / 1 연결됐지만 인터넷 없음(주황) / 2 케이블·신호 없음(회색) / 3 사용 안 함(회색)
        int AdapterRank(AdapterInfo a, out string text, out Color color)
        {
            NetworkInterface ni = FindNi(a.Name);
            if (ni == null && !a.Enabled) { text = "사용 안 함"; color = Color.Gray; return 3; }
            if (ni != null && ni.OperationalStatus == OperationalStatus.Up)
            {
                if (AdapterReady(a.Name))
                {
                    string ip = "";
                    try { foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                            if (u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { ip = " · " + u.Address; break; } } catch { }
                    text = "● 인터넷 연결됨" + ip; color = GREEN; return 0;
                }
                text = "연결됨 (인터넷 없음)"; color = ORANGE; return 1;
            }
            text = a.Enabled ? "케이블/신호 없음" : "사용 안 함"; color = Color.Gray; return a.Enabled ? 2 : 3;
        }

        static string KindOf(AdapterInfo a)
        {
            if (IsPhoneDesc(a.Desc)) return "폰 테더링";
            if (IsWireless(a.Desc)) return "와이파이";
            return "유선랜";
        }

        ListBox MakeAdapterList(List<AdapterInfo> list, string selName, bool phoneFirst)
        {
            ListBox lb = new ListBox { DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = S(42), IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
            List<AdapterInfo> sorted = new List<AdapterInfo>(list);
            sorted.Sort((x, y) =>
            {
                string t; Color c;
                int kx = phoneFirst ? (IsPhoneDesc(x.Desc) ? 0 : 1) : (KindOf(x) == "유선랜" ? 0 : 1);
                int ky = phoneFirst ? (IsPhoneDesc(y.Desc) ? 0 : 1) : (KindOf(y) == "유선랜" ? 0 : 1);
                int rx = AdapterRank(x, out t, out c), ry = AdapterRank(y, out t, out c);
                if (rx != ry) return rx.CompareTo(ry);   // 인터넷 되는 것 먼저
                return kx.CompareTo(ky);
            });
            foreach (AdapterInfo a in sorted) { lb.Items.Add(a); if (a.Name == selName) lb.SelectedItem = a; }

            lb.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                AdapterInfo a = (AdapterInfo)lb.Items[e.Index];
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush bg = new SolidBrush(selected ? Color.FromArgb(220, 232, 252) : Color.White)) g.FillRectangle(bg, e.Bounds);
                if (selected) using (SolidBrush bar = new SolidBrush(Color.FromArgb(37, 99, 235))) g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, S(4), e.Bounds.Height);

                string st; Color sc2;
                int rank = AdapterRank(a, out st, out sc2);
                int x = e.Bounds.X + S(12), y = e.Bounds.Y;
                using (SolidBrush dot = new SolidBrush(rank == 0 ? GREEN : rank == 1 ? ORANGE : Color.Silver))
                    g.FillEllipse(dot, x, y + S(15), S(12), S(12));
                x += S(20);

                string kind = KindOf(a);
                using (Font fb = new Font("Malgun Gothic", 9.5f, FontStyle.Bold))
                using (Font fk = new Font("Malgun Gothic", 8f, FontStyle.Bold))
                using (Font fs = new Font("Malgun Gothic", 8f))
                {
                    SizeF ns = g.MeasureString(a.Name, fb);
                    g.DrawString(a.Name, fb, rank == 0 ? Brushes.Black : Brushes.DimGray, x, y + S(3));
                    Color kc = kind == "유선랜" ? Color.FromArgb(37, 99, 235) : kind == "폰 테더링" ? Color.FromArgb(150, 80, 200) : Color.Teal;
                    RectangleF kr = new RectangleF(x + ns.Width + S(4), y + S(5), g.MeasureString(kind, fk).Width + S(6), S(17));
                    using (SolidBrush kb = new SolidBrush(kc)) g.FillRectangle(kb, kr);
                    g.DrawString(kind, fk, Brushes.White, kr.X + S(3), kr.Y + S(1));
                    using (SolidBrush sb = new SolidBrush(sc2)) g.DrawString(st, fs, sb, x, y + S(23));
                    float sw2 = g.MeasureString(st, fs).Width;
                    g.DrawString("·  " + a.Desc, fs, Brushes.Gray, new RectangleF(x + sw2 + S(4), y + S(23), e.Bounds.Right - x - sw2 - S(8), S(18)),
                        new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                }
                using (Pen ln = new Pen(Color.FromArgb(235, 236, 240))) g.DrawLine(ln, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };
            return lb;
        }

        void ShowSettings()
        {
            List<AdapterInfo> list = ListAdapters();
            using (Form f = new Form())
            {
                f.Text = "네트워크 선택";
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.Font = new Font("Malgun Gothic", 9f);
                f.BackColor = Color.FromArgb(246, 247, 249);
                f.ClientSize = new Size(S(480), S(560));

                Label tip = new Label { AutoSize = false, ForeColor = Color.DimGray,
                    Text = "초록색 = 지금 인터넷이 되는 연결이에요.\r\n헷갈리면 랜선을 잠깐 뺐다 꽂아보세요. 상태가 바로 바뀌는 게 실제 유선랜이에요." };
                tip.SetBounds(S(16), S(10), S(450), S(36));

                Label l1 = new Label { Text = "유선 모뎀", AutoSize = false, Font = new Font("Malgun Gothic", 10f, FontStyle.Bold) };
                l1.SetBounds(S(16), S(52), S(440), S(22));
                ListBox lm = MakeAdapterList(list, modemName, false);
                lm.SetBounds(S(16), S(76), S(448), S(196));

                Label l2 = new Label { Text = "테더링 (폰)  — 폰 테더링이 켜져 있어야 목록에 보여요", AutoSize = false, Font = new Font("Malgun Gothic", 10f, FontStyle.Bold) };
                l2.SetBounds(S(16), S(282), S(440), S(22));
                ListBox lp = MakeAdapterList(list, phoneName, true);
                lp.SetBounds(S(16), S(306), S(448), S(196));

                Button okb = new Button { Text = "확인", DialogResult = DialogResult.OK }; okb.SetBounds(S(290), S(516), S(84), S(30));
                Button cb = new Button { Text = "취소", DialogResult = DialogResult.Cancel }; cb.SetBounds(S(380), S(516), S(84), S(30));
                f.Controls.AddRange(new Control[] { tip, l1, lm, l2, lp, okb, cb });
                f.AcceptButton = okb; f.CancelButton = cb;

                // 1초마다 상태 다시 그림 (랜선 뺐다 꽂으면 바로 보이게)
                System.Windows.Forms.Timer rt = new System.Windows.Forms.Timer { Interval = 1000 };
                rt.Tick += delegate { lm.Invalidate(); lp.Invalidate(); };
                rt.Start();
                f.FormClosed += delegate { rt.Stop(); rt.Dispose(); };

                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    AdapterInfo m = lm.SelectedItem as AdapterInfo, p = lp.SelectedItem as AdapterInfo;
                    if (m != null && p != null && m.Name == p.Name) { Log("유선 모뎀과 테더링은 서로 다른 네트워크로 골라주세요"); return; }
                    if (m != null) { modemName = m.Name; SaveSetting("modem", modemName); }
                    if (p != null) { phoneName = p.Name; SaveSetting("phone", phoneName); }
                    Log("설정 저장 · 유선 모뎀: " + (modemName ?? "-") + " / 테더링: " + (phoneName ?? "-"));
                }
            }
        }

        // ---------- 1초마다 상태 확인 ----------
        int polling;
        void Poll()
        {
            if (Interlocked.Exchange(ref polling, 1) == 1) return;
            try
            {
                tick++;
                if (adb != null) CheckPhone();
                if (tick % 2 == 1) UpdateStatusLabels();
            }
            catch { }
            finally { Interlocked.Exchange(ref polling, 0); }
        }

        void UpdateStatusLabels()
        {
            // 유선 모뎀 상태
            if (modemName == null) SetLbl(lblModemSt, "모뎀 선택 안 됨", Color.Gray);
            else
            {
                AdapterInfo m = GetAdapter(modemName);
                if (m == null) SetLbl(lblModemSt, "모뎀 찾을 수 없음", RED);
                else if (!m.Enabled) SetLbl(lblModemSt, "사용 안 함", Color.Gray);
                else if (AdapterReady(modemName)) SetLbl(lblModemSt, "연결됨", GREEN);
                else SetLbl(lblModemSt, "신호 없음", ORANGE);
            }

            // 테더링 상태 (폰에 직접 물어봄 → PC에서 사용 안 함이어도 확인됨)
            string st; Color c;
            if (adb == null || phoneState == "none" || phoneState == "") { st = "폰 연결 안 됨"; c = Color.Gray; }
            else if (phoneState == "unauthorized") { st = "USB 디버깅 허용 필요"; c = ORANGE; }
            else if (phoneState != "device") { st = "폰 상태: " + phoneState; c = ORANGE; }
            else if (!TetherOn()) { st = "테더링 꺼짐!"; c = RED; }
            else
            {
                AdapterInfo p = GetAdapter(phoneName);
                if (p != null && !p.Enabled) { st = "테더링 켜짐 · PC 사용 안 함"; c = GREEN; }
                else if (phoneName != null && AdapterReady(phoneName)) { st = "테더링 켜짐 · 연결됨"; c = GREEN; }
                else { st = "테더링 켜짐"; c = GREEN; }
            }
            SetLbl(lblTetherSt, st, c);
        }

        bool lastTetherOn = true;
        bool TetherOn()
        {
            string cfg = (Run("shell getprop sys.usb.config", 4000) + Run("shell getprop sys.usb.state", 4000)).ToLower();
            bool on = cfg.Contains("rndis") || cfg.Contains("ncm");
            if (!on)
            {
                string ifs = Run("shell ip -o link", 4000).ToLower();
                on = (ifs.Contains("rndis0") || ifs.Contains("ncm0")) && ifs.Contains("state up");
            }
            if (on != lastTetherOn) { Log(on ? "폰 테더링 켜짐 확인" : "폰 테더링이 꺼져 있어요 → 폰에서 USB 테더링을 켜주세요"); lastTetherOn = on; }
            return on;
        }

        void CheckPhone()
        {
            string outp = Run("devices", 5000);
            string state = "none";
            foreach (string raw in outp.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("List of") || l.StartsWith("*")) continue;
                string[] parts = l.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (parts[1] == "device") { state = "device"; break; }
                if (parts[1] == "unauthorized") state = "unauthorized";
                else if (state == "none") state = parts[1];
            }
            if (state == phoneState) return;
            phoneState = state;

            if (state == "device")
            {
                string model = Run("shell getprop ro.product.model", 5000).Trim();
                string ver = Run("shell getprop ro.build.version.release", 5000).Trim();
                SetLbl(lblPhone, "폰 연결됨 · " + model + " (안드로이드 " + ver + ")", GREEN);
                Log("폰 연결됨: " + model + " / 안드로이드 " + ver);
                int v; if (int.TryParse(ver.Split('.')[0], out v) && v < 11)
                    Log("주의: 안드로이드 11 미만은 비행기모드 전환이 안 될 수 있어요");
                if (phoneName == null) DetectAdapters(false);
            }
            else if (state == "unauthorized")
            {
                SetLbl(lblPhone, "폰 화면에서 'USB 디버깅 허용'을 눌러주세요", ORANGE);
                Log("폰 감지됨 → 폰 화면에 뜬 'USB 디버깅 허용'에서 '항상 허용' 체크 후 허용");
            }
            else if (state == "none")
            {
                SetLbl(lblPhone, "폰 연결 안 됨 · USB 연결 + USB 디버깅 켜기", Color.Gray);
                Log("폰 연결 안 됨 (USB 케이블과 개발자 옵션의 USB 디버깅 확인)");
            }
            else
            {
                SetLbl(lblPhone, "폰 상태: " + state, ORANGE);
                Log("폰 상태: " + state);
            }
        }

        // ---------- 응답속도(핑) : 테더링 사용중에만, 3초마다, 최소 크기 ----------
        class PingRec { public DateTime t; public long ms; public bool ok; }
        readonly List<PingRec> pings = new List<PingRec>();
        int pingBusy;
        static readonly byte[] tinyBuf = new byte[0];

        void ResetPing()
        {
            lock (pings) pings.Clear();
            lastPingMs = -1;
            SetLbl(lblPing, "응답속도 측정 중...", Color.Gray);
        }

        void PingOnce()
        {
            if (busy) return;   // 유선·테더링 모두 측정 (작업표시줄 상자에 표시)
            if (Interlocked.Exchange(ref pingBusy, 1) == 1) return;
            try
            {
                PingRec r = new PingRec { t = DateTime.Now };
                try
                {
                    using (Ping p = new Ping())
                    {
                        PingReply rep;
                        try { rep = p.Send("1.1.1.1", 2000, tinyBuf); }
                        catch { rep = p.Send("1.1.1.1", 2000, new byte[1]); }
                        r.ok = rep.Status == IPStatus.Success;
                        r.ms = rep.RoundtripTime;
                    }
                }
                catch { r.ok = false; }

                int n, fail; long sum = 0; int okc = 0;
                lock (pings)
                {
                    pings.Add(r);
                    pings.RemoveAll(x => (DateTime.Now - x.t).TotalSeconds > 60);   // 최근 1분만
                    n = pings.Count; fail = 0;
                    foreach (PingRec x in pings) { if (x.ok) { sum += x.ms; okc++; } else fail++; }
                }
                long avg = okc > 0 ? sum / okc : -1;
                int loss = n > 0 ? fail * 100 / n : 0;

                string grade; Color c;
                if (okc == 0) { grade = "연결 안 됨"; c = RED; }
                else if (loss >= 10 || avg >= 200) { grade = "느림/불안정"; c = RED; }
                else if (loss > 0 || avg > 60) { grade = "보통"; c = ORANGE; }
                else { grade = "좋음"; c = GREEN; }

                lastPingMs = r.ok ? (int)r.ms : -2;
                string now = r.ok ? r.ms + "ms" : "응답 없음";
                string txt = "응답속도 " + now + "  ·  최근 1분 평균 " + (avg >= 0 ? avg + "ms" : "-") + (loss > 0 ? " · 끊김 " + loss + "%" : "") + "  ·  " + grade;
                SetLbl(lblPing, txt, c);
            }
            finally { Interlocked.Exchange(ref pingBusy, 0); }
        }

        // ---------- 아이피 변경 (테더링일 때만) ----------
        void StartChange()
        {
            if (busy) return;
            if (!tetherMode) return;   // 유선 사용중엔 눌리지 않음
            if (phoneState != "device")
            {
                Log(phoneState == "unauthorized" ? "폰에서 'USB 디버깅 허용'을 먼저 눌러주세요" : "폰이 연결되지 않았어요");
                return;
            }
            busy = true;
            sw.Enabled = false;
            btn.SetState(BtnState.Busy, "변경중");
            lblTitle.Text = "아이피 변경중...";
            lblNewIp.Text = "-";
            if (lastNewIp != null) lblCurIp.Text = lastNewIp;
            new Thread(DoChange) { IsBackground = true }.Start();
        }

        void DoChange()
        {
            string newIp = null;
            try
            {
                Log("──────── 아이피 변경 시작 ────────");
                string oldIp = GetPublicIp(4);
                Ui(() => { lblCurIp.Text = oldIp ?? "확인 실패"; });
                Log("현재 아이피: " + (oldIp ?? "확인 실패"));

                Log("비행기모드 켜는 중...");
                SetAirplane(true);
                WaitAirplane(true);
                Thread.Sleep(2500);

                Log("비행기모드 끄는 중...");
                SetAirplane(false);
                WaitAirplane(false);

                Log("모바일 데이터 연결 기다리는 중...");
                WaitData();
                if (!TetherOn()) Log("주의: 테더링이 꺼졌어요 → 폰에서 USB 테더링을 다시 켜주세요");

                Log("새 아이피 확인 중...");
                DateTime until = DateTime.Now.AddSeconds(40);
                while (DateTime.Now < until)
                {
                    string ip = GetPublicIp(1);
                    if (ip != null)
                    {
                        newIp = ip;
                        if (ip != oldIp || DateTime.Now > until.AddSeconds(-15)) break;
                    }
                    Thread.Sleep(1500);
                }

                if (newIp == null) Log("새 아이피 확인 실패 (테더링 연결을 확인해 주세요)");
                else if (newIp == oldIp) Log("아이피가 그대로예요: " + newIp + " (한 번 더 눌러보세요)");
                else Log("변경 완료: " + oldIp + " → " + newIp);
            }
            catch (Exception ex) { Log("오류: " + ex.Message); }

            ResetPing();
            string fin = newIp;
            Ui(() =>
            {
                busy = false;
                sw.Enabled = true;
                lblTitle.Text = "아이피 변경";
                lblNewIp.Text = fin ?? "확인 실패";
                if (fin != null) lastNewIp = fin;
                ApplyModeUi();
            });
        }

        void SetAirplane(bool on)
        {
            string r = Run("shell cmd connectivity airplane-mode " + (on ? "enable" : "disable"), 10000).Trim();
            if (r.Length > 0 && (r.ToLower().Contains("unknown") || r.ToLower().Contains("error") || r.Contains("Exception")))
            {
                Log("기본 방식 실패, 다른 방식 시도: " + FirstLine(r));
                Run("shell settings put global airplane_mode_on " + (on ? "1" : "0"), 8000);
                Run("shell am broadcast -a android.intent.action.AIRPLANE_MODE --ez state " + (on ? "true" : "false"), 8000);
            }
        }

        void WaitAirplane(bool on)
        {
            string want = on ? "1" : "0";
            for (int i = 0; i < 20; i++)
            {
                string v = Run("shell settings get global airplane_mode_on", 5000).Trim();
                if (v == want) { Log("비행기모드 " + (on ? "켜짐" : "꺼짐") + " 확인"); return; }
                Thread.Sleep(500);
            }
            Log("비행기모드 상태 확인 안 됨 (계속 진행)");
        }

        void WaitData()
        {
            string last = "";
            for (int i = 0; i < 40; i++)
            {
                string r = Run("shell \"dumpsys telephony.registry | grep -m1 mDataConnectionState\"", 6000);
                string st = DataStateName(r);
                if (st != last) { Log("모바일 데이터: " + st); last = st; }
                if (st == "연결됨") return;
                Thread.Sleep(700);
            }
        }

        static string DataStateName(string r)
        {
            int i = r.IndexOf('=');
            string v = i >= 0 ? r.Substring(i + 1).Trim() : r.Trim();
            switch (v)
            {
                case "0": return "끊김";
                case "1": return "연결 중";
                case "2": return "연결됨";
                case "3": return "일시 중지";
                case "4": return "끊는 중";
                default: return v.Length == 0 ? "확인 불가" : v;
            }
        }

        static string FirstLine(string s) { int i = s.IndexOf('\n'); return (i > 0 ? s.Substring(0, i) : s).Trim(); }

        // ---------- 공인 아이피 ----------
        static string GetPublicIp(int tries)
        {
            string[] urls = { "https://api.ipify.org", "https://icanhazip.com", "https://ifconfig.me/ip", "http://checkip.amazonaws.com" };
            for (int t = 0; t < tries; t++)
            {
                foreach (string u in urls)
                {
                    try
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(u);
                        req.Timeout = 3000; req.ReadWriteTimeout = 3000; req.KeepAlive = false;
                        req.UserAgent = "curl/8.0";
                        using (WebResponse res = req.GetResponse())
                        using (StreamReader sr = new StreamReader(res.GetResponseStream()))
                        {
                            string ip = sr.ReadToEnd().Trim();
                            IPAddress a;
                            if (IPAddress.TryParse(ip, out a)) return ip;
                        }
                    }
                    catch { }
                }
                if (t < tries - 1) Thread.Sleep(1000);
            }
            return null;
        }

        // ---------- 설정 저장 ----------
        static string SettingsDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IpChanger"); }
        static string LoadSetting(string key)
        {
            try { string f = Path.Combine(SettingsDir(), key + ".txt"); if (File.Exists(f)) { string v = File.ReadAllText(f, Encoding.UTF8).Trim(); return v.Length > 0 ? v : null; } } catch { }
            return null;
        }
        static void SaveSetting(string key, string val)
        {
            try { Directory.CreateDirectory(SettingsDir()); File.WriteAllText(Path.Combine(SettingsDir(), key + ".txt"), val ?? "", Encoding.UTF8); } catch { }
        }

        // ---------- ADB ----------
        string EnsureAdb()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string local = Path.Combine(exeDir, "platform-tools", "adb.exe");
            if (File.Exists(local)) return local;

            string pt = Path.Combine(SettingsDir(), "platform-tools");
            string cached = Path.Combine(pt, "adb.exe");
            if (File.Exists(cached)) return cached;

            Log("ADB 준비 중 (처음 한 번만)...");
            Directory.CreateDirectory(pt);
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string name in new[] { "AdbWinApi.dll", "AdbWinUsbApi.dll", "adb.exe" })
            {
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) throw new Exception(name + " 없음");
                    string dest = Path.Combine(pt, name);
                    string tmp = dest + ".tmp";
                    using (FileStream fs = File.Create(tmp)) s.CopyTo(fs);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tmp, dest);
                }
            }
            Log("ADB 준비 완료");
            return cached;
        }

        string Run(string args, int timeoutMs)
        {
            if (adb == null) return "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(adb, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                using (Process p = Process.Start(psi))
                {
                    StringBuilder sb = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                    else p.WaitForExit();
                    lock (sb) return sb.ToString();
                }
            }
            catch (Exception ex) { return "error: " + ex.Message; }
        }
    }
}
