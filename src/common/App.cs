using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Mem
{
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

    public static void Trim()
    {
        GC.Collect();
        try { EmptyWorkingSet(GetCurrentProcess()); } catch { }
    }

    // Açılıştan / pencere kapanışından kısa süre sonra çalışma kümesini küçült.
    public static void TrimLater(int ms)
    {
        System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        t.Interval = ms;
        t.Tick += delegate { t.Stop(); t.Dispose(); Trim(); };
        t.Start();
    }
}

// Tek tepsi simgesi + tüm widget'lar + tek ayar penceresi.
class HubApp : ApplicationContext
{
    const int Margin = 24, Gap = 12;   // 96 dpi px

    readonly List<WidgetDef> defs;
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem miPinAll, miTogether;
    readonly Control sync = new Control();
    SettingsForm win;
    int iconAccent = -1;

    // Tepsi menüsü bilerek kısa: Ayarlar · Dikeyde hizala · Birlikte taşı · Tümünü sabitle · Çıkış.
    // (Widget'ların üstünde sağ tık hiçbir şey yapmaz; göster/gizle ve widget'a özel ayarlar ayar penceresindedir.)
    public HubApp(List<WidgetDef> defs)
    {
        this.defs = defs;
        sync.CreateControl();
        IntPtr h = sync.Handle;   // ikinci örnek sinyali için UI iş parçacığına dönüş noktası

        ContextMenuStrip menu = Ui.NewMenu(true);
        ToolStripMenuItem miSettings = Ui.MenuItem("Ayarlar");
        miSettings.Click += delegate { ShowSettings(0); };
        menu.Items.Add(miSettings);

        ToolStripMenuItem miAlign = Ui.MenuItem("Dikeyde hizala");
        miAlign.Click += delegate { AlignVertical(); };
        miTogether = Ui.MenuItem("Birlikte taşı");
        miTogether.Click += delegate { AppSettings.I.MoveTogether = !AppSettings.I.MoveTogether; Settings.RaiseAll(); Settings.SaveAll(); };
        menu.Items.Add(miAlign);
        menu.Items.Add(miTogether);

        miPinAll = Ui.MenuItem("Tümünü sabitle");
        miPinAll.Click += delegate
        {
            bool target = !AllPinned();
            foreach (WidgetDef d in this.defs) d.S.Pinned = target;
            Settings.RaiseAll(); Settings.SaveAll();
        };
        ToolStripMenuItem miExit = Ui.MenuItem("Çıkış");
        miExit.Click += delegate { Application.Exit(); };
        menu.Items.Add(miPinAll);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miExit);
        menu.Opening += delegate { SyncMenu(); };

        tray = new NotifyIcon();
        tray.Text = "WinWinget";
        tray.ContextMenuStrip = menu;
        SetTrayIcon();
        tray.Visible = true;
        tray.MouseClick += delegate(object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(0); };

        foreach (WidgetDef d in defs)
            d.Form.Notice += delegate(string title, string text) { tray.ShowBalloonTip(4000, title, text, ToolTipIcon.None); };
        Settings.AnyChanged += OnChanged;
        Application.ApplicationExit += delegate { tray.Visible = false; tray.Dispose(); };
    }

    bool AllPinned()
    {
        foreach (WidgetDef d in defs) if (d.S.Enabled && !d.S.Pinned) return false;
        return true;
    }

    void SyncMenu()
    {
        miPinAll.Checked = AllPinned();
        miTogether.Checked = AppSettings.I.MoveTogether;
    }

    void SetTrayIcon()
    {
        if (iconAccent == AppSettings.I.Accent) return;
        iconAccent = AppSettings.I.Accent;
        Icon old = tray.Icon;
        tray.Icon = Ui.MakeIcon(AppSettings.I.UiAccent, IconKind.App);
        if (old != null) old.Dispose();
    }

    void OnChanged(object sender, EventArgs e)
    {
        SetTrayIcon();
        ArrangeIfNeeded();
    }

    // ---- yerleşim: aynı genişlikte kartlar tek sütunda, eşit aralıkla ----
    bool NeedsArrange()
    {
        foreach (WidgetDef d in defs) if (d.S.Enabled && !d.S.HasPos) return true;
        return false;
    }

    void ArrangeIfNeeded()
    {
        if (!NeedsArrange()) return;
        Arrange();
        foreach (WidgetDef d in defs) if (d.S.Enabled && !d.Form.Visible) d.Form.Show();
        Settings.SaveAll();
    }

    // Konumu olmayan widget'ları sağ üstten başlayarak dizer; zaten yerleşik olanlarla çakışmaz.
    void Arrange()
    {
        AppSettings app = AppSettings.I;
        Rectangle wa = app.HasHome ? Screen.FromPoint(new Point(app.HomeX, app.HomeY)).WorkingArea : Screen.PrimaryScreen.WorkingArea;
        if (!app.HasHome) app.SetHome(wa.Left + 1, wa.Top + 1);
        int margin = Ui.S(Margin), gap = Ui.S(Gap);
        List<Rectangle> placed = new List<Rectangle>();
        foreach (WidgetDef d in defs)
            if (d.S.Enabled && d.S.HasPos) placed.Add(new Rectangle(d.S.X, d.S.Y, d.Form.CardSize.Width, d.Form.CardSize.Height));

        int colRight = wa.Right - margin;   // sütunun sağ kenarı; hepsi aynı genişlikte → sol kenarlar da hizalı
        int y = wa.Top + margin;
        foreach (WidgetDef d in defs)
        {
            if (!d.S.Enabled || d.S.HasPos) continue;
            Size sz = d.Form.CardSize;
            int x = colRight - sz.Width;
            for (int guard = 0; guard < 40; guard++)
            {
                Rectangle cand = new Rectangle(x, y, sz.Width, sz.Height);
                bool moved = false;
                foreach (Rectangle r in placed)
                {
                    if (!cand.IntersectsWith(r)) continue;
                    y = r.Bottom + gap; moved = true; break;
                }
                if (!moved && y + sz.Height > wa.Bottom - margin && y > wa.Top + margin)
                {
                    colRight -= sz.Width + gap; x = colRight - sz.Width; y = wa.Top + margin; moved = true;   // sütun doldu → sola yeni sütun
                }
                if (!moved) break;
            }
            d.Form.MoveTo(new Point(x, y));
            placed.Add(new Rectangle(x, y, sz.Width, sz.Height));
            y += sz.Height + gap;
        }
    }

    // Görünür tüm widget'ları mevcut yerlerinde tek sütuna toplar: en üsttekinin sol kenarı ve üst noktası esas alınır,
    // mevcut dikey sıra korunur, aralar eşit olur (hepsi aynı genişlikte olduğu için kenarlar hizalanır).
    public void AlignVertical()
    {
        List<WidgetDef> list = new List<WidgetDef>();
        foreach (WidgetDef d in defs) if (d.S.Enabled && d.Form.Visible) list.Add(d);
        if (list.Count == 0) return;
        list.Sort(delegate(WidgetDef a, WidgetDef b)
        {
            int c = a.Form.Top.CompareTo(b.Form.Top);
            return c != 0 ? c : defs.IndexOf(a).CompareTo(defs.IndexOf(b));
        });

        Point a0 = list[0].Form.Location;
        Rectangle wa = Screen.FromPoint(new Point(a0.X + 1, a0.Y + 1)).WorkingArea;
        int margin = Ui.S(Margin), gap = Ui.S(Gap), total = gap * (list.Count - 1);
        foreach (WidgetDef d in list) total += d.Form.CardSize.Height;

        int x = Math.Max(wa.Left, Math.Min(a0.X, wa.Right - list[0].Form.CardSize.Width));
        int y = a0.Y;
        if (y + total > wa.Bottom - margin) y = Math.Max(wa.Top + margin, wa.Bottom - margin - total);
        AppSettings.I.SetHome(x + 1, y + 1);
        foreach (WidgetDef d in list)
        {
            d.Form.MoveTo(new Point(x, y));
            y += d.Form.CardSize.Height + gap;
        }
        Settings.SaveAll();
    }

    public void Start(bool openSettings, int tab, string snapshotDir)
    {
        foreach (WidgetDef d in defs) { d.Form.Prepare(); d.Form.ApplySavedPosition(); }
        Arrange();
        foreach (WidgetDef d in defs) if (d.S.Enabled) d.Form.Show();
        Settings.SaveAll();
        if (openSettings) ShowSettings(tab);
        if (snapshotDir != null)
        {
            // Geliştirme: kısa süre bekle, her kartı PNG'ye yaz, çık.
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 2500;
            t.Tick += delegate
            {
                t.Stop();
                Directory.CreateDirectory(snapshotDir);
                foreach (WidgetDef d in defs) d.Form.SaveSnapshot(Path.Combine(snapshotDir, d.Id + ".png"));
                Application.Exit();
            };
            t.Start();
        }
    }

    public void ShowSettings(int tab)
    {
        if (win != null && !win.IsDisposed)
        {
            if (win.WindowState == FormWindowState.Minimized) win.WindowState = FormWindowState.Normal;
            win.Activate();
            return;
        }
        win = new SettingsForm(defs, AlignVertical, tab);
        win.FormClosed += delegate { win = null; Mem.TrimLater(500); };
        win.Show();
        win.Activate();
    }

    // İkinci örnek başlatılınca ayarları aç (tepsi simgesi gizli kalsa bile ulaşılabilsin).
    public void Post(Action a)
    {
        try { sync.BeginInvoke(a); } catch { }
    }
}

static class WidgetApp
{
    public static void Run(string appId, string[] args, Func<HubApp> factory)
    {
        bool open = false; int tab = 0; string snap = null;
        foreach (string a in args)
        {
            if (a == "--settings") open = true;
            else if (a.StartsWith("--tab=")) int.TryParse(a.Substring(6), out tab);
            else if (a.StartsWith("--snapshot=")) snap = a.Substring(11);
        }

        bool created;
        using (Mutex m = new Mutex(true, "Local\\" + appId + ".SingleInstance", out created))
        {
            EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\" + appId + ".ShowSettings");
            if (!created) { ev.Set(); return; }

            Ui.Init();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            HubApp app = factory();
            app.Start(open, tab, snap);
            ThreadPool.RegisterWaitForSingleObject(ev,
                delegate(object st, bool timedOut) { app.Post(delegate { app.ShowSettings(0); }); },
                null, Timeout.Infinite, false);
            Application.Run(app);
        }
    }
}
