using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Windows.Forms;

class PomoSettings : Settings
{
    public int Mode;                                   // 0 pomodoro, 1 geri sayım
    public int FocusMin = 25, ShortMin = 5, LongMin = 15, LongEvery = 4, CountMin = 10;
    public bool AutoStart, Sound = true, Toast = true;

    protected override void Parse(string k, string v, int n, bool isInt)
    {
        switch (k)
        {
            case "mode": if (isInt) Mode = n; break;
            case "focus": if (isInt) FocusMin = n; break;
            case "short": if (isInt) ShortMin = n; break;
            case "long": if (isInt) LongMin = n; break;
            case "every": if (isInt) LongEvery = n; break;
            case "count": if (isInt) CountMin = n; break;
            case "auto": AutoStart = v == "1"; break;
            case "sound": Sound = v == "1"; break;
            case "toast": Toast = v == "1"; break;
        }
    }

    protected override void WriteExtra(List<string> l)
    {
        l.Add("mode=" + Mode);
        l.Add("focus=" + FocusMin);
        l.Add("short=" + ShortMin);
        l.Add("long=" + LongMin);
        l.Add("every=" + LongEvery);
        l.Add("count=" + CountMin);
        l.Add("auto=" + Bit(AutoStart));
        l.Add("sound=" + Bit(Sound));
        l.Add("toast=" + Bit(Toast));
    }

    protected override void Normalize()
    {
        Mode = Cl(Mode, 0, 1);
        FocusMin = Cl(FocusMin, 5, 90);
        ShortMin = Cl(ShortMin, 1, 30);
        LongMin = Cl(LongMin, 5, 60);
        LongEvery = Cl(LongEvery, 2, 8);
        CountMin = Cl(CountMin, 1, 180);
    }
}

// Pomodoro / geri sayım. Çalışmıyorken zamanlayıcı kapalıdır (0 CPU); çalışırken yalnızca saniye sınırında uyanır.
class PomodoroForm : WidgetForm
{
    enum Phase { Focus, Short, Long, Count }

    readonly PomoSettings ps;
    Phase phase = Phase.Focus;
    bool running, finished;
    DateTime endUtc;
    int remaining, done, lastTotal, lastMode;
    int hot = -1;
    Rectangle rPrimary, rReset, rSkip, rRing, rModeA, rModeB;
    float padX, padY, pipY;
    bool showSkip;

    public PomodoroForm(PomoSettings s) : base(s)
    {
        ps = s;
        lastMode = ps.Mode;
        phase = ps.Mode == 1 ? Phase.Count : Phase.Focus;
        remaining = lastTotal = Total();
    }

    protected override bool ClickThroughWhenPinned { get { return false; } }  // düğmeler sabitliyken de çalışsın
    protected override bool KeepAliveHidden { get { return running; } }       // gizliyken de süre dolunca uyarsın

    public bool Running { get { return running; } }

    // ---- durum makinesi ----
    int Total()
    {
        switch (phase)
        {
            case Phase.Focus: return ps.FocusMin * 60;
            case Phase.Short: return ps.ShortMin * 60;
            case Phase.Long: return ps.LongMin * 60;
            default: return ps.CountMin * 60;
        }
    }

    int Rem()
    {
        if (!running) return remaining;
        return Math.Max(0, (int)Math.Ceiling((endUtc - DateTime.UtcNow).TotalSeconds));
    }

    void StartRun()
    {
        if (finished) { finished = false; remaining = Total(); }
        if (remaining <= 0) remaining = Total();
        endUtc = DateTime.UtcNow.AddSeconds(remaining);
        running = true;
    }

    void PauseRun()
    {
        remaining = Rem();
        running = false;
    }

    public void ToggleRun()
    {
        if (running) PauseRun(); else StartRun();
        Redraw();
    }

    // Geçerli aşamayı başa alır; zaten baştaysa tüm döngüyü sıfırlar.
    public void ResetTimer()
    {
        bool atStart = !running && !finished && remaining == Total();
        running = false; finished = false;
        if (atStart) { done = 0; phase = ps.Mode == 1 ? Phase.Count : Phase.Focus; }
        remaining = Total();
        Redraw();
    }

    public void SkipPhase()
    {
        if (ps.Mode == 1) return;
        Advance(false);
        Redraw();
    }

    // Aşama bitti (ya da atlandı): sıradakine geç.
    void Advance(bool announce)
    {
        running = false;
        if (phase == Phase.Count)
        {
            finished = true; remaining = 0;
            if (announce) Alert("Süre doldu", "Geri sayım tamamlandı.");
            return;
        }
        if (phase == Phase.Focus)
        {
            done++;
            phase = (done % ps.LongEvery == 0) ? Phase.Long : Phase.Short;
            remaining = Total();
            if (announce) Alert("Odak bitti", (phase == Phase.Long ? "Uzun mola: " + ps.LongMin : "Kısa mola: " + ps.ShortMin) + " dk");
        }
        else
        {
            phase = Phase.Focus;
            remaining = Total();
            if (announce) Alert("Mola bitti", "Odaklanma zamanı: " + ps.FocusMin + " dk");
        }
        if (announce && ps.AutoStart) StartRun();
    }

    void Alert(string title, string text)
    {
        if (ps.Sound) SystemSounds.Asterisk.Play();
        if (ps.Toast) Notify(title, text);
    }

    protected override void OnTick()
    {
        if (running && Rem() <= 0) Advance(true);
    }

    protected override void OnSettingsApplied()
    {
        if (lastMode != ps.Mode)
        {
            lastMode = ps.Mode;
            running = false; finished = false; done = 0;
            phase = ps.Mode == 1 ? Phase.Count : Phase.Focus;
            remaining = Total();
        }
        else if (!running && !finished && remaining == lastTotal)
        {
            remaining = Total();   // henüz başlamadıysa yeni süreyi uygula
        }
        lastTotal = Total();
    }

    protected override int NextDelayMs()
    {
        if (!running) return 0;
        int ms = (int)(endUtc - DateTime.UtcNow).TotalMilliseconds;
        if (ms <= 0) return 50;
        return ms - ((ms - 1) / 1000) * 1000 + 15;
    }

    // ---- yerleşim ----
    protected override void MeasureCard()
    {
        padX = 22 * Sc; padY = 18 * Sc;
        showSkip = ps.Mode == 0;

        // Üstte mod anahtarı (Pomodoro | Geri sayım): tam genişlikte iki yarım.
        int inner0 = (int)(BW - 2 * padX), segH = (int)(28 * Sc);
        rModeA = new Rectangle((int)padX, (int)padY, inner0 / 2, segH);
        rModeB = new Rectangle((int)padX + inner0 / 2, (int)padY, inner0 - inner0 / 2, segH);
        float ringTop = padY + segH + 14 * Sc;
        float d = 132 * Sc;
        rRing = new Rectangle((int)((BW - d) / 2), (int)ringTop, (int)d, (int)d);

        float y = ringTop + d + 16 * Sc;
        pipY = y;
        if (ps.Mode == 0) y += 7 * Sc + 16 * Sc;

        int bh = (int)(32 * Sc), gap = (int)(8 * Sc);
        int inner = (int)(BW - 2 * padX);
        int skipW = showSkip ? bh : 0;
        int resetW = (int)(60 * Sc);
        int primW = inner - resetW - (showSkip ? skipW + gap : 0) - gap;
        int x = (int)padX;
        rPrimary = new Rectangle(x, (int)y, primW, bh); x += primW + gap;
        rReset = new Rectangle(x, (int)y, resetW, bh); x += resetW + gap;
        rSkip = showSkip ? new Rectangle(x, (int)y, skipW, bh) : Rectangle.Empty;

        BH = (int)Math.Ceiling(y + bh + padY);
    }

    // ---- çizim ----
    static string Fmt(int sec)
    {
        int h = sec / 3600, m = (sec % 3600) / 60, s = sec % 60;
        return h > 0 ? h + ":" + m.ToString("00") + ":" + s.ToString("00") : m.ToString("00") + ":" + s.ToString("00");
    }

    protected override void PaintContent(Graphics g)
    {
        Font fHdr = Fnt(false, 11.5f, true);
        bool brk = phase == Phase.Short || phase == Phase.Long;
        Color ring = (brk || finished) ? C.Good : C.Acc;
        string hdr = finished ? "BİTTİ" : (phase == Phase.Focus ? "ODAK" : phase == Phase.Short ? "KISA MOLA" : phase == Phase.Long ? "UZUN MOLA" : "GERİ SAYIM");
        DrawMode(g);

        // Halka: iz + kalan süre yayı
        int total = Math.Max(1, Total());
        int rem = Rem();
        float frac = finished ? 0f : Math.Min(1f, rem / (float)total);
        float sw = 8 * Sc;
        RectangleF rr = new RectangleF(rRing.X + sw / 2, rRing.Y + sw / 2, rRing.Width - sw, rRing.Height - sw);
        using (Pen track = new Pen(C.Track, sw)) g.DrawEllipse(track, rr);
        if (frac > 0.002f)
            using (Pen arc = new Pen(ring, sw))
            {
                arc.StartCap = LineCap.Round; arc.EndCap = LineCap.Round;
                g.DrawArc(arc, rr, -90f, 360f * frac);
            }

        string t = Fmt(rem);
        Font fT = Fnt(true, t.Length > 5 ? 23 : 30, true);
        // Aşama etiketi halkanın içinde, sürenin üstünde.
        float rcx = rRing.X + rRing.Width / 2f, timeTop = rRing.Y + rRing.Height / 2f - Cap(fT) / 2f + 8 * Sc;
        TxtC(g, t, fT, C.Txt, rcx, timeTop);
        TxtC(g, hdr, fHdr, (brk || finished) ? C.Good : C.Mut, rcx, timeTop - 12 * Sc - Cap(fHdr));

        // Oturum noktaları (yalnızca pomodoro modunda)
        if (ps.Mode == 0)
        {
            int n = ps.LongEvery;
            int filled = phase == Phase.Long ? n : done % n;
            float pd = 7 * Sc, pg = 8 * Sc, w = n * pd + (n - 1) * pg;
            float px = (BW - w) / 2f;
            for (int i = 0; i < n; i++)
            {
                RectangleF dot = new RectangleF(px + i * (pd + pg), pipY, pd, pd);
                using (Brush b = new SolidBrush(i < filled ? C.Acc : C.Track)) g.FillEllipse(b, dot);
            }
        }

        string primary = running ? "Duraklat" : (finished ? "Yeniden" : (remaining < Total() ? "Devam" : "Başlat"));
        DrawBtn(g, rPrimary, primary, true, hot == 0);
        DrawBtn(g, rReset, "Sıfırla", false, hot == 1);
        if (showSkip) DrawSkip(g, rSkip, hot == 2);
    }

    // Mod anahtarı: segmented (DESIGN.md §9) — seçili yarı accent dolu.
    void DrawMode(Graphics g)
    {
        Rectangle all = Rectangle.Union(rModeA, rModeB);
        RectangleF box = new RectangleF(all.X + 0.5f, all.Y + 0.5f, all.Width - 1, all.Height - 1);
        float rad = 10 * Sc, inset = 3 * Sc;
        Ui.FillRound(g, C.Track, box, rad);
        Ui.StrokeRound(g, C.Line, 1f, box, rad);
        Font f = Fnt(false, 12, true);
        for (int i = 0; i < 2; i++)
        {
            Rectangle r = i == 0 ? rModeA : rModeB;
            RectangleF ri = new RectangleF(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset);
            bool sel = ps.Mode == i, isHot = hot == 3 + i;
            Color fg;
            if (sel) { Ui.FillRound(g, C.Acc, ri, rad - inset); fg = C.Ink; }
            else if (isHot) { Ui.FillRound(g, Color.FromArgb(28, C.Txt), ri, rad - inset); fg = C.Txt; }
            else fg = C.Mut;
            TxtC(g, i == 0 ? "Pomodoro" : "Geri sayım", f, fg, r.X + r.Width / 2f, r.Y + r.Height / 2f - Cap(f) / 2f);
        }
    }

    // Mod değiştirmek zamanlayıcıyı sıfırlar (OnSettingsApplied).
    void SetMode(int m)
    {
        if (ps.Mode == m) return;
        ps.Mode = m;
        Settings.RaiseAll();
        Settings.SaveAll();
    }

    void DrawBtn(Graphics g, Rectangle r, string text, bool primary, bool isHot)
    {
        RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        float rad = 10 * Sc;
        Font f = Fnt(false, 13, true);
        Color fg;
        if (primary)
        {
            Ui.FillRound(g, isHot ? Ui.Mix(C.Acc, C.Light ? Color.Black : Color.White, 0.15f) : C.Acc, rf, rad);
            fg = C.Ink;
        }
        else
        {
            if (isHot) Ui.FillRound(g, C.Track, rf, rad);
            Ui.StrokeRound(g, isHot ? Color.FromArgb(82, C.Acc) : C.Line, 1f, rf, rad);
            fg = C.Txt;
        }
        TxtC(g, text, f, fg, r.X + r.Width / 2f, r.Y + r.Height / 2f - Cap(f) / 2f);
    }

    void DrawSkip(Graphics g, Rectangle r, bool isHot)
    {
        RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        float rad = 10 * Sc;
        if (isHot) Ui.FillRound(g, C.Track, rf, rad);
        Ui.StrokeRound(g, isHot ? Color.FromArgb(82, C.Acc) : C.Line, 1f, rf, rad);
        // "atla" simgesi: ▶| (üçgen + çubuk)
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = 5 * Sc;
        PointF[] tri = { new PointF(cx - s, cy - s), new PointF(cx - s, cy + s), new PointF(cx + s * 0.5f, cy) };
        using (Brush b = new SolidBrush(C.Txt))
        {
            g.FillPolygon(b, tri);
            g.FillRectangle(b, cx + s * 0.5f + 1.5f * Sc, cy - s, 2f * Sc, 2 * s);
        }
    }

    // ---- fare ----
    int Hit(Point p)
    {
        if (rPrimary.Contains(p)) return 0;
        if (rReset.Contains(p)) return 1;
        if (showSkip && rSkip.Contains(p)) return 2;
        if (rModeA.Contains(p)) return 3;
        if (rModeB.Contains(p)) return 4;
        return -1;
    }

    protected override bool OnCardMouseDown(Point p)
    {
        int h = Hit(p);
        if (h == 0) ToggleRun();
        else if (h == 1) ResetTimer();
        else if (h == 2) SkipPhase();
        else if (h >= 3) SetMode(h - 3);
        return h >= 0;
    }

    protected override void OnCardMouseMove(Point p)
    {
        int h = Hit(p);
        if (h == hot) return;
        hot = h;
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        Redraw();
    }

    protected override void OnCardMouseLeave()
    {
        if (hot < 0) return;
        hot = -1;
        Cursor = Cursors.Default;
        Redraw();
    }
}

// Pomodoro modunda süre satırları, geri sayım modunda yalnızca geri sayım süresi görünür (sayfa sığsın diye).
class PomoPage : WidgetPage
{
    readonly PomoSettings ps;
    Segmented segMode;
    Slider slFocus, slShort, slLong, slEvery, slCount;
    Toggle tgAuto, tgSound, tgToast;
    Panel panelPomo, panelCount;

    public PomoPage(PomoSettings s) { ps = s; }

    public override void Build(SettingsForm f, Panel p)
    {
        int y = Ui.S(4);
        segMode = f.AddSeg(p, ref y, "Mod", null, new string[] { "Pomodoro", "Geri sayım" }, delegate(int i) { ps.Mode = i; });
        tgToast = f.AddToggle(p, ref y, "Bildirim göster", "Tepsi simgesinden balon bildirimi.", delegate(bool b) { ps.Toast = b; });
        tgSound = f.AddToggle(p, ref y, "Bitince ses çal", null, delegate(bool b) { ps.Sound = b; });

        int rowH = Ui.S(48);
        panelPomo = new Panel();
        panelPomo.BackColor = Ui.Bg;
        panelPomo.Bounds = new Rectangle(0, y, f.CW, rowH * 5);
        panelCount = new Panel();
        panelCount.BackColor = Ui.Bg;
        panelCount.Bounds = new Rectangle(0, y, f.CW, rowH);
        p.Controls.Add(panelPomo);
        p.Controls.Add(panelCount);

        int ya = 0;
        slFocus = f.AddSlider(panelPomo, ref ya, "Odak süresi", null, 5, 90, 5, " dk", delegate(int v) { ps.FocusMin = v; });
        slShort = f.AddSlider(panelPomo, ref ya, "Kısa mola", null, 1, 30, 1, " dk", delegate(int v) { ps.ShortMin = v; });
        slLong = f.AddSlider(panelPomo, ref ya, "Uzun mola", null, 5, 60, 5, " dk", delegate(int v) { ps.LongMin = v; });
        slEvery = f.AddSlider(panelPomo, ref ya, "Uzun mola aralığı", null, 2, 8, 1, " ×", delegate(int v) { ps.LongEvery = v; });
        tgAuto = f.AddToggle(panelPomo, ref ya, "Sonrakini otomatik başlat", null, delegate(bool b) { ps.AutoStart = b; });
        int yb = 0;
        slCount = f.AddSlider(panelCount, ref yb, "Geri sayım süresi", null, 1, 180, 1, " dk", delegate(int v) { ps.CountMin = v; });
    }

    public override void Sync()
    {
        segMode.Selected = ps.Mode;
        slFocus.Value = ps.FocusMin;
        slShort.Value = ps.ShortMin;
        slLong.Value = ps.LongMin;
        slEvery.Value = ps.LongEvery;
        slCount.Value = ps.CountMin;
        tgAuto.Checked = ps.AutoStart;
        tgSound.Checked = ps.Sound;
        tgToast.Checked = ps.Toast;
        panelPomo.Visible = ps.Mode == 0;
        panelCount.Visible = ps.Mode == 1;
    }
}
