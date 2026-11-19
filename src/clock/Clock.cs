using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

class ClockSettings : Settings
{
    public bool H24 = true, Seconds, Date = true;
    public int DateFormat = 1;   // 0 kısa, 1 uzun, 2 uzun + gün adı
    public int Font;             // 0 mono, 1 sans

    protected override void Parse(string k, string v, int n, bool isInt)
    {
        switch (k)
        {
            case "h24": H24 = v == "1"; break;
            case "seconds": Seconds = v == "1"; break;
            case "date": Date = v == "1"; break;
            case "datefmt": if (isInt) DateFormat = n; break;
            case "font": if (isInt) Font = n; break;
        }
    }

    protected override void WriteExtra(List<string> l)
    {
        l.Add("h24=" + Bit(H24));
        l.Add("seconds=" + Bit(Seconds));
        l.Add("date=" + Bit(Date));
        l.Add("datefmt=" + DateFormat);
        l.Add("font=" + Font);
    }

    protected override void Normalize()
    {
        DateFormat = Cl(DateFormat, 0, 2);
        Font = Cl(Font, 0, 1);
    }
}

class ClockForm : WidgetForm
{
    readonly ClockSettings cs;
    string mainText, secText, subText;
    SizeF mainSz, secSz, subSz;
    float padX, padY, gap, mainVis, subVis, mainPx, subPx;

    public ClockForm(ClockSettings s) : base(s) { cs = s; }

    string BuildSub(DateTime n)
    {
        string d = "";
        if (cs.Date)
            d = cs.DateFormat == 0 ? n.ToString("dd.MM.yyyy", Ui.Tr)
              : cs.DateFormat == 1 ? n.ToString("d MMMM yyyy", Ui.Tr)
              : n.ToString("d MMMM yyyy, dddd", Ui.Tr);
        if (!cs.H24)
        {
            string ap = n.Hour < 12 ? "ÖÖ" : "ÖS";
            d = d.Length > 0 ? ap + "  ·  " + d : ap;
        }
        return d;
    }

    protected override void MeasureCard()
    {
        padX = 22 * Sc; padY = 18 * Sc; gap = 10 * Sc;
        float avail = BW - 2 * padX;

        DateTime n = DateTime.Now;
        CultureInfo inv = CultureInfo.InvariantCulture;
        mainText = n.ToString(cs.H24 ? "HH\\:mm" : "hh\\:mm", inv);
        secText = cs.Seconds ? n.ToString("\\:ss", inv) : "";
        subText = BuildSub(n);

        // Kart genişliği sabit: sığmayan metin orantılı küçülür (ör. saniyeli saat).
        bool mono = cs.Font == 0;
        float tplW = Wid(cs.Seconds ? "00:00:00" : "00:00", Fnt(mono, 46f, true));
        mainPx = (float)Math.Round(tplW > avail ? 46f * avail / tplW : 46f, 1);
        Font fMain = Fnt(mono, mainPx, true);
        subPx = 13.5f;
        if (subText.Length > 0)
        {
            float sw = Wid(subText, Fnt(false, 13.5f, false));
            if (sw > avail) subPx = (float)Math.Round(13.5f * avail / sw, 1);
        }
        Font fSub = Fnt(false, subPx, false);

        mainSz = Meas(mainText, fMain);
        secSz = secText.Length > 0 ? Meas(secText, fMain) : SizeF.Empty;
        subSz = subText.Length > 0 ? Meas(subText, fSub) : SizeF.Empty;

        mainVis = Cap(fMain);
        subVis = subText.Length > 0 ? Cap(fSub) + fSub.Size * 0.22f : 0;
        BH = (int)Math.Ceiling(padY + mainVis + (subVis > 0 ? gap + subVis : 0) + padY);
    }

    protected override void PaintContent(Graphics g)
    {
        Font fMain = Fnt(cs.Font == 0, mainPx, true), fSub = Fnt(false, subPx, false);
        float x = (BW - (mainSz.Width + secSz.Width)) / 2f;
        Txt(g, mainText, fMain, C.Txt, x, padY);
        if (secText.Length > 0) Txt(g, secText, fMain, C.Acc, x + mainSz.Width, padY);
        if (subText.Length > 0) Txt(g, subText, fSub, C.Mut, (BW - subSz.Width) / 2f, padY + mainVis + gap);
    }

    // Saniye/dakika sınırına hizalı: aradaki sürede CPU harcamaz.
    protected override int NextDelayMs()
    {
        DateTime n = DateTime.Now;
        return (cs.Seconds ? 1000 - n.Millisecond : (60 - n.Second) * 1000 - n.Millisecond) + 15;
    }
}

class ClockPage : WidgetPage
{
    readonly ClockSettings cs;
    Segmented segHour, segDateFmt, segFont;
    Toggle tgSec, tgDate;

    public ClockPage(ClockSettings s) { cs = s; }

    public override void Build(SettingsForm f, Panel p)
    {
        int y = Ui.S(4);
        segHour = f.AddSeg(p, ref y, "Saat biçimi", null, new string[] { "24 saat", "12 saat" }, delegate(int i) { cs.H24 = i == 0; });
        tgSec = f.AddToggle(p, ref y, "Saniyeyi göster", "Saniye vurgu renginde gösterilir.", delegate(bool b) { cs.Seconds = b; });
        tgDate = f.AddToggle(p, ref y, "Tarihi göster", null, delegate(bool b) { cs.Date = b; });
        segDateFmt = f.AddSeg(p, ref y, "Tarih biçimi", null, new string[] { "Kısa", "Uzun", "Gün adlı" }, delegate(int i) { cs.DateFormat = i; });
        segFont = f.AddSeg(p, ref y, "Rakam yazı tipi", null, new string[] { "Mono", "Sans" }, delegate(int i) { cs.Font = i; });
    }

    public override void Sync()
    {
        segHour.Selected = cs.H24 ? 0 : 1;
        tgSec.Checked = cs.Seconds;
        tgDate.Checked = cs.Date;
        segDateFmt.Selected = cs.DateFormat;
        segDateFmt.Enabled = cs.Date;
        segFont.Selected = cs.Font;
    }
}
