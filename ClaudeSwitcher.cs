// Claude Switcher — switch the Claude desktop app (Windows) between accounts.
// MIT License. Not affiliated with or endorsed by Anthropic.
//
// How it works: the desktop app keeps everything (sign-in, settings, sessions) in one
// data folder. Each saved account is that folder, parked under
// %LOCALAPPDATA%\ClaudeSwitcher\profiles\<name>. Switching = close Claude, rename the
// live folder into the store, rename the chosen account's folder into place, start
// Claude. Same-drive renames are instant; nothing is copied.
//
// Build: build.cmd   (uses the C# compiler that ships with Windows)
// Test:  "Claude Switcher.exe" --test --data <fake data dir> --store <fake store dir>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================ core logic
static class Program
{
    public static string DataDir, StoreDir, ActiveFile, SettingsFile, PackageId;
    public static bool TestMode;

    [STAThread]
    static void Main(string[] args)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string home = Path.Combine(local, "ClaudeSwitcher");
        StoreDir = Path.Combine(home, "profiles");
        PackageId = FindPackageId(local);
        DataDir = PackageId == null ? null : Path.Combine(local, @"Claude-Data\claude_" + PackageId);

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--test") TestMode = true;
            else if (args[i] == "--data" && i + 1 < args.Length) DataDir = args[++i];
            else if (args[i] == "--store" && i + 1 < args.Length) StoreDir = args[++i];
        }
        ActiveFile = Path.Combine(Path.GetDirectoryName(StoreDir), "active.txt");
        SettingsFile = Path.Combine(Path.GetDirectoryName(StoreDir), "theme.txt");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Theme.Load();

        if (DataDir == null)
        {
            Dialog.Alert("Claude not found", "Couldn't find the Claude desktop app on this PC. Install it from claude.ai/download, open it once, then try again.");
            return;
        }
        Directory.CreateDirectory(StoreDir);
        if (!TestMode && SelfInstall(home)) return;

        if (GetActive() == null)
        {
            string name = null;
            while (!ValidName(name))
            {
                name = Dialog.Ask("Welcome", "What should we call the account you're signed in to right now?", "Main");
                if (name == null) return;
            }
            SetActive(name);
        }
        Application.Run(new MainForm());
    }

    // The Store app's data lives in %LOCALAPPDATA%\Claude-Data\claude_<publisherId>.
    static string FindPackageId(string local)
    {
        string root = Path.Combine(local, "Claude-Data");
        if (!Directory.Exists(root)) return null;
        var d = Directory.GetDirectories(root, "claude_*").FirstOrDefault();
        return d == null ? null : Path.GetFileName(d).Substring("claude_".Length);
    }

    // The exe must not run from inside Claude's data folder (it gets moved when switching).
    static bool SelfInstall(string home)
    {
        string me = Application.ExecutablePath;
        if (!me.StartsWith(DataDir + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        Directory.CreateDirectory(home);
        string target = Path.Combine(home, "Claude Switcher.exe");
        File.Copy(me, target, true);
        try
        {
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Claude Switcher.lnk");
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            var bf = System.Reflection.BindingFlags.InvokeMethod;
            object sc = t.InvokeMember("CreateShortcut", bf, null, shell, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { home });
            st.InvokeMember("Save", bf, null, sc, null);
        }
        catch { }
        Process.Start(target);
        return true;
    }

    public static string GetActive()
    {
        if (!File.Exists(ActiveFile)) return null;
        string s = File.ReadAllText(ActiveFile).Trim();
        return s.Length == 0 ? null : s;
    }
    public static void SetActive(string name) { File.WriteAllText(ActiveFile, name); }

    public static List<string> StoredProfiles()
    {
        return Directory.GetDirectories(StoreDir).Select(d => Path.GetFileName(d))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static bool ValidName(string n)
    {
        if (string.IsNullOrWhiteSpace(n) || n.Length > 40) return false;
        if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return n != "." && n != "..";
    }
    public static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    public static bool NameTaken(string n)
    {
        return Same(GetActive(), n) || Directory.Exists(Path.Combine(StoreDir, n));
    }

    static List<Process> ClaudeProcesses()
    {
        var list = new List<Process>();
        int self = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcesses())
        {
            if (p.Id == self) continue;
            string path = null;
            try { path = p.MainModule.FileName; } catch { }
            if (path == null) continue;
            bool app = path.IndexOf(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase) >= 0
                       && path.IndexOf(PackageId, StringComparison.OrdinalIgnoreCase) >= 0;
            bool data = path.StartsWith(DataDir + "\\", StringComparison.OrdinalIgnoreCase);
            if (app || data) list.Add(p);
        }
        return list;
    }

    static void StopClaude()
    {
        if (TestMode) return;
        var procs = ClaudeProcesses();
        if (procs.Count == 0) return;
        foreach (var p in procs) { try { p.CloseMainWindow(); } catch { } }
        for (int i = 0; i < 10 && ClaudeProcesses().Count > 0; i++) Thread.Sleep(500);
        foreach (var p in ClaudeProcesses()) { try { p.Kill(); } catch { } }
        for (int i = 0; i < 20 && ClaudeProcesses().Count > 0; i++) Thread.Sleep(500);
        if (ClaudeProcesses().Count > 0) throw new Exception("Claude didn't close. Close it yourself and try again.");
    }

    public static void StartClaude()
    {
        if (TestMode) return;
        Process.Start("explorer.exe", "shell:AppsFolder\\Claude_" + PackageId + "!Claude");
    }

    static void MoveWithRetry(string from, string to)
    {
        for (int i = 0; ; i++)
        {
            try { Directory.Move(from, to); return; }
            catch
            {
                if (i >= 15) throw new Exception("Couldn't move Claude's data folder. Another program may be using it.");
                Thread.Sleep(1000);
            }
        }
    }

    public static void SwitchTo(string target)
    {
        string active = GetActive();
        if (Same(target, active)) { StartClaude(); return; }

        string park = Path.Combine(StoreDir, active);
        string incoming = Path.Combine(StoreDir, target);
        if (Directory.Exists(park))
            throw new Exception("A saved account called '" + active + "' already exists, so the current one can't be stored.");

        StopClaude();
        if (Directory.Exists(DataDir)) MoveWithRetry(DataDir, park);

        // New account: leave the data folder absent so Claude starts fresh on its sign-in screen.
        if (Directory.Exists(incoming))
        {
            try { MoveWithRetry(incoming, DataDir); }
            catch
            {
                if (Directory.Exists(park)) Directory.Move(park, DataDir);   // roll back
                throw;
            }
        }
        SetActive(target);
        StartClaude();
    }
}

// ============================================================ theme
static class Theme
{
    public static bool Dark;
    public static Color Bg, Surface, SurfaceHover, Border, Text, SubText, Accent, AccentText, Danger;
    static readonly Color[] AvatarLight = { C(0xE8D5C4), C(0xCFE3D6), C(0xD4DDF0), C(0xEAD3E3), C(0xF0E2B8), C(0xD0E6EA) };
    static readonly Color[] AvatarDark  = { C(0x5A4334), C(0x34503F), C(0x39455E), C(0x553D50), C(0x5A4E2A), C(0x325258) };

    static Color C(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

    public static void Load()
    {
        string pref = null;
        try { if (File.Exists(Program.SettingsFile)) pref = File.ReadAllText(Program.SettingsFile).Trim(); } catch { }
        if (pref == "dark") Set(true);
        else if (pref == "light") Set(false);
        else Set(SystemIsDark());
    }
    public static void Toggle()
    {
        Set(!Dark);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Program.SettingsFile));
            File.WriteAllText(Program.SettingsFile, Dark ? "dark" : "light");
        }
        catch { }
    }
    static bool SystemIsDark()
    {
        try
        {
            object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return v is int && (int)v == 0;
        }
        catch { return false; }
    }
    static void Set(bool dark)
    {
        Dark = dark;
        if (dark)
        {
            Bg = C(0x1C1B19); Surface = C(0x262523); SurfaceHover = C(0x302E2B); Border = C(0x3A3835);
            Text = C(0xEDEAE4); SubText = C(0x9A968E); Accent = C(0xD9825F); AccentText = C(0x1C1B19); Danger = C(0xE5776B);
        }
        else
        {
            Bg = C(0xFAF9F6); Surface = C(0xFFFFFF); SurfaceHover = C(0xF3F1EC); Border = C(0xE6E2DA);
            Text = C(0x1F1E1C); SubText = C(0x77736B); Accent = C(0xC96A45); AccentText = Color.White; Danger = C(0xC2463A);
        }
    }
    public static Color AvatarFor(string name)
    {
        int h = 0; foreach (char ch in name.ToLowerInvariant()) h = h * 31 + ch;
        var pal = Dark ? AvatarDark : AvatarLight;
        return pal[Math.Abs(h % pal.Length)];
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    public static void ApplyTitleBar(Form f)
    {
        int v = Dark ? 1 : 0;
        try { DwmSetWindowAttribute(f.Handle, 20, ref v, 4); } catch { }
        try { int c = ColorTranslator.ToWin32(Bg); DwmSetWindowAttribute(f.Handle, 35, ref c, 4); } catch { }  // Win11 caption color
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    public static Font Ui(float size, bool bold) { return new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size); }
}

// ============================================================ controls
// A rounded, hover-aware surface that every clickable thing in the app is built from.
class Pill : Control
{
    public enum Kind { Card, Primary, Ghost, Dashed, Icon }
    public Kind Style = Kind.Ghost;
    public string Sub;          // second line (cards)
    public bool Current;        // highlighted card
    public bool ShowMore;       // "⋯" on hover (cards)
    public event EventHandler MoreClick;
    bool hover, overMore;

    public Pill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
        Font = Theme.Ui(10f, false);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; overMore = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool m = ShowMore && e.X > Width - 44;
        if (m != overMore) { overMore = m; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnClick(EventArgs e)
    {
        if (ShowMore && overMore) { if (MoreClick != null) MoreClick(this, e); return; }
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float rad = Style == Kind.Card || Style == Kind.Dashed ? 12 : Height / 2f;
        if (Style == Kind.Icon) rad = 10;

        Color fill, border, fg;
        switch (Style)
        {
            case Kind.Primary: fill = hover ? ControlPaint.Light(Theme.Accent, 0.15f) : Theme.Accent; border = fill; fg = Theme.AccentText; break;
            case Kind.Card:
                fill = hover && !Current ? Theme.SurfaceHover : Theme.Surface;
                border = Current ? Theme.Accent : Theme.Border; fg = Theme.Text; break;
            case Kind.Dashed: fill = hover ? Theme.SurfaceHover : Theme.Bg; border = Theme.Border; fg = hover ? Theme.Accent : Theme.SubText; break;
            default: fill = hover ? Theme.SurfaceHover : Theme.Bg; border = Theme.Border; fg = Theme.Text; break;
        }
        if (Style == Kind.Icon) border = hover ? Theme.Border : Theme.Bg;

        using (var path = Theme.Round(r, rad))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var p = new Pen(border, Current ? 1.6f : 1f))
            {
                if (Style == Kind.Dashed) p.DashPattern = new float[] { 4, 3 };
                g.DrawPath(p, path);
            }
        }

        if (Style != Kind.Card)
        {
            var font = Style == Kind.Icon ? new Font("Segoe MDL2 Assets", 11f) : Font;
            TextRenderer.DrawText(g, Text, font, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        // Card: avatar circle with initial, name, subtitle, then "Current" badge or "⋯".
        int av = 34, ax = 14, ay = (Height - av) / 2;
        using (var b = new SolidBrush(Current ? Theme.Accent : Theme.AvatarFor(Text))) g.FillEllipse(b, ax, ay, av, av);
        string initial = Text.Length > 0 ? Text.Substring(0, 1).ToUpperInvariant() : "?";
        TextRenderer.DrawText(g, initial, Theme.Ui(11f, true), new Rectangle(ax, ay, av, av),
            Current ? Theme.AccentText : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        int tx = ax + av + 12, right = Width - (ShowMore || Current ? 90 : 16);
        var nameFont = Theme.Ui(10.5f, true);
        if (string.IsNullOrEmpty(Sub))
            TextRenderer.DrawText(g, Text, nameFont, new Rectangle(tx, 0, right - tx, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        else
        {
            TextRenderer.DrawText(g, Text, nameFont, new Rectangle(tx, Height / 2 - 20, right - tx, 22), Theme.Text,
                TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Sub, Theme.Ui(8.5f, false), new Rectangle(tx, Height / 2 + 1, right - tx, 18), Theme.SubText,
                TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        if (ShowMore && hover)
        {
            var mr = new Rectangle(Width - 40, (Height - 28) / 2, 28, 28);
            if (overMore) using (var b = new SolidBrush(Theme.Border)) g.FillEllipse(b, mr);
            TextRenderer.DrawText(g, "", new Font("Segoe MDL2 Assets", 10f), mr, Theme.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else if (Current)
        {
            string badge = "Current";
            var bf = Theme.Ui(8.5f, true);
            var sz = TextRenderer.MeasureText(badge, bf);
            var br = new RectangleF(Width - sz.Width - 30, (Height - 22) / 2f, sz.Width + 16, 22);
            using (var path = Theme.Round(br, 11))
            using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 50 : 30, Theme.Accent))) g.FillPath(b, path);
            TextRenderer.DrawText(g, badge, bf, Rectangle.Round(br), Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

// ============================================================ main window
class MainForm : Form
{
    readonly Label title = new Label();
    readonly Pill themeBtn = new Pill();
    readonly Panel list = new Panel();
    readonly Pill addBtn = new Pill();
    readonly Label status = new Label();
    const int W = 360, Pad = 20, CardH = 60, Gap = 8;

    public MainForm()
    {
        Text = Program.TestMode ? "Claude Switcher (test mode)" : "Claude Switcher";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Font = Theme.Ui(10f, false);
        DoubleBuffered = true;

        title.Text = "Accounts";
        title.Font = Theme.Ui(15f, true);
        title.AutoSize = true;
        title.Location = new Point(Pad - 3, 18);
        Controls.Add(title);

        themeBtn.Style = Pill.Kind.Icon;
        themeBtn.Size = new Size(36, 36);
        themeBtn.Location = new Point(W - Pad - 36, 16);
        themeBtn.Click += delegate { Theme.Toggle(); ApplyTheme(); };
        Controls.Add(themeBtn);

        list.Location = new Point(Pad, 66);
        list.AutoScroll = true;
        Controls.Add(list);

        addBtn.Style = Pill.Kind.Dashed;
        addBtn.Text = "+   Add account";
        addBtn.Font = Theme.Ui(10f, true);
        addBtn.Click += delegate { AddAccount(); };
        Controls.Add(addBtn);

        status.AutoSize = false;
        status.Font = Theme.Ui(8.5f, false);
        status.TextAlign = ContentAlignment.MiddleCenter;
        Controls.Add(status);

        Rebuild();
        ApplyTheme();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.ApplyTitleBar(this); }

    void ApplyTheme()
    {
        BackColor = list.BackColor = Theme.Bg;
        title.ForeColor = Theme.Text;
        status.ForeColor = Theme.SubText;
        themeBtn.Text = Theme.Dark ? "" : "";   // sun / moon
        if (IsHandleCreated) Theme.ApplyTitleBar(this);
        foreach (Control c in Controls) c.Invalidate();
        foreach (Control c in list.Controls) c.Invalidate();
        Invalidate(true);
    }

    void Rebuild()
    {
        string active = Program.GetActive();
        var names = new List<string> { active };
        names.AddRange(Program.StoredProfiles().Where(n => !Program.Same(n, active)));

        list.SuspendLayout();
        list.Controls.Clear();
        int y = 0, cw = W - Pad * 2;
        bool scroll = names.Count > 5;
        if (scroll) cw -= SystemInformation.VerticalScrollBarWidth + 2;
        foreach (var n in names)
        {
            string name = n;
            bool cur = Program.Same(name, active);
            var card = new Pill();
            card.Style = Pill.Kind.Card;
            card.Text = name;
            card.Current = cur;
            card.Sub = cur ? "Signed in" : "Click to switch";
            card.ShowMore = true;
            card.SetBounds(0, y, cw, CardH);
            card.Click += delegate { SwitchTo(name); };
            card.MoreClick += delegate { More(name); };
            list.Controls.Add(card);
            y += CardH + Gap;
        }
        list.ResumeLayout();

        int listH = Math.Min(names.Count, 5) * (CardH + Gap) - Gap;
        list.Size = new Size(W - Pad * 2, listH);
        addBtn.SetBounds(Pad, list.Bottom + 12, W - Pad * 2, 46);
        status.SetBounds(Pad, addBtn.Bottom + 6, W - Pad * 2, 26);
        ClientSize = new Size(W, status.Bottom + 10);
    }

    void Busy(string msg, Action action)
    {
        status.Text = msg;
        UseWaitCursor = true; Enabled = false; Refresh();
        try { action(); }
        catch (Exception ex) { status.Text = ""; Dialog.Alert("Something went wrong", ex.Message); }
        finally { Enabled = true; UseWaitCursor = false; Rebuild(); ApplyTheme(); }
    }

    void SwitchTo(string name)
    {
        if (Program.Same(name, Program.GetActive())) { Busy("Opening Claude…", Program.StartClaude); return; }
        if (!Dialog.Confirm("Switch to " + name + "?", "Claude will close and reopen signed in as " + name + ". Anything running in Claude will stop.", "Switch", false)) return;
        Busy("Switching to " + name + "…", delegate { Program.SwitchTo(name); status.Text = "Switched to " + name; });
    }

    void AddAccount()
    {
        string n = Dialog.Ask("Add account", "Give it a name — like Work or Personal. Claude will reopen on its sign-in screen so you can log in.", "");
        if (n == null) return;
        if (!Program.ValidName(n) || Program.NameTaken(n)) { Dialog.Alert("Try another name", "That name is empty, too long, has symbols Windows doesn't allow, or is already used."); return; }
        Busy("Opening a fresh Claude for " + n + "…", delegate { Program.SwitchTo(n); status.Text = "Sign in to " + n + " in Claude"; });
    }

    void More(string name)
    {
        bool cur = Program.Same(name, Program.GetActive());
        var opts = cur ? new[] { "Rename", "Cancel" } : new[] { "Rename", "Delete", "Cancel" };
        int pick = Dialog.Choose(name, cur ? "This is the account you're signed in to." : "Saved account.", opts);
        if (pick == 0) Rename(name);
        else if (!cur && pick == 1) Delete(name);
    }

    void Rename(string old)
    {
        string n = Dialog.Ask("Rename", "New name for " + old + ":", old);
        if (n == null || n == old) return;
        if (!Program.ValidName(n) || (Program.NameTaken(n) && !Program.Same(n, old)))
        { Dialog.Alert("Try another name", "That name is empty, too long, has symbols Windows doesn't allow, or is already used."); return; }
        Busy("", delegate
        {
            if (Program.Same(old, Program.GetActive())) Program.SetActive(n);
            else Directory.Move(Path.Combine(Program.StoreDir, old), Path.Combine(Program.StoreDir, n));
        });
    }

    void Delete(string name)
    {
        if (!Dialog.Confirm("Delete " + name + "?", "Its sign-in, settings and local chats are removed from this PC. This can't be undone.", "Delete", true)) return;
        Busy("", delegate { Directory.Delete(Path.Combine(Program.StoreDir, name), true); });
    }
}

// ============================================================ themed dialogs
static class Dialog
{
    // Shows a small themed dialog. Returns the index of the clicked button (-1 if closed);
    // when withInput is true, the typed text comes back through `text`.
    static int Show(string heading, string message, string[] buttons, int primary, bool danger, bool withInput, ref string text)
    {
        const int W = 360, P = 22;
        using (var f = new Form())
        {
            f.Text = "Claude Switcher";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.MaximizeBox = f.MinimizeBox = false; f.ShowInTaskbar = Form.ActiveForm == null;
            f.BackColor = Theme.Bg; f.Font = Theme.Ui(10f, false);
            f.HandleCreated += delegate { Theme.ApplyTitleBar(f); };

            var h = new Label { Text = heading, Font = Theme.Ui(13f, true), ForeColor = Theme.Text, AutoSize = false };
            h.SetBounds(P - 2, 18, W - P * 2, 30);
            var mh = TextRenderer.MeasureText(message, f.Font, new Size(W - P * 2, 1000), TextFormatFlags.WordBreak).Height;
            var m = new Label { Text = message, ForeColor = Theme.SubText, AutoSize = false };
            m.SetBounds(P - 2, 52, W - P * 2, mh + 4);
            f.Controls.Add(h); f.Controls.Add(m);
            int y = m.Bottom + 14;

            TextBox tb = null;
            if (withInput)
            {
                var box = new Panel { BackColor = Theme.Surface };
                box.SetBounds(P, y, W - P * 2, 40);
                box.Paint += (s, e) => { using (var pen = new Pen(Theme.Accent, 1.5f)) e.Graphics.DrawRectangle(pen, 0, 0, box.Width - 1, box.Height - 1); };
                tb = new TextBox { Text = text ?? "", BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Ui(11f, false) };
                tb.SetBounds(12, 10, box.Width - 24, 24);
                box.Controls.Add(tb);
                f.Controls.Add(box);
                y = box.Bottom + 18;
            }

            int result = -1, bw = 100, x = W - P;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                int idx = i;
                var b = new Pill { Text = buttons[i] };
                b.Style = i == primary ? Pill.Kind.Primary : Pill.Kind.Ghost;
                b.Font = Theme.Ui(9.5f, i == primary);
                x -= bw; b.SetBounds(x, y, bw, 36); x -= 8;
                b.Click += delegate { result = idx; f.Close(); };
                f.Controls.Add(b);
            }
            f.ClientSize = new Size(W, y + 36 + P);
            f.KeyPreview = true;
            f.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && primary >= 0) { result = primary; e.SuppressKeyPress = true; f.Close(); }
                if (e.KeyCode == Keys.Escape) { result = -1; f.Close(); }
            };
            if (tb != null) f.Shown += delegate { tb.Focus(); tb.SelectAll(); };

            Color savedAccent = Theme.Accent;
            if (danger) Theme.Accent = Theme.Danger;
            try { f.ShowDialog(); }
            finally { Theme.Accent = savedAccent; }
            if (tb != null) text = tb.Text.Trim();
            return result;
        }
    }

    public static void Alert(string heading, string message)
    {
        string t = null; Show(heading, message, new[] { "OK" }, 0, false, false, ref t);
    }
    public static bool Confirm(string heading, string message, string yes, bool danger)
    {
        string t = null; return Show(heading, message, new[] { "Cancel", yes }, 1, danger, false, ref t) == 1;
    }
    public static string Ask(string heading, string message, string def)
    {
        string t = def; int r = Show(heading, message, new[] { "Cancel", "OK" }, 1, false, true, ref t);
        return r == 1 ? t : null;
    }
    public static int Choose(string heading, string message, string[] options)
    {
        string t = null; int r = Show(heading, message, options, -1, false, false, ref t);
        return r == options.Length - 1 ? -1 : r;   // last option is Cancel
    }
}
