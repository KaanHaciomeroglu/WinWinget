using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Widget'a özel ayar sayfası: kendi sekmesini kurar ve ayar değişince kontrollerini eşitler.
abstract class WidgetPage
{
    public abstract void Build(SettingsForm f, Panel page);
    public abstract void Sync();
}

// Hub'ın yönettiği bir widget: ayarları, penceresi ve ayar sayfası.
class WidgetDef
{
    public string Id, Title;
    public Settings S;
    public WidgetForm Form;
    public WidgetPage Page;

    public WidgetDef(string id, string title, Settings s, WidgetForm form, WidgetPage page)
    {
        Id = id; Title = title; S = s; Form = form; Page = page;
    }
}

// Tek ayar penceresi: Genel (göster/sabitle + uygulama) · Görünüm (ortak) · her widget için bir sekme.
// Değişiklikler anında uygulanır ve kaydedilir.
class SettingsForm : Form
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public readonly int CW;
    readonly List<WidgetDef> defs;
    readonly Panel[] pages;
    readonly UnderlineTabs tabs;
    Segmented segTheme;
    Swatches sw;
    Slider slScale, slOpacity;
    Toggle tgTop, tgStart, tgTogether;
    Toggle[] tgShow, tgPin;
    Label lblAccent;
    readonly Icon icon;

    public int G { get { return Ui.S(24); } }

    public SettingsForm(List<WidgetDef> defs, Action alignVertical, int tab)
    {
        this.defs = defs;
        Ui.SetAccent(AppSettings.I.UiAccent);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "WinWinget – Ayarlar";
        BackColor = Ui.Bg;
        ForeColor = Ui.Text;
        CW = Ui.S(520);
        ClientSize = new Size(CW, Ui.S(600));
        icon = Ui.MakeIcon(Ui.Accent, IconKind.App);
        Icon = icon;

        Label title = Lab("Ayarlar", 25, Ui.Text, true, false);
        title.Location = new Point(G, Ui.S(20));
        Label sub = Lab("Masaüstü widget'ları", 13.5f, Ui.Muted, false, false);
        sub.Location = new Point(G, Ui.S(58));
        Controls.Add(title); Controls.Add(sub);

        string[] names = new string[defs.Count + 2];
        names[0] = "Genel"; names[1] = "Görünüm";
        for (int i = 0; i < defs.Count; i++) names[i + 2] = defs[i].Title;
        tabs = new UnderlineTabs(CW, names);
        tabs.Location = new Point(0, Ui.S(88));
        tabs.Changed += delegate { ShowPage(); };
        Controls.Add(tabs);

        pages = new Panel[names.Length];
        for (int i = 0; i < pages.Length; i++)
        {
            Panel p = new Panel();
            p.BackColor = Ui.Bg;
            p.Bounds = new Rectangle(0, Ui.S(128), CW, Ui.S(412));
            pages[i] = p;
            Controls.Add(p);
        }
        BuildGeneral(pages[0]);
        BuildLook(pages[1]);
        for (int i = 0; i < defs.Count; i++) defs[i].Page.Build(this, pages[i + 2]);

        // Alt çubuk: dikeyde hizala · konumları sıfırla · çıkış ........ tamam
        Panel footLine = new Panel();
        footLine.BackColor = Ui.LineSolid;
        footLine.Bounds = new Rectangle(0, Ui.S(540), CW, 1);
        Controls.Add(footLine);

        int fy = Ui.S(540) + Ui.S(13);
        Btn align = new Btn("Dikeyde hizala", BtnKind.Ghost);
        align.Location = new Point(G, fy);
        align.Click += delegate { alignVertical(); };
        Btn reset = new Btn("Konumları sıfırla", BtnKind.Ghost);
        reset.Location = new Point(align.Right + Ui.S(8), fy);
        reset.Click += delegate { Edit(delegate { foreach (WidgetDef d in this.defs) d.S.HasPos = false; }); };
        Btn quit = new Btn("Çıkış", BtnKind.Danger);
        quit.Location = new Point(reset.Right + Ui.S(8), fy);
        quit.Click += delegate { Application.Exit(); };
        Btn ok = new Btn("Tamam", BtnKind.Primary);
        ok.Location = new Point(CW - G - ok.Width, fy);
        ok.Click += delegate { Close(); };
        Controls.Add(align); Controls.Add(reset); Controls.Add(quit); Controls.Add(ok);

        tabs.Selected = Math.Max(0, Math.Min(pages.Length - 1, tab));
        Sync();
        ShowPage();
        Settings.AnyChanged += OnExternalChange;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Koyu başlık çubuğu + zemin rengiyle eşleşen başlık (Windows 10 1809+ / 11).
        int on = 1;
        DwmSetWindowAttribute(Handle, 20, ref on, 4);
        int cap = Ui.Bg.R | (Ui.Bg.G << 8) | (Ui.Bg.B << 16);
        DwmSetWindowAttribute(Handle, 35, ref cap, 4);
        int txt = Ui.Text.R | (Ui.Text.G << 8) | (Ui.Text.B << 16);
        DwmSetWindowAttribute(Handle, 36, ref txt, 4);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift) || keyData == Keys.Left || keyData == Keys.Right)
        {
            Ctl.KeyboardUsed = true;
            Invalidate(true);
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Settings.AnyChanged -= OnExternalChange;
        base.OnFormClosed(e);
        icon.Dispose();
    }

    // ---- yapı taşları (widget sayfaları da kullanır) ----
    public Label Lab(string text, float px, Color c, bool strong, bool mono)
    {
        Label l = new Label();
        l.AutoSize = true;
        l.Text = text;
        l.Font = Ui.F(px, strong, mono);
        l.ForeColor = c;
        l.BackColor = Ui.Bg;
        l.UseCompatibleTextRendering = false;
        l.Margin = Padding.Empty;
        return l;
    }

    void ShowPage()
    {
        for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == tabs.Selected;
    }

    // Ayarı değiştir → tüm widget'lara uygula → kaydet.
    public void Edit(Action change)
    {
        change();
        Settings.RaiseAll();
        Settings.SaveAll();
    }

    public int Divider(Panel p, int y)
    {
        Panel d = new Panel();
        d.BackColor = Ui.LineSoftSolid;
        d.Bounds = new Rectangle(G, y - 1, CW - 2 * G, 1);
        p.Controls.Add(d);
        return y;
    }

    // Sol: başlık (+ ipucu), sağ: kontrol. Bir sonraki y'yi döndürür.
    public int Row(Panel p, int y, string title, string hint, Control c)
    {
        int textW = CW - 2 * G - c.Width - Ui.S(20);
        Label t = Lab(title, 13.5f, Ui.Text2, false, false);
        Label h = null;
        int hh = 0;
        if (hint != null)
        {
            h = Lab(hint, 12, Ui.Muted, false, false);
            h.MaximumSize = new Size(textW, 0);
            hh = h.GetPreferredSize(new Size(textW, 0)).Height;
        }
        int th = t.PreferredHeight;
        int block = th + (h != null ? Ui.S(2) + hh : 0);
        int rowH = Math.Max(Ui.S(48), block + Ui.S(24));
        t.Location = new Point(G, y + (rowH - block) / 2);
        p.Controls.Add(t);
        if (h != null)
        {
            h.Location = new Point(G, t.Top + th + Ui.S(2));
            p.Controls.Add(h);
        }
        c.Location = new Point(CW - G - c.Width, y + (rowH - c.Height) / 2);
        p.Controls.Add(c);
        Divider(p, y + rowH);
        return y + rowH;
    }

    public Segmented AddSeg(Panel p, ref int y, string title, string hint, string[] items, Action<int> apply)
    {
        Segmented seg = new Segmented(items);
        seg.Changed += delegate { Edit(delegate { apply(seg.Selected); }); };
        y = Row(p, y, title, hint, seg);
        return seg;
    }

    public Toggle AddToggle(Panel p, ref int y, string title, string hint, Action<bool> apply)
    {
        Toggle tg = new Toggle();
        tg.Toggled += delegate { Edit(delegate { apply(tg.Checked); }); };
        y = Row(p, y, title, hint, tg);
        return tg;
    }

    // Kaydırıcı: sürüklerken canlı uygular, bırakınca kaydeder.
    public Slider AddSlider(Panel p, ref int y, string title, string hint, int min, int max, int step, string suffix, Action<int> apply)
    {
        Slider sl = new Slider(min, max, step, 250);
        sl.Suffix = suffix;
        sl.ValueChanged += delegate { apply(sl.Value); Settings.RaiseAll(); };
        sl.Committed += delegate { Settings.SaveAll(); };
        y = Row(p, y, title, hint, sl);
        return sl;
    }

    // ---- sayfalar ----
    void BuildGeneral(Panel p)
    {
        int y = Ui.S(4);
        int tw = Ui.S(40);
        int xPin = CW - G - tw, xShow = xPin - Ui.S(80);

        // Sütun başlıkları
        Label c1 = Lab("GÖSTER", 11.5f, Ui.Muted, true, false);
        Label c2 = Lab("SABİTLE", 11.5f, Ui.Muted, true, false);
        c1.Location = new Point(xShow + tw / 2 - c1.PreferredWidth / 2, y + Ui.S(10));
        c2.Location = new Point(xPin + tw / 2 - c2.PreferredWidth / 2, y + Ui.S(10));
        Label wl = Lab("WIDGET'LAR", 11.5f, Ui.Muted, true, false);
        wl.Location = new Point(G, y + Ui.S(10));
        p.Controls.Add(c1); p.Controls.Add(c2); p.Controls.Add(wl);
        y += Ui.S(36);

        tgShow = new Toggle[defs.Count];
        tgPin = new Toggle[defs.Count];
        for (int i = 0; i < defs.Count; i++)
        {
            WidgetDef d = defs[i];
            Label t = Lab(d.Title, 13.5f, Ui.Text2, false, false);
            t.Location = new Point(G, y + (Ui.S(48) - t.PreferredHeight) / 2);
            p.Controls.Add(t);

            Toggle show = new Toggle();
            show.Location = new Point(xShow, y + (Ui.S(48) - show.Height) / 2);
            show.Toggled += delegate { Edit(delegate { d.S.Enabled = show.Checked; }); };
            Toggle pin = new Toggle();
            pin.Location = new Point(xPin, y + (Ui.S(48) - pin.Height) / 2);
            pin.Toggled += delegate { Edit(delegate { d.S.Pinned = pin.Checked; }); };
            p.Controls.Add(show); p.Controls.Add(pin);
            tgShow[i] = show; tgPin[i] = pin;

            y = Divider(p, y + Ui.S(48));
        }

        y += Ui.S(8);
        tgTogether = AddToggle(p, ref y, "Birlikte taşı", "Birini sürüklerken sabitlenmemiş hepsi birlikte hareket eder.",
            delegate(bool b) { AppSettings.I.MoveTogether = b; });
        tgTop = AddToggle(p, ref y, "Her zaman üstte", null, delegate(bool b) { AppSettings.I.TopMost = b; });
        tgStart = AddToggle(p, ref y, "Windows ile başlat", null, delegate(bool b) { Startup.Set(b); });
    }

    void BuildLook(Panel p)
    {
        int y = Ui.S(4);

        segTheme = AddSeg(p, ref y, "Tema", null, new string[] { "Koyu", "Açık" }, delegate(int i) { AppSettings.I.Theme = i; });

        // Vurgu rengi: başlık + seçili ad üstte, swatch'lar tam genişlikte.
        Label t = Lab("Vurgu rengi", 13.5f, Ui.Text2, false, false);
        t.Location = new Point(G, y + Ui.S(14));
        p.Controls.Add(t);
        lblAccent = Lab("", 12, Ui.Muted, false, false);
        lblAccent.Location = new Point(G + Ui.S(110), y + Ui.S(16));
        p.Controls.Add(lblAccent);
        Color[] cols = new Color[Palette.Count];
        for (int i = 0; i < cols.Length; i++) cols[i] = Palette.Get(i, false);
        sw = new Swatches(cols);
        sw.Location = new Point(G - Ui.S(5), y + Ui.S(40));
        sw.Changed += delegate { Edit(delegate { AppSettings.I.Accent = sw.Selected; }); };
        p.Controls.Add(sw);
        y = Divider(p, y + Ui.S(96));

        slScale = AddSlider(p, ref y, "Boyut", "Tüm widget'lar aynı genişlikte kalır.", 60, 200, 5, "%",
            delegate(int v) { AppSettings.I.Scale = v; });
        slOpacity = AddSlider(p, ref y, "Arka plan opaklığı", null, 20, 100, 5, "%",
            delegate(int v) { AppSettings.I.Opacity = v; });
    }

    // ---- ayarlar başka yerden değişince (tepsi menüsü vb.) kontrolleri eşitle ----
    void OnExternalChange(object sender, EventArgs e) { Sync(); }

    void Sync()
    {
        AppSettings a = AppSettings.I;
        Ui.SetAccent(a.UiAccent);
        for (int i = 0; i < defs.Count; i++)
        {
            tgShow[i].Checked = defs[i].S.Enabled;
            tgPin[i].Checked = defs[i].S.Pinned;
            defs[i].Page.Sync();
        }
        tgTogether.Checked = a.MoveTogether;
        tgTop.Checked = a.TopMost;
        tgStart.Checked = Startup.IsOn();
        segTheme.Selected = a.Theme;
        sw.Selected = a.Accent;
        lblAccent.Text = Palette.Names[a.Accent];
        slScale.Value = a.Scale;
        slOpacity.Value = a.Opacity;
        Invalidate(true);
    }
}
