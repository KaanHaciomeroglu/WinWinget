using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

enum IconKind { Clock, Chart, Timer, Net, App }

// Kaan Design System (Design/DESIGN.md) → WinForms karşılığı.
static class Ui
{
    public static float K = 1f;
    public static readonly CultureInfo Tr = new CultureInfo("tr-TR");

    // Nötrler (koyu)
    public static readonly Color Bg = Palette.Hex("0B0C0E");
    public static readonly Color Surface2 = Palette.Hex("1A1D24");
    public static readonly Color Surface3 = Palette.Hex("222630");
    public static readonly Color InputBg = Palette.Hex("101115");
    public static readonly Color Text = Palette.Hex("F2F3F5");
    public static readonly Color Text2 = Palette.Hex("E0E2E8");
    public static readonly Color Muted = Palette.Hex("9BA1AC");
    public static readonly Color Muted2 = Palette.Hex("6B7280");
    public static readonly Color Danger = Palette.Hex("F87171");
    public static readonly Color DangerBg = Color.FromArgb(31, 248, 113, 113);
    public static readonly Color DangerBorder = Color.FromArgb(71, 248, 113, 113);
    public static readonly Color Line = Color.FromArgb(23, 255, 255, 255);
    public static readonly Color LineSolid = Palette.Hex("212224");
    public static readonly Color LineSoftSolid = Palette.Hex("17181A");
    public static readonly Color MenuBg = Palette.Hex("16181E");

    public static Color Accent, AccentHover, AccentInk, AccentSoftBg, AccentSoftBorder;

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    public static void Init()
    {
        try { SetProcessDPIAware(); } catch { }
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) K = g.DpiX / 96f;
        SetAccent(AppSettings.I.UiAccent);
    }

    public static int S(float v) { return (int)Math.Round(v * K); }

    public static void SetAccent(Color c)
    {
        Accent = c;
        AccentHover = Mix(c, Color.White, 0.2f);
        AccentInk = Color.FromArgb((int)(c.R * 0.09), (int)(c.G * 0.09), (int)(c.B * 0.09));
        AccentSoftBg = Color.FromArgb(38, c);
        AccentSoftBorder = Color.FromArgb(82, c);
    }

    public static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    // ---- yazı tipi: Manrope / IBM Plex Mono varsa onlar, yoksa Segoe UI / Consolas ----
    static readonly Dictionary<string, bool> installed = new Dictionary<string, bool>();
    static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

    static bool Has(string name)
    {
        bool ok;
        if (installed.TryGetValue(name, out ok)) return ok;
        try { using (new FontFamily(name)) ok = true; } catch { ok = false; }
        installed[name] = ok;
        return ok;
    }

    public static Font NewFont(bool mono, float px, bool strong)
    {
        string name; FontStyle st = FontStyle.Regular;
        if (mono)
        {
            name = Has("IBM Plex Mono") ? "IBM Plex Mono" : "Consolas";
            if (strong) st = FontStyle.Bold;
        }
        else if (Has("Manrope"))
        {
            name = "Manrope";
            if (strong) st = FontStyle.Bold;
        }
        else
        {
            name = strong ? "Segoe UI Semibold" : "Segoe UI";
        }
        return new Font(name, px, st, GraphicsUnit.Pixel);
    }

    // Arayüz yazı tipi (px, DPI ile ölçeklenir), önbellekli.
    public static Font F(float px, bool strong, bool mono)
    {
        string key = px + "|" + strong + "|" + mono;
        Font f;
        if (!cache.TryGetValue(key, out f)) { f = NewFont(mono, px * K, strong); cache[key] = f; }
        return f;
    }

    // ---- çizim yardımcıları ----
    public static GraphicsPath Round(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static GraphicsPath TopRound(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
    {
        using (GraphicsPath p = Round(r, rad)) using (Brush b = new SolidBrush(c)) g.FillPath(b, p);
    }

    public static void StrokeRound(Graphics g, Color c, float w, RectangleF r, float rad)
    {
        using (GraphicsPath p = Round(r, rad)) using (Pen pen = new Pen(c, w)) g.DrawPath(pen, p);
    }

    public static Icon MakeIcon(Color accent, IconKind kind)
    {
        using (Bitmap b = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(accent, 3.4f))
            using (Pen h = new Pen(Color.White, 2.8f))
            using (Brush ab = new SolidBrush(accent))
            using (Brush wb = new SolidBrush(Color.White))
            {
                h.StartCap = LineCap.Round; h.EndCap = LineCap.Round;
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                switch (kind)
                {
                    case IconKind.App:     // uygulama: 2×2 widget karosu
                        FillRound(g, accent, new RectangleF(3, 3, 12, 12), 3.5f);
                        FillRound(g, Color.White, new RectangleF(17, 3, 12, 12), 3.5f);
                        FillRound(g, Color.White, new RectangleF(3, 17, 12, 12), 3.5f);
                        FillRound(g, accent, new RectangleF(17, 17, 12, 12), 3.5f);
                        break;
                    case IconKind.Chart:   // sistem monitörü: üç çubuk
                        g.FillRectangle(ab, 4, 15, 6, 13);
                        g.FillRectangle(wb, 13, 5, 6, 23);
                        g.FillRectangle(ab, 22, 10, 6, 18);
                        break;
                    case IconKind.Timer:   // pomodoro: halka + tepe + ibre
                        g.DrawEllipse(p, 4, 6, 24, 24);
                        g.FillRectangle(wb, 13, 1, 6, 3);
                        g.DrawLine(h, 16, 18, 16, 12);
                        g.DrawLine(h, 16, 18, 21, 21);
                        break;
                    case IconKind.Net:     // ağ: wifi yayları
                        g.DrawArc(p, 2, 5, 28, 28, 225, 90);
                        g.DrawArc(h, 8, 11, 16, 16, 225, 90);
                        g.FillEllipse(wb, 13.5f, 22, 5, 5);
                        break;
                    default:               // saat
                        g.DrawEllipse(p, 3, 3, 25, 25);
                        g.DrawLine(h, 16, 16, 16, 8.5f);
                        g.DrawLine(h, 16, 16, 21.5f, 19.5f);
                        break;
                }
            }
            IntPtr hi = b.GetHicon();
            Icon icon = (Icon)Icon.FromHandle(hi).Clone();
            DestroyIcon(hi);
            return icon;
        }
    }

    public static ContextMenuStrip NewMenu(bool checkMargin)
    {
        ContextMenuStrip m = new ContextMenuStrip();
        m.Renderer = new DarkRenderer();
        m.Font = F(13.5f, false, false);
        m.ShowImageMargin = false;
        m.ShowCheckMargin = checkMargin;
        m.BackColor = MenuBg;
        m.ForeColor = Text2;
        return m;
    }

    // Alt menü (submenu) açılırken ana menüyle aynı koyu stili kullansın.
    public static void StyleSub(ToolStripMenuItem item)
    {
        item.DropDown.Renderer = new DarkRenderer();
        item.DropDown.Font = F(13.5f, false, false);
        item.DropDown.BackColor = MenuBg;
        item.DropDown.ForeColor = Text2;
        ToolStripDropDownMenu dm = item.DropDown as ToolStripDropDownMenu;
        if (dm != null) { dm.ShowImageMargin = false; dm.ShowCheckMargin = false; }
    }

    public static ToolStripMenuItem MenuItem(string text)
    {
        ToolStripMenuItem i = new ToolStripMenuItem(text);
        i.ForeColor = Text2;
        i.Padding = new Padding(S(2));
        return i;
    }
}

class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return Ui.MenuBg; } }
    public override Color ImageMarginGradientBegin { get { return Ui.MenuBg; } }
    public override Color ImageMarginGradientMiddle { get { return Ui.MenuBg; } }
    public override Color ImageMarginGradientEnd { get { return Ui.MenuBg; } }
    public override Color MenuItemSelected { get { return Ui.Surface3; } }
    public override Color MenuItemSelectedGradientBegin { get { return Ui.Surface3; } }
    public override Color MenuItemSelectedGradientEnd { get { return Ui.Surface3; } }
    public override Color MenuItemBorder { get { return Ui.Surface3; } }
    public override Color MenuBorder { get { return Ui.LineSolid; } }
    public override Color SeparatorDark { get { return Ui.LineSolid; } }
    public override Color SeparatorLight { get { return Ui.MenuBg; } }
    public override Color CheckBackground { get { return Ui.Mix(Ui.MenuBg, Ui.Accent, 0.28f); } }
    public override Color CheckSelectedBackground { get { return Ui.Mix(Ui.MenuBg, Ui.Accent, 0.28f); } }
    public override Color CheckPressedBackground { get { return Ui.Mix(Ui.MenuBg, Ui.Accent, 0.28f); } }
}

class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Ui.Text2 : Ui.Muted2;
        base.OnRenderItemText(e);
    }
}

// ================= Özel kontroller =================

abstract class Ctl : Control
{
    // Odak halkası yalnızca klavye kullanıldıktan sonra görünür (fareyle açılışta çıkmasın).
    public static bool KeyboardUsed;
    protected bool Hot, Down, KbFocus;
    protected const TextFormatFlags CenterFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                                  TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    protected Ctl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = Ui.Bg;
    }

    protected abstract void Draw(Graphics g);
    protected virtual float FocusRadius { get { return Ui.S(10); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        Draw(g);
        if (Focused && KbFocus && Enabled && KeyboardUsed)
            Ui.StrokeRound(g, Ui.Accent, 2f, new RectangleF(1, 1, Width - 2, Height - 2), FocusRadius);
    }

    protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Down = true; KbFocus = false; Focus(); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { KbFocus = !Down; Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { KbFocus = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override bool IsInputKey(Keys k)
    {
        return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
    }

    protected static Size Measure(string text, Font f)
    {
        return TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
    }
}

class Toggle : Ctl
{
    public bool Checked;
    public event EventHandler Toggled;

    public Toggle() { Size = new Size(Ui.S(40), Ui.S(22)); }

    protected override float FocusRadius { get { return Height / 2f; } }

    protected override void Draw(Graphics g)
    {
        RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
        float rad = r.Height / 2f;
        Color track = Checked ? Ui.Accent : (Hot ? Ui.Mix(Ui.Surface3, Color.White, 0.08f) : Ui.Surface3);
        if (!Enabled) track = Ui.Surface2;
        Ui.FillRound(g, track, r, rad);
        if (!Checked) Ui.StrokeRound(g, Ui.Line, 1f, r, rad);
        float d = Height - Ui.S(8);
        float x = Checked ? Width - Ui.S(4) - d : Ui.S(4);
        using (Brush b = new SolidBrush(Checked ? Ui.AccentInk : Ui.Muted))
            g.FillEllipse(b, x, (Height - d) / 2f, d, d);
    }

    protected override void OnClick(EventArgs e)
    {
        if (!Enabled) return;
        Checked = !Checked;
        Invalidate();
        if (Toggled != null) Toggled(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

// Segmented: sayfa içi görünüm/seçenek değiştirme (DESIGN.md §9 Tab'lar).
class Segmented : Ctl
{
    readonly string[] items;
    readonly int[] xs, ws;
    int hot = -1;
    public int Selected;
    public event EventHandler Changed;

    public Segmented(params string[] items)
    {
        this.items = items;
        xs = new int[items.Length]; ws = new int[items.Length];
        Font f = Ui.F(13, true, false);
        int outer = Ui.S(4), cx = outer;
        for (int i = 0; i < items.Length; i++)
        {
            ws[i] = Measure(items[i], f).Width + Ui.S(28);
            xs[i] = cx; cx += ws[i];
        }
        Size = new Size(cx + outer, Ui.S(36));
    }

    protected override float FocusRadius { get { return Ui.S(12); } }

    int HitTest(int px)
    {
        for (int i = 0; i < items.Length; i++) if (px >= xs[i] && px < xs[i] + ws[i]) return i;
        return -1;
    }

    void Pick(int i)
    {
        if (!Enabled || i < 0 || i >= items.Length || i == Selected) return;
        Selected = i; Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e) { hot = HitTest(e.X); Invalidate(); base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pick(HitTest(e.X)); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) { Pick(Selected - 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Right) { Pick(Selected + 1); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void Draw(Graphics g)
    {
        RectangleF box = new RectangleF(0, 0, Width - 1, Height - 1);
        Ui.FillRound(g, Ui.Surface2, box, Ui.S(12));
        Ui.StrokeRound(g, Ui.Line, 1f, box, Ui.S(12));
        Font f = Ui.F(13, true, false);
        for (int i = 0; i < items.Length; i++)
        {
            Rectangle r = new Rectangle(xs[i], Ui.S(4), ws[i], Height - Ui.S(8));
            bool sel = i == Selected;
            Color fg;
            if (sel && Enabled) { Ui.FillRound(g, Ui.Accent, r, Ui.S(8)); fg = Ui.AccentInk; }
            else if (sel) { Ui.FillRound(g, Ui.Surface3, r, Ui.S(8)); fg = Ui.Muted2; }
            else if (i == hot && Enabled) { Ui.FillRound(g, Ui.Surface3, r, Ui.S(8)); fg = Ui.Text2; }
            else fg = Enabled ? Ui.Muted : Ui.Muted2;
            TextRenderer.DrawText(g, items[i], f, r, fg, CenterFlags);
        }
    }
}

// Alt çizgili sekmeler: ayarlar / modal içi bölümler.
class UnderlineTabs : Ctl
{
    readonly string[] items;
    readonly int[] xs, ws;
    int hot = -1;
    public int Selected;
    public event EventHandler Changed;

    public UnderlineTabs(int width, params string[] items)
    {
        this.items = items;
        xs = new int[items.Length]; ws = new int[items.Length];
        Font f = Ui.F(13.5f, true, false);
        int cx = Ui.S(24);
        for (int i = 0; i < items.Length; i++)
        {
            ws[i] = Measure(items[i], f).Width + Ui.S(28);
            xs[i] = cx; cx += ws[i] + Ui.S(2);
        }
        Size = new Size(width, Ui.S(40));
    }

    protected override float FocusRadius { get { return Ui.S(8); } }

    int HitTest(int px)
    {
        for (int i = 0; i < items.Length; i++) if (px >= xs[i] && px < xs[i] + ws[i]) return i;
        return -1;
    }

    void Pick(int i)
    {
        if (i < 0 || i >= items.Length || i == Selected) return;
        Selected = i; Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e) { hot = HitTest(e.X); Invalidate(); base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pick(HitTest(e.X)); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) { Pick(Selected - 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Right) { Pick(Selected + 1); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void Draw(Graphics g)
    {
        using (Pen p = new Pen(Ui.LineSolid, 1f)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        Font f = Ui.F(13.5f, true, false);
        for (int i = 0; i < items.Length; i++)
        {
            Rectangle r = new Rectangle(xs[i], 0, ws[i], Height - 1);
            bool sel = i == Selected;
            Color fg = sel ? Ui.Accent : (i == hot ? Ui.Text2 : Ui.Muted);
            if (sel)
            {
                using (GraphicsPath tp = Ui.TopRound(r, Ui.S(8))) using (Brush b = new SolidBrush(Ui.AccentSoftBg)) g.FillPath(b, tp);
                using (Brush b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, xs[i], Height - Ui.S(2), ws[i], Ui.S(2));
            }
            TextRenderer.DrawText(g, items[i], f, r, fg, CenterFlags);
        }
    }
}

// Vurgu rengi seçici (DESIGN.md §11: 36px kareler, aktif = beyaz kenar + glow).
class Swatches : Ctl
{
    readonly Color[] cols;
    readonly int cell, gap, pad;
    int hot = -1;
    public int Selected;
    public event EventHandler Changed;

    public Swatches(Color[] colors)
    {
        cols = colors;
        cell = Ui.S(36); gap = Ui.S(8); pad = Ui.S(5);
        Size = new Size(pad * 2 + cols.Length * cell + (cols.Length - 1) * gap, cell + pad * 2);
    }

    protected override float FocusRadius { get { return Ui.S(8); } }

    Rectangle Cell(int i) { return new Rectangle(pad + i * (cell + gap), pad, cell, cell); }

    int HitTest(Point p)
    {
        for (int i = 0; i < cols.Length; i++) if (Cell(i).Contains(p)) return i;
        return -1;
    }

    void Pick(int i)
    {
        if (i < 0 || i >= cols.Length || i == Selected) return;
        Selected = i; Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e) { hot = HitTest(e.Location); Invalidate(); base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pick(HitTest(e.Location)); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) { Pick(Selected - 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Right) { Pick(Selected + 1); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void Draw(Graphics g)
    {
        for (int i = 0; i < cols.Length; i++)
        {
            Rectangle r = Cell(i);
            RectangleF rf = new RectangleF(r.X, r.Y, r.Width, r.Height);
            Ui.FillRound(g, cols[i], rf, Ui.S(8));
            if (i == Selected)
            {
                Ui.StrokeRound(g, Color.FromArgb(90, cols[i]), Ui.S(3), RectangleF.Inflate(rf, Ui.S(2), Ui.S(2)), Ui.S(10));
                Ui.StrokeRound(g, Ui.Text, 2f, RectangleF.Inflate(rf, -1f, -1f), Ui.S(7));
            }
            else if (i == hot)
            {
                Ui.StrokeRound(g, Color.FromArgb(130, Ui.Text), 1.5f, RectangleF.Inflate(rf, -1f, -1f), Ui.S(7));
            }
        }
    }
}

class Slider : Ctl
{
    public int Min, Max, Value, Step;
    public string Suffix = "%";
    public event EventHandler ValueChanged;
    public event EventHandler Committed;
    readonly int valW;
    bool dragging;

    public Slider(int min, int max, int step, int width)
    {
        Min = min; Max = max; Step = step; Value = min;
        valW = Ui.S(52);
        Size = new Size(Ui.S(width), Ui.S(28));
    }

    protected override float FocusRadius { get { return Ui.S(8); } }

    Rectangle Track { get { return new Rectangle(Ui.S(9), 0, Width - valW - Ui.S(22), Height); } }

    void SetFromX(int px)
    {
        Rectangle t = Track;
        double f = Math.Max(0, Math.Min(1, (px - t.Left) / (double)t.Width));
        int v = Min + (int)Math.Round(f * (Max - Min) / Step) * Step;
        SetValue(Math.Max(Min, Math.Min(Max, v)));
    }

    void SetValue(int v)
    {
        if (v == Value) return;
        Value = v; Invalidate();
        if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); dragging = true; SetFromX(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (dragging) { dragging = false; if (Committed != null) Committed(this, EventArgs.Empty); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int d = 0;
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) d = -Step;
        else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) d = Step;
        if (d != 0)
        {
            SetValue(Math.Max(Min, Math.Min(Max, Value + d)));
            if (Committed != null) Committed(this, EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void Draw(Graphics g)
    {
        Rectangle t = Track;
        float th = Ui.S(6), cy = Height / 2f;
        RectangleF tr = new RectangleF(t.Left, cy - th / 2, t.Width, th);
        Ui.FillRound(g, Ui.InputBg, tr, th / 2);
        Ui.StrokeRound(g, Ui.Line, 1f, tr, th / 2);
        float frac = (Value - Min) / (float)(Max - Min);
        float tx = t.Left + frac * t.Width;
        if (frac > 0) Ui.FillRound(g, Ui.Accent, new RectangleF(t.Left, cy - th / 2, Math.Max(th, tx - t.Left), th), th / 2);
        float d = Ui.S(16);
        if (Hot || dragging)
            using (Brush b = new SolidBrush(Color.FromArgb(60, Ui.Accent))) g.FillEllipse(b, tx - d / 2 - Ui.S(4), cy - d / 2 - Ui.S(4), d + Ui.S(8), d + Ui.S(8));
        using (Brush b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, tx - d / 2, cy - d / 2, d, d);
        Rectangle vr = new Rectangle(Width - valW, 0, valW, Height);
        TextRenderer.DrawText(g, Value + Suffix, Ui.F(12.5f, true, true), vr, Ui.Text2,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}

enum BtnKind { Primary, Ghost, Danger }

class Btn : Ctl
{
    readonly string text;
    readonly BtnKind kind;

    public Btn(string text, BtnKind kind)
    {
        this.text = text; this.kind = kind;
        Size = new Size(Measure(text, Ui.F(13, true, false)).Width + Ui.S(32), Ui.S(34));
    }

    protected override void Draw(Graphics g)
    {
        RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
        float rad = Ui.S(10);
        Color fg;
        switch (kind)
        {
            case BtnKind.Primary:
                Ui.FillRound(g, Hot ? Ui.AccentHover : Ui.Accent, r, rad);
                fg = Ui.AccentInk;
                break;
            case BtnKind.Danger:
                if (Hot) Ui.FillRound(g, Ui.DangerBg, r, rad);
                Ui.StrokeRound(g, Ui.DangerBorder, 1f, r, rad);
                fg = Ui.Danger;
                break;
            default:
                if (Hot) Ui.FillRound(g, Ui.Surface2, r, rad);
                Ui.StrokeRound(g, Hot ? Ui.AccentSoftBorder : Ui.Line, 1f, r, rad);
                fg = Ui.Text2;
                break;
        }
        TextRenderer.DrawText(g, text, Ui.F(13, true, false), new Rectangle(0, 0, Width, Height), fg, CenterFlags);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
