using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Kart renkleri: tema (koyu/açık) + accent'e göre bir kez hesaplanır (DESIGN.md §2).
class Card
{
    public bool Light;
    public int Alpha;
    public Color Surf, Txt, Mut, Acc, Ink, Good, Warn, Danger, Track, Line;

    public static Card From(Settings s)
    {
        Card c = new Card();
        c.Light = s.Light;
        c.Alpha = (int)Math.Round(255 * s.Opacity / 100.0);
        c.Acc = s.CardAccent;
        c.Surf = c.Light ? Color.White : Palette.Hex("14161C");
        c.Txt = Palette.Hex(c.Light ? "111827" : "F2F3F5");
        c.Mut = Palette.Hex(c.Light ? "6B7280" : "9BA1AC");
        c.Good = Palette.Hex(c.Light ? "059669" : "34D399");
        c.Warn = Palette.Hex(c.Light ? "D97706" : "FBBF24");
        c.Danger = Palette.Hex(c.Light ? "DC2626" : "F87171");
        c.Ink = c.Light ? Color.White : Color.FromArgb((int)(c.Acc.R * 0.09), (int)(c.Acc.G * 0.09), (int)(c.Acc.B * 0.09));
        c.Track = c.Light ? Color.FromArgb(26, 0, 0, 0) : Color.FromArgb(30, 255, 255, 255);
        c.Line = c.Light ? Color.FromArgb(20, 0, 0, 0) : Color.FromArgb(23, 255, 255, 255);
        return c;
    }

    // Doluluk yüzdesine göre durum rengi (accent → uyarı → tehlike).
    public Color Status(double pct) { return pct >= 90 ? Danger : (pct >= 70 ? Warn : Acc); }
}

// Widget'ların ortak tabanı: per-pixel alfa katmanlı, görev çubuğunda görünmeyen kart pencere.
// Alt sınıf yalnızca Layout / PaintContent / NextDelayMs yazar.
abstract class WidgetForm : Form
{
    const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20;
    const int GWL_EXSTYLE = -20, ULW_ALPHA = 2;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc,
        ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(IntPtr h, int i, int v);

    // Açık tüm widget'lar (grup taşıma için).
    static readonly List<WidgetForm> Group = new List<WidgetForm>();

    protected readonly Settings S;
    protected readonly Graphics Mg;
    protected readonly StringFormat Sf = new StringFormat(StringFormat.GenericTypographic);
    protected int BW, BH;      // kart boyutu (px)
    protected float Sc;        // DPI × kullanıcı ölçeği
    protected Card C;

    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Bitmap probe = new Bitmap(1, 1);
    readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
    float fontsSc = -1f;
    Bitmap buf;

    // Bildirim isteği (tepsi balonu olarak gösterilir).
    public event Action<string, string> Notice;
    protected void Notify(string title, string text) { if (Notice != null) Notice(title, text); }

    // Tüm kartların ortak genişliği (96 dpi px). BW her zaman bundan türetilir → simetrik görünüm.
    public const float CardWidth = 252f;

    // ---- alt sınıfın sözleşmesi ----
    protected abstract void MeasureCard();                       // yalnızca BH hesapla (BW ve Sc hazır)
    protected abstract void PaintContent(Graphics g);       // kartın içeriğini çiz (C hazır)
    protected abstract int NextDelayMs();                   // bir sonraki yeniden çizime kadar ms; <=0 → zamanlayıcı yok
    protected virtual void OnTick() { }                     // her zamanlayıcı atımında, çizimden önce
    protected virtual void OnSettingsApplied() { }
    protected virtual bool ClickThroughWhenPinned { get { return true; } }
    protected virtual bool KeepAliveHidden { get { return false; } }   // gizliyken de zamanlayıcı çalışsın mı (pomodoro)
    protected virtual bool OnCardMouseDown(Point p) { return false; }  // true = tıklama işlendi (sürükleme yok)
    protected virtual void OnCardMouseMove(Point p) { }
    protected virtual void OnCardMouseLeave() { }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            // Başlangıç durumu burada verilmeli: WinForms tutamaktan sonra CreateParams stilini yeniden uygular.
            // (S, temel sınıf kurucusu çalışırken henüz atanmamış olabilir.)
            if (S != null && S.Pinned && ClickThroughWhenPinned) cp.ExStyle |= WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected WidgetForm(Settings settings)
    {
        S = settings;
        Group.Add(this);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = S.TopMost;

        Mg = Graphics.FromImage(probe);
        Mg.TextRenderingHint = TextRenderingHint.AntiAlias;
        Sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;

        // Widget üstünde sağ tık bilerek hiçbir şey yapmaz (menü yok); sabitleme tepsi menüsü / ayarlardan.

        timer.Tick += delegate { OnTick(); Render(); ScheduleNext(); };
        Settings.AnyChanged += OnSettings;
    }

    // Alt sınıf kurulduktan sonra çağrılır: boyutu hesaplar.
    public void Prepare()
    {
        DoLayout();
        Size = new Size(BW, BH);
    }

    void DoLayout()
    {
        Sc = Ui.K * S.Scale / 100f;
        BW = (int)Math.Ceiling(CardWidth * Sc);
        MeasureCard();
    }

    public Size CardSize { get { return new Size(BW, BH); } }

    // ---- konum ----
    // Kayıtlı konum varsa uygular (çalışma alanına sığdırarak); yoksa false döner (hub dizer).
    public bool ApplySavedPosition()
    {
        if (!S.HasPos) return false;
        Location = ClampToScreen(new Point(S.X, S.Y));
        return true;
    }

    public void MoveTo(Point p)
    {
        Location = p;
        S.X = p.X; S.Y = p.Y; S.HasPos = true;
    }

    Point ClampToScreen(Point p)
    {
        Rectangle r = new Rectangle(p, new Size(BW, BH));
        foreach (Screen sc2 in Screen.AllScreens)
        {
            Rectangle wa = sc2.WorkingArea;
            if (!wa.IntersectsWith(r)) continue;
            int x = Math.Max(wa.Left, Math.Min(p.X, wa.Right - BW));
            int y = Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - BH));
            return new Point(x, y);
        }
        Rectangle pa = Screen.PrimaryScreen.WorkingArea;
        return new Point(pa.Right - BW - Ui.S(24), pa.Top + Ui.S(24));
    }

    // ---- ayarlar değişince ----
    void OnSettings(object sender, EventArgs e)
    {
        TopMost = S.TopMost;
        OnSettingsApplied();
        if (IsHandleCreated) ApplyClickThrough();
        // Konumu olmayan widget'ı hub yerleştirip gösterir.
        if (S.Enabled && !Visible && S.HasPos) Show();
        else if (!S.Enabled && Visible) Hide();
        Render();
        ScheduleNext();
    }

    void ApplyClickThrough()
    {
        int ex = GetWindowLong(Handle, GWL_EXSTYLE);
        bool ct = S.Pinned && ClickThroughWhenPinned;
        ex = ct ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
        SetWindowLong(Handle, GWL_EXSTYLE, ex);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyClickThrough();
    }

    // Gösterilince (ilk açılış ya da gizlemeden dönüş) çiz ve zamanlayıcıyı başlat.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) { ScheduleNext(); return; }
        Render();
        ScheduleNext();
        Mem.TrimLater(2000);
    }

    // ---- yazı / ölçüm yardımcıları ----
    protected Font Fnt(bool mono, float px, bool strong)
    {
        if (Math.Abs(fontsSc - Sc) > 0.001f)
        {
            foreach (Font f0 in fonts.Values) f0.Dispose();
            fonts.Clear();
            fontsSc = Sc;
        }
        string key = mono + "|" + px + "|" + strong;
        Font f;
        if (!fonts.TryGetValue(key, out f)) { f = Ui.NewFont(mono, px * Sc, strong); fonts[key] = f; }
        return f;
    }

    protected static float Cap(Font f) { return f.Size * 0.70f; }

    protected static float Asc(Font f)
    {
        return f.FontFamily.GetCellAscent(f.Style) * f.Size / f.FontFamily.GetEmHeight(f.Style);
    }

    protected float Wid(string t, Font f) { return Mg.MeasureString(t, f, PointF.Empty, Sf).Width; }
    protected SizeF Meas(string t, Font f) { return Mg.MeasureString(t, f, PointF.Empty, Sf); }

    // capTop: büyük harf üst çizgisinin y'si (satır kutusu değil, görsel üst).
    protected void Txt(Graphics g, string t, Font f, Color c, float x, float capTop)
    {
        using (Brush b = new SolidBrush(c)) g.DrawString(t, f, b, x, capTop - (Asc(f) - Cap(f)), Sf);
    }

    protected float TxtR(Graphics g, string t, Font f, Color c, float xRight, float capTop)
    {
        float w = Wid(t, f);
        Txt(g, t, f, c, xRight - w, capTop);
        return w;
    }

    protected float TxtC(Graphics g, string t, Font f, Color c, float cx, float capTop)
    {
        float w = Wid(t, f);
        Txt(g, t, f, c, cx - w / 2f, capTop);
        return w;
    }

    // ---- çizim ----
    protected void Redraw()
    {
        Render();
        ScheduleNext();
    }

    void Render()
    {
        if (!IsHandleCreated || !Visible) return;
        DoLayout();
        if (buf == null || buf.Width != BW || buf.Height != BH)
        {
            if (buf != null) buf.Dispose();
            buf = new Bitmap(BW, BH, PixelFormat.Format32bppArgb);
        }
        C = Card.From(S);
        using (Graphics g = Graphics.FromImage(buf))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            PaintCard(g);
            PaintContent(g);
        }
        Push(buf);
    }

    void PaintCard(Graphics g)
    {
        RectangleF r = new RectangleF(0.5f, 0.5f, BW - 1, BH - 1);
        using (GraphicsPath path = Ui.Round(r, 16 * Sc))
        {
            using (Brush b = new SolidBrush(Color.FromArgb(C.Alpha, C.Surf))) g.FillPath(b, path);

            // Hero kart: accent'ten şeffafa 135° gradient (DESIGN.md §9 Kart)
            Color a0 = Color.FromArgb((int)(C.Alpha * 0.16), C.Acc), a1 = Color.FromArgb(0, C.Acc);
            using (LinearGradientBrush gb = new LinearGradientBrush(r, a0, a1, 45f))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { a0, a1, a1 };
                cb.Positions = new float[] { 0f, 0.6f, 1f };
                gb.InterpolationColors = cb;
                g.FillPath(gb, path);
            }
            using (Pen p = new Pen(Color.FromArgb(82, C.Acc), Math.Max(1f, Ui.K))) g.DrawPath(p, path);
        }
    }

    void Push(Bitmap bmp)
    {
        IntPtr scr = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(scr);
        IntPtr hb = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hb = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(mem, hb);
            SIZE sz = new SIZE(); sz.cx = bmp.Width; sz.cy = bmp.Height;
            POINT src = new POINT();
            POINT dst = new POINT(); dst.x = Left; dst.y = Top;
            BLENDFUNCTION bf = new BLENDFUNCTION(); bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1;
            UpdateLayeredWindow(Handle, scr, ref dst, ref sz, mem, ref src, 0, ref bf, ULW_ALPHA);
        }
        finally
        {
            if (hb != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hb); }
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, scr);
        }
    }

    void ScheduleNext()
    {
        timer.Stop();
        if (!Visible && !KeepAliveHidden) return;   // gizli widget CPU harcamaz
        int ms = NextDelayMs();
        if (ms <= 0) return;
        timer.Interval = Math.Max(50, ms);
        timer.Start();
    }

    // Test/geliştirme: son çizimi PNG olarak kaydeder (--snapshot=yol).
    public void SaveSnapshot(string path)
    {
        if (buf != null) buf.Save(path, ImageFormat.Png);
    }

    // ---- fare / sürükleme ----
    // Sürükleme elle yapılır: "Birlikte taşı" açıksa sabitlenmemiş tüm görünür widget'lar aynı farkla hareket eder.
    bool dragging;
    Point dragStart;
    readonly List<WidgetForm> movers = new List<WidgetForm>();
    readonly List<Point> origins = new List<Point>();

    void BeginDrag(Point cursor)
    {
        movers.Clear(); origins.Clear();
        if (AppSettings.I.MoveTogether)
        {
            foreach (WidgetForm w in Group) if (w.Visible && !w.S.Pinned) movers.Add(w);
        }
        if (!movers.Contains(this)) movers.Add(this);
        foreach (WidgetForm w in movers) origins.Add(w.Location);
        dragStart = cursor;
        Capture = true;
        dragging = true;
    }

    void DragTo(Point cursor)
    {
        int dx = cursor.X - dragStart.X, dy = cursor.Y - dragStart.Y;
        for (int i = 0; i < movers.Count; i++)
            movers[i].Location = new Point(origins[i].X + dx, origins[i].Y + dy);
    }

    void EndDrag()
    {
        if (!dragging) return;
        dragging = false;
        Capture = false;
        foreach (WidgetForm w in movers) { w.S.X = w.Left; w.S.Y = w.Top; w.S.HasPos = true; }
        AppSettings.I.SetHome(Left + 1, Top + 1);   // diziliş/sıfırlama bu monitörde yapılsın
        Settings.SaveAll();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !OnCardMouseDown(e.Location) && !S.Pinned) BeginDrag(Cursor.Position);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging) DragTo(Cursor.Position);
        else OnCardMouseMove(e.Location);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { EndDrag(); base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (dragging && !Capture) EndDrag(); base.OnMouseCaptureChanged(e); }
    protected override void OnMouseLeave(EventArgs e) { if (!dragging) OnCardMouseLeave(); base.OnMouseLeave(e); }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        Group.Remove(this);
        Settings.AnyChanged -= OnSettings;
        base.OnFormClosed(e);
    }
}
