using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

class NetSettings : Settings
{
    public bool ShowSpeed = true, ShowPing = true, ShowIp = true, ShowPublicIp = true, IpHidden, PubHidden;
    public int Target;       // 0 Cloudflare 1.1.1.1, 1 Google 8.8.8.8, 2 ağ geçidi
    public int PingEvery = 1; // 0 = 5 sn, 1 = 10 sn, 2 = 30 sn

    protected override void Parse(string k, string v, int n, bool isInt)
    {
        switch (k)
        {
            case "speed": ShowSpeed = v == "1"; break;
            case "ping": ShowPing = v == "1"; break;
            case "ip": ShowIp = v == "1"; break;
            case "pubip": ShowPublicIp = v == "1"; break;
            case "iphidden": IpHidden = v == "1"; break;
            case "pubhidden": PubHidden = v == "1"; break;
            case "target": if (isInt) Target = n; break;
            case "pingevery": if (isInt) PingEvery = n; break;
        }
    }

    protected override void WriteExtra(List<string> l)
    {
        l.Add("speed=" + Bit(ShowSpeed));
        l.Add("ping=" + Bit(ShowPing));
        l.Add("ip=" + Bit(ShowIp));
        l.Add("pubip=" + Bit(ShowPublicIp));
        l.Add("iphidden=" + Bit(IpHidden));
        l.Add("pubhidden=" + Bit(PubHidden));
        l.Add("target=" + Target);
        l.Add("pingevery=" + PingEvery);
    }

    protected override void Normalize()
    {
        Target = Cl(Target, 0, 2);
        PingEvery = Cl(PingEvery, 0, 2);
    }

    public int PingSeconds { get { return PingEvery == 0 ? 5 : (PingEvery == 1 ? 10 : 30); } }
}

// Bağlantı durumu, VPN, yerel + dış IP, gecikme ve hız. Ağ değişince hemen, aksi halde seyrek yenilenir.
class NetStatusForm : WidgetForm
{
    readonly NetSettings ns;

    // Arayüz durumu
    bool linkUp, vpnUp, vpnRoutes;   // vpnUp: VPN açık; vpnRoutes: internet trafiği VPN'den çıkıyor (dış IP değişir)
    string linkType = "", vpnName = "", localIp = "", gateway = "";

    // Dış IP (VPN bağlıyken VPN çıkış IP'si). Durum değişince / 10 dk'da bir sorulur; tek seferde bir istek.
    static readonly string[] PubUrls = { "https://api.ipify.org", "https://checkip.amazonaws.com" };
    bool pubBusy, pubKnown;
    string pubIp = "";
    int pubGen;
    DateTime pubNext = DateTime.MinValue;

    // Ping
    volatile bool pinging, pingOk;
    volatile int pingMs = -1;
    bool pingKnown;
    DateTime lastPing = DateTime.MinValue;

    // Hız
    long pRx, pTx;
    DateTime pTime = DateTime.UtcNow;
    double down, up;

    float padX, padY;

    // IP satırlarındaki göz düğmeleri (her satır ayrı gizlenir). Düğme tıklanabilsin diye sabitliyken de tıklamayı geçirmez.
    Rectangle rEye = Rectangle.Empty, rEyePub = Rectangle.Empty;
    int eyeHot = -1;   // -1 yok, 0 yerel, 1 dış

    protected override bool ClickThroughWhenPinned { get { return false; } }

    int EyeAt(Point p)
    {
        if (!rEye.IsEmpty && rEye.Contains(p)) return 0;
        if (!rEyePub.IsEmpty && rEyePub.Contains(p)) return 1;
        return -1;
    }

    protected override bool OnCardMouseDown(Point p)
    {
        int e = EyeAt(p);
        if (e < 0) return false;
        if (e == 0) ns.IpHidden = !ns.IpHidden; else ns.PubHidden = !ns.PubHidden;
        ns.Raise();
        Settings.SaveAll();
        return true;
    }

    protected override void OnCardMouseMove(Point p)
    {
        int h = EyeAt(p);
        if (h == eyeHot) return;
        eyeHot = h;
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        Redraw();
    }

    protected override void OnCardMouseLeave()
    {
        if (eyeHot < 0) return;
        eyeHot = -1;
        Cursor = Cursors.Default;
        Redraw();
    }

    // Göz simgesi (Lucide tarzı çizgi ikon); gizliyken üstü çizili.
    void DrawEye(Graphics g, Rectangle r, bool slashed, bool hot)
    {
        if (hot) Ui.FillRound(g, C.Track, r, 6 * Sc);
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        float w = r.Width * 0.62f, h = w * 0.46f;
        Color col = hot ? C.Txt : C.Mut;
        using (Pen p = new Pen(col, Math.Max(1.4f, 1.5f * Sc)))
        using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            path.AddBezier(cx - w / 2, cy, cx - w / 4, cy - h * 1.15f, cx + w / 4, cy - h * 1.15f, cx + w / 2, cy);
            path.AddBezier(cx + w / 2, cy, cx + w / 4, cy + h * 1.15f, cx - w / 4, cy + h * 1.15f, cx - w / 2, cy);
            g.DrawPath(p, path);
            float pr = h * 0.42f;
            g.DrawEllipse(p, cx - pr, cy - pr, pr * 2, pr * 2);
            if (slashed) g.DrawLine(p, cx - w / 2 + 1 * Sc, cy + h * 1.05f, cx + w / 2 - 1 * Sc, cy - h * 1.05f);
        }
    }

    // Satır düzeni (Layout'ta hesaplanır)
    readonly List<string> rows = new List<string>();

    public NetStatusForm(NetSettings s) : base(s)
    {
        ns = s;
        NetInfo.Sample(out pRx, out pTx);
        RefreshLinks();
        NetworkChange.NetworkAddressChanged += OnNetChanged;
    }

    void OnNetChanged(object sender, EventArgs e)
    {
        try
        {
            BeginInvoke(new Action(delegate
            {
                RefreshLinks();
                lastPing = DateTime.MinValue;
                // Yollar otursun diye biraz bekleyip yeniden sor (değer silinmez; durum değiştiyse RefreshLinks siler).
                DateTime soon = DateTime.UtcNow.AddSeconds(2);
                if (pubNext > soon) pubNext = soon;
                Redraw();
            }));
        }
        catch { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        NetworkChange.NetworkAddressChanged -= OnNetChanged;
        base.OnFormClosed(e);
    }

    // ---- veri toplama ----
    void RefreshLinks()
    {
        bool up = false, vpn = false, routes = false;
        string type = "", ip = "", gw = "", vname = "";
        int bestScore = -1;
        int best = NetInfo.BestIfIndex();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                int k = NetInfo.Kind(ni);
                if (k == 0) continue;
                bool isBest = best >= 0 && NetInfo.IfIndex(ni) == best;
                // VPN: internet trafiği ondan çıkıyorsa bağlı. Sanal LAN'lar (Radmin) varsayılan yol değildir ve
                // kapalıyken de "Up" kalır → Radmin için güç düğmesi okunur.
                if (k == 2)
                {
                    bool on = isBest;
                    if (!on && (ni.Description + " " + ni.Name).ToLowerInvariant().Contains("radmin")) on = NetInfo.RadminOn() == true;
                    if (on && (!vpn || isBest)) { vpn = true; vname = ni.Name; }
                    if (isBest) routes = true;
                    continue;
                }

                IPInterfaceProperties props = ni.GetIPProperties();
                string niGw = "", niIp = "";
                foreach (GatewayIPAddressInformation ga in props.GatewayAddresses)
                    if (ga.Address.AddressFamily == AddressFamily.InterNetwork && !ga.Address.Equals(IPAddress.Any)) { niGw = ga.Address.ToString(); break; }
                foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) { niIp = ua.Address.ToString(); break; }
                if (niIp.Length == 0 && niGw.Length == 0) continue;

                int score = (isBest ? 4 : 0) + (niGw.Length > 0 ? 2 : 0) + (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0);
                if (score > bestScore)
                {
                    bestScore = score; up = true; ip = niIp; gw = niGw;
                    type = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet";
                }
            }
        }
        catch { }

        // Bağlantı / VPN / adres değiştiyse dış IP eskidi: sil ve kısa süre sonra yeniden sor.
        if (up != linkUp || routes != vpnRoutes || ip != localIp || gw != gateway)
        {
            pubGen++; pubKnown = false; pubIp = "";
            pubNext = DateTime.UtcNow.AddSeconds(1.5);
        }
        linkUp = up; vpnUp = vpn; vpnRoutes = routes; linkType = type; localIp = ip; gateway = gw; vpnName = vname;
    }

    void FetchPublicIp()
    {
        if (pubBusy) return;
        pubBusy = true;
        pubNext = DateTime.UtcNow.AddMinutes(10);
        int gen = pubGen;
        ThreadPool.QueueUserWorkItem(delegate
        {
            string r = null;
            foreach (string u in PubUrls) { r = HttpIp(u); if (r != null) break; }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    pubBusy = false;
                    if (gen != pubGen) { pubNext = DateTime.MinValue; return; }   // arada ağ değişti: yeniden sor
                    pubKnown = true;
                    pubIp = r ?? "";
                    if (r == null) pubNext = DateTime.UtcNow.AddSeconds(30);
                    Redraw();
                }));
            }
            catch { pubBusy = false; }
        });
    }

    static string HttpIp(string url)
    {
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            HttpWebRequest q = (HttpWebRequest)WebRequest.Create(url);
            q.Timeout = 5000; q.ReadWriteTimeout = 5000;
            q.UserAgent = "WinWinget";
            using (WebResponse resp = q.GetResponse())
            using (System.IO.StreamReader rd = new System.IO.StreamReader(resp.GetResponseStream()))
            {
                string t = rd.ReadToEnd().Trim();
                IPAddress a;
                if (t.Length <= 45 && IPAddress.TryParse(t, out a) && a.AddressFamily == AddressFamily.InterNetwork) return a.ToString();
            }
        }
        catch { }
        return null;
    }

    string PingHost()
    {
        if (ns.Target == 1) return "8.8.8.8";
        if (ns.Target == 2 && gateway.Length > 0) return gateway;
        return "1.1.1.1";
    }

    void StartPing()
    {
        if (pinging) return;
        pinging = true;
        lastPing = DateTime.UtcNow;
        Ping p = new Ping();
        p.PingCompleted += delegate(object o, PingCompletedEventArgs e)
        {
            try
            {
                bool ok = !e.Cancelled && e.Error == null && e.Reply != null && e.Reply.Status == IPStatus.Success;
                pingOk = ok;
                pingMs = ok ? (int)e.Reply.RoundtripTime : -1;
                pingKnown = true;
            }
            finally
            {
                pinging = false;
                ((Ping)o).Dispose();
                try { BeginInvoke(new Action(Redraw)); } catch { }
            }
        };
        try { p.SendAsync(PingHost(), 1500, null); }
        catch { pinging = false; pingOk = false; pingKnown = true; p.Dispose(); }
    }

    protected override void OnTick()
    {
        RefreshLinks();
        if (ns.ShowSpeed)
        {
            long rx, tx;
            NetInfo.Sample(out rx, out tx);
            DateTime now = DateTime.UtcNow;
            double dt = (now - pTime).TotalSeconds;
            if (dt > 0.2) { down = Math.Max(0, (rx - pRx) / dt); up = Math.Max(0, (tx - pTx) / dt); }
            pRx = rx; pTx = tx; pTime = now;
        }
        if (ns.ShowPing && linkUp && (DateTime.UtcNow - lastPing).TotalSeconds >= ns.PingSeconds) StartPing();
        if (ns.ShowPublicIp && linkUp && DateTime.UtcNow >= pubNext) FetchPublicIp();
    }

    int lastKey = -1;
    bool lastPub;

    // Ping ayarı (aç/kapa, hedef, sıklık) değişince hemen yeniden ölç; ilgisiz değişimlerde dokunma.
    protected override void OnSettingsApplied()
    {
        if (ns.ShowPublicIp != lastPub)
        {
            lastPub = ns.ShowPublicIp;
            if (lastPub && linkUp) FetchPublicIp();
        }
        int key = (ns.ShowPing ? 1 : 0) | (ns.Target << 1) | (ns.PingEvery << 3);
        if (key == lastKey) return;
        lastKey = key;
        lastPing = DateTime.MinValue;
        if (ns.ShowPing && linkUp) StartPing();
    }

    // ---- yerleşim ----
    const float RowH = 34f;

    protected override void MeasureCard()
    {
        rows.Clear();
        rows.Add("vpn");
        if (ns.ShowIp) rows.Add("ip");
        if (ns.ShowPublicIp) rows.Add("pub");
        if (ns.ShowPing) rows.Add("ping");
        if (ns.ShowSpeed) rows.Add("speed");

        padX = 20 * Sc; padY = 16 * Sc;
        BH = (int)Math.Ceiling(padY + 30 * Sc + rows.Count * RowH * Sc + padY - 4 * Sc);
    }

    // ---- çizim ----
    string State(out Color c)
    {
        if (!linkUp) { c = C.Danger; return "Çevrimdışı"; }
        if (ns.ShowPing && pingKnown && !pingOk) { c = C.Warn; return "İnternet yok"; }
        c = C.Good;
        return "Çevrimiçi";
    }

    // Pill: yarı saydam durum zemini + kenarlık + metin (DESIGN.md §9 Pill/Badge). Sağa hizalı.
    void Pill(Graphics g, string text, Color col, float xRight, float cy, bool filled)
    {
        Font f = Fnt(false, 11.5f, true);
        float w = Wid(text, f) + 18 * Sc, h = 20 * Sc;
        RectangleF r = new RectangleF(xRight - w, cy - h / 2, w, h);
        Ui.FillRound(g, Color.FromArgb(filled ? 34 : 22, col), r, h / 2);
        Ui.StrokeRound(g, Color.FromArgb(filled ? 71 : 45, col), 1f, r, h / 2);
        Txt(g, text, f, col, r.X + 9 * Sc, cy - Cap(f) / 2f);
    }

    static string Trunc(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

    protected override void PaintContent(Graphics g)
    {
        Font fLab = Fnt(false, 11.5f, true), fMono = Fnt(true, 13, true), fHead = Fnt(false, 15, true);
        rEye = rEyePub = Rectangle.Empty;   // IP satırları çizilirse yeniden belirlenir
        float right = BW - padX;

        // Üst satır: durum noktası + durum metni + bağlantı türü
        Color sc;
        string st = State(out sc);
        float cy = padY + 11 * Sc;
        float d = 9 * Sc;
        using (Brush glow = new SolidBrush(Color.FromArgb(60, sc))) g.FillEllipse(glow, padX - 3 * Sc, cy - d / 2 - 3 * Sc, d + 6 * Sc, d + 6 * Sc);
        using (Brush dot = new SolidBrush(sc)) g.FillEllipse(dot, padX, cy - d / 2, d, d);
        Txt(g, st, fHead, C.Txt, padX + d + 10 * Sc, cy - Cap(fHead) / 2f);
        if (linkUp && linkType.Length > 0) TxtR(g, linkType, fLab, C.Mut, right, cy - Cap(fLab) / 2f);

        float y0 = padY + 30 * Sc;
        for (int i = 0; i < rows.Count; i++)
        {
            float top = y0 + i * RowH * Sc;
            float mid = top + RowH * Sc / 2f - 2 * Sc;
            using (Pen sep = new Pen(C.Line, 1f)) g.DrawLine(sep, padX, top, BW - padX, top);

            switch (rows[i])
            {
                case "vpn":
                    Txt(g, "VPN", fLab, C.Mut, padX, mid - Cap(fLab) / 2f);
                    if (vpnUp) Pill(g, "Bağlı" + (vpnName.Length > 0 ? " · " + Trunc(vpnName, 14) : ""), C.Good, right, mid, true);
                    else Pill(g, "Kapalı", C.Mut, right, mid, false);
                    break;
                case "ip":
                case "pub":
                {
                    bool pub = rows[i] == "pub";
                    Txt(g, pub ? (vpnRoutes ? "VPN IP" : "DIŞ IP") : "YEREL IP", fLab, pub && vpnRoutes ? C.Good : C.Mut, padX, mid - Cap(fLab) / 2f);
                    // Her IP satırının kendi göz düğmesi var.
                    float eb = 22 * Sc;
                    Rectangle er = new Rectangle((int)(right - eb), (int)(mid - eb / 2f), (int)eb, (int)eb);
                    bool hidden = pub ? ns.PubHidden : ns.IpHidden;
                    if (pub) rEyePub = er; else rEye = er;
                    DrawEye(g, er, hidden, eyeHot == (pub ? 1 : 0));
                    float vr = er.X - 6 * Sc;
                    string val = pub ? pubIp : localIp;
                    bool has = linkUp && val.Length > 0;
                    string ipText = has ? (hidden ? "•••.•••.•••.•••" : val) : (pub && linkUp && !pubKnown ? "…" : "—");
                    TxtR(g, ipText, fMono, has ? C.Txt : C.Mut, vr, mid - Cap(fMono) / 2f);
                    break;
                }
                case "ping":
                    Txt(g, "GECİKME", fLab, C.Mut, padX, mid - Cap(fLab) / 2f);
                    string pv; Color pc;
                    if (!linkUp || !pingKnown) { pv = "—"; pc = C.Mut; }
                    else if (!pingOk) { pv = "zaman aşımı"; pc = C.Danger; }
                    else { pv = pingMs + " ms"; pc = pingMs < 80 ? C.Good : (pingMs < 200 ? C.Warn : C.Danger); }
                    TxtR(g, pv, fMono, pc, right, mid - Cap(fMono) / 2f);
                    break;
                case "speed":
                    float vy = mid - Cap(fMono) / 2f;
                    Txt(g, "↓", fMono, C.Acc, padX, vy);
                    Txt(g, NetInfo.Rate(down), fMono, C.Txt, padX + 16 * Sc, vy);
                    string us = NetInfo.Rate(up);
                    float uw = Wid(us, fMono);
                    TxtR(g, us, fMono, C.Txt, right, vy);
                    TxtR(g, "↑", fMono, C.Mut, right - uw - 6 * Sc, vy);
                    break;
            }
        }
    }

    protected override int NextDelayMs() { return ns.ShowSpeed ? 2000 : 5000; }
}

class NetPage : WidgetPage
{
    readonly NetSettings ns;
    Toggle tgSpeed, tgPing, tgIp, tgPub, tgIpHide, tgPubHide;
    Segmented segTarget, segEvery;

    public NetPage(NetSettings s) { ns = s; }

    public override void Build(SettingsForm f, Panel p)
    {
        int y = Ui.S(4);
        tgIp = f.AddToggle(p, ref y, "Yerel IP", null, delegate(bool b) { ns.ShowIp = b; });
        tgPub = f.AddToggle(p, ref y, "Dış IP", "VPN bağlıyken VPN IP'si. ipify.org'dan alınır.", delegate(bool b) { ns.ShowPublicIp = b; });
        tgIpHide = f.AddToggle(p, ref y, "Yerel IP'yi gizle", null, delegate(bool b) { ns.IpHidden = b; });
        tgPubHide = f.AddToggle(p, ref y, "Dış IP'yi gizle", null, delegate(bool b) { ns.PubHidden = b; });
        tgPing = f.AddToggle(p, ref y, "Gecikme (ping)", null, delegate(bool b) { ns.ShowPing = b; });
        tgSpeed = f.AddToggle(p, ref y, "İndirme / yükleme hızı", null, delegate(bool b) { ns.ShowSpeed = b; });
        segTarget = f.AddSeg(p, ref y, "Ping hedefi", null, new string[] { "Cloudflare", "Google", "Ağ geçidi" }, delegate(int i) { ns.Target = i; });
        segEvery = f.AddSeg(p, ref y, "Ping sıklığı", null, new string[] { "5 sn", "10 sn", "30 sn" }, delegate(int i) { ns.PingEvery = i; });
    }

    public override void Sync()
    {
        tgIp.Checked = ns.ShowIp;
        tgIpHide.Checked = ns.IpHidden;
        tgPub.Checked = ns.ShowPublicIp;
        tgIpHide.Enabled = ns.ShowIp;
        tgPubHide.Checked = ns.PubHidden;
        tgPubHide.Enabled = ns.ShowPublicIp;
        tgPing.Checked = ns.ShowPing;
        tgSpeed.Checked = ns.ShowSpeed;
        segTarget.Selected = ns.Target;
        segEvery.Selected = ns.PingEvery;
        segTarget.Enabled = segEvery.Enabled = ns.ShowPing;
    }
}
