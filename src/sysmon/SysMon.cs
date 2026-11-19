using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class SysSettings : Settings
{
    public bool ShowCpu = true, ShowRam = true, ShowGpu = true;
    public int Interval = 1;          // 0 = 1 sn, 1 = 2 sn, 2 = 5 sn
    public List<string> Drives;       // gösterilecek sürücüler ("C:", "D:"…); null = yalnızca sistem sürücüsü

    public static string SystemDrive
    {
        get { return Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\'); }
    }

    public List<string> ActiveDrives()
    {
        if (Drives == null) { List<string> d = new List<string>(); d.Add(SystemDrive); return d; }
        return Drives;
    }

    public bool DriveOn(string d) { return ActiveDrives().Contains(d); }

    public void SetDrive(string d, bool on)
    {
        List<string> l = new List<string>(ActiveDrives());
        if (on && !l.Contains(d)) l.Add(d);
        if (!on) l.Remove(d);
        Drives = l;
    }

    protected override void Parse(string k, string v, int n, bool isInt)
    {
        switch (k)
        {
            case "cpu": ShowCpu = v == "1"; break;
            case "ram": ShowRam = v == "1"; break;
            case "gpu": ShowGpu = v == "1"; break;
            case "interval": if (isInt) Interval = n; break;
            case "disk": if (v == "0" && Drives == null) Drives = new List<string>(); break;   // eski sürümden
            case "drives": Drives = new List<string>(v.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)); break;
        }
    }

    protected override void WriteExtra(List<string> l)
    {
        l.Add("cpu=" + Bit(ShowCpu));
        l.Add("ram=" + Bit(ShowRam));
        l.Add("gpu=" + Bit(ShowGpu));
        l.Add("interval=" + Interval);
        if (Drives != null) l.Add("drives=" + string.Join(";", Drives.ToArray()));
    }

    protected override void Normalize() { Interval = Cl(Interval, 0, 2); }

    public int IntervalMs { get { return Interval == 0 ? 1000 : (Interval == 1 ? 2000 : 5000); } }
}

class DiskInfo
{
    public string Name, VolLabel;
    public double UsedGb, TotalGb, Pct;
}

// GPU kullanımı: Windows "GPU Engine" sayaçları, PDH (pdh.dll) ile; PerformanceCounter kullanılmaz.
// Görev Yöneticisi gibi: aynı adaptör+motor türündeki süreçler toplanır, en yüksek tür gösterilir.
// Yeni süreç örnekleri için sayaç 15 sn'de bir yeniden kurulur. Sayaç yoksa (eski Windows) Read() -1 döner.
class GpuReader
{
    [StructLayout(LayoutKind.Sequential)]
    struct ITEM
    {
        public IntPtr Name;
        public uint CStatus;
        public double Value;
    }

    [DllImport("pdh.dll")] static extern uint PdhOpenQuery(IntPtr src, IntPtr user, out IntPtr q);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(IntPtr q, string path, IntPtr user, out IntPtr c);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr q);
    [DllImport("pdh.dll")] static extern uint PdhGetFormattedCounterArrayW(IntPtr c, uint fmt, ref uint size, ref uint count, IntPtr buf);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr q);

    const uint FmtDouble = 0x200, MoreData = 0x800007D2;

    // Sıcaklık: NVIDIA sürücüsüyle gelen nvml.dll (yönetici hakkı gerekmez). Yoksa -1.
    [DllImport("nvml.dll")] static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetCount_v2(out uint n);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint i, out IntPtr h);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr h, int sensor, out uint t);

    bool nvmlTried, nvmlOk;
    readonly List<IntPtr> nvmlDevs = new List<IntPtr>();

    // En sıcak NVIDIA GPU'nun °C değeri; okunamıyorsa -1.
    public double ReadTemp()
    {
        if (!nvmlTried)
        {
            nvmlTried = true;
            try
            {
                uint n;
                if (nvmlInit_v2() == 0 && nvmlDeviceGetCount_v2(out n) == 0)
                {
                    for (uint i = 0; i < n; i++)
                    {
                        IntPtr h;
                        if (nvmlDeviceGetHandleByIndex_v2(i, out h) == 0) nvmlDevs.Add(h);
                    }
                    nvmlOk = nvmlDevs.Count > 0;
                }
            }
            catch { }
        }
        if (!nvmlOk) return -1;
        double max = -1;
        try
        {
            foreach (IntPtr h in nvmlDevs)
            {
                uint t;
                if (nvmlDeviceGetTemperature(h, 0, out t) == 0 && t > max) max = t;
            }
        }
        catch { }
        return max;
    }

    IntPtr q, c;
    DateTime built = DateTime.MinValue;
    bool failed;
    double last;

    void Close()
    {
        if (q != IntPtr.Zero) PdhCloseQuery(q);
        q = c = IntPtr.Zero;
    }

    public double Read()
    {
        if (failed) return -1;
        if (q == IntPtr.Zero || (DateTime.UtcNow - built).TotalSeconds >= 15)
        {
            Close();
            built = DateTime.UtcNow;
            try
            {
                if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out q) != 0
                    || PdhAddEnglishCounterW(q, "\\GPU Engine(*)\\Utilization Percentage", IntPtr.Zero, out c) != 0)
                { Close(); failed = true; return -1; }
                PdhCollectQueryData(q);   // hız sayacı: ilk örnek değer vermez
            }
            catch { Close(); failed = true; return -1; }
            return last;
        }

        try
        {
            if (PdhCollectQueryData(q) != 0) return last;
            uint size = 0, count = 0;
            if (PdhGetFormattedCounterArrayW(c, FmtDouble, ref size, ref count, IntPtr.Zero) != MoreData || size == 0) return last;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(c, FmtDouble, ref size, ref count, buf) != 0) return last;
                Dictionary<string, double> sum = new Dictionary<string, double>();
                int step = Marshal.SizeOf(typeof(ITEM));
                for (int i = 0; i < count; i++)
                {
                    ITEM it = (ITEM)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + (long)i * step), typeof(ITEM));
                    if (it.CStatus != 0 && it.CStatus != 1) continue;
                    string n = Marshal.PtrToStringUni(it.Name) ?? "";
                    int a = n.IndexOf("luid_"), b = n.IndexOf("_engtype_");
                    if (a < 0 || b < 0) continue;
                    string key = n.Substring(a, n.IndexOf("_phys", a) - a) + n.Substring(b);
                    double v;
                    sum.TryGetValue(key, out v);
                    sum[key] = v + it.Value;
                }
                double max = 0;
                foreach (double v in sum.Values) if (v > max) max = v;
                last = Math.Max(0, Math.Min(100, max));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
        return last;
    }
}

// CPU / RAM / disk okumaları. Hafif Win32 çağrıları; PerformanceCounter kullanılmaz.
class SysStats
{
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

    public double Cpu, Gpu = -1, GpuTemp = -1, RamPct, RamUsedGb, RamTotalGb;   // Gpu < 0: okunamıyor
    readonly GpuReader gpuReader = new GpuReader();
    public readonly List<DiskInfo> Disks = new List<DiskInfo>();

    long pIdle, pKernel, pUser;
    string diskKey = "";
    DateTime diskAt = DateTime.MinValue;

    public SysStats() { GetSystemTimes(out pIdle, out pKernel, out pUser); }

    public static List<DriveInfo> FindDrives()
    {
        List<DriveInfo> l = new List<DriveInfo>();
        foreach (DriveInfo d in DriveInfo.GetDrives())
        {
            try { if ((d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.IsReady) l.Add(d); }
            catch { }
        }
        return l;
    }

    public void Sample(bool cpu, bool gpu, bool ram, List<string> drives)
    {
        if (gpu) { Gpu = gpuReader.Read(); GpuTemp = gpuReader.ReadTemp(); }
        if (cpu)
        {
            long idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                long total = (kernel - pKernel) + (user - pUser), busy = total - (idle - pIdle);
                if (total > 0) Cpu = Math.Max(0, Math.Min(100, busy * 100.0 / total));
                pIdle = idle; pKernel = kernel; pUser = user;
            }
        }
        if (ram)
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(m))
            {
                RamPct = m.dwMemoryLoad;
                RamTotalGb = m.ullTotalPhys / 1073741824.0;
                RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            }
        }
        // Disk doluluğu yavaş değişir: 30 sn'de bir (ya da sürücü seçimi değişince) okunur.
        string key = string.Join(";", drives.ToArray());
        if (key != diskKey || (DateTime.UtcNow - diskAt).TotalSeconds >= 30)
        {
            diskKey = key; diskAt = DateTime.UtcNow;
            Disks.Clear();
            List<string> sorted = new List<string>(drives);
            sorted.Sort();
            foreach (string name in sorted)
            {
                try
                {
                    DriveInfo d = new DriveInfo(name);
                    if (!d.IsReady) continue;
                    DiskInfo di = new DiskInfo();
                    di.Name = name;
                    try { di.VolLabel = d.VolumeLabel; } catch { }
                    di.TotalGb = d.TotalSize / 1073741824.0;
                    di.UsedGb = (d.TotalSize - d.AvailableFreeSpace) / 1073741824.0;
                    di.Pct = di.TotalGb > 0 ? di.UsedGb * 100.0 / di.TotalGb : 0;
                    Disks.Add(di);
                }
                catch { }   // çıkarılmış / erişilemeyen sürücü: atla
            }
        }
    }
}

class SysMonForm : WidgetForm
{
    readonly SysSettings ss;
    readonly SysStats st = new SysStats();

    struct Item { public string Label, Detail, Value; public double Pct; }
    readonly List<Item> items = new List<Item>();
    float padX, padY, stride;
    string lastKey;

    public SysMonForm(SysSettings s) : base(s)
    {
        ss = s;
        Sample();
        lastKey = Key();
    }

    string Key()
    {
        return ss.ShowCpu + "|" + ss.ShowGpu + "|" + ss.ShowRam + "|" + string.Join(";", ss.ActiveDrives().ToArray());
    }

    void Sample() { st.Sample(ss.ShowCpu, ss.ShowGpu, ss.ShowRam, ss.ActiveDrives()); }

    protected override void OnTick() { Sample(); }

    // Yalnızca gösterilen metrikler/sürücüler değişince yeniden örnekle (ilgisiz ayar değişimlerinde CPU ölçümü bozulmasın).
    protected override void OnSettingsApplied()
    {
        string key = Key();
        if (key == lastKey) return;
        lastKey = key;
        Sample();
    }

    protected override void MeasureCard()
    {
        items.Clear();
        Item it;
        if (ss.ShowCpu)
        {
            it = new Item(); it.Label = "CPU"; it.Pct = st.Cpu; it.Value = Math.Round(st.Cpu) + "%"; items.Add(it);
        }
        if (ss.ShowGpu && st.Gpu >= 0)
        {
            it = new Item(); it.Label = "GPU"; it.Pct = st.Gpu; it.Value = Math.Round(st.Gpu) + "%";
            if (st.GpuTemp >= 0) it.Detail = Math.Round(st.GpuTemp) + "°C";
            items.Add(it);
        }
        if (ss.ShowRam)
        {
            it = new Item(); it.Label = "RAM"; it.Pct = st.RamPct; it.Value = Math.Round(st.RamPct) + "%";
            it.Detail = st.RamUsedGb.ToString("0.0") + " / " + st.RamTotalGb.ToString("0.0") + " GB"; items.Add(it);
        }
        foreach (DiskInfo d in st.Disks)
        {
            it = new Item(); it.Label = (string.IsNullOrEmpty(d.VolLabel) ? "DİSK " + d.Name : d.Name + " " + (d.VolLabel.Length > 10 ? d.VolLabel.Substring(0, 10) : d.VolLabel)); it.Pct = d.Pct; it.Value = Math.Round(d.Pct) + "%";
            it.Detail = Math.Round(d.UsedGb) + " / " + Math.Round(d.TotalGb) + " GB"; items.Add(it);
        }

        padX = 20 * Sc; padY = 18 * Sc; stride = 42 * Sc;
        float content = items.Count > 0 ? items.Count * stride - (stride - 26 * Sc) : 14 * Sc;
        BH = (int)Math.Ceiling(padY * 2 + content);
    }

    protected override void PaintContent(Graphics g)
    {
        Font fLab = Fnt(false, 11.5f, true), fVal = Fnt(true, 13.5f, true), fDet = Fnt(false, 12, false);
        float right = BW - padX;

        if (items.Count == 0)
        {
            Txt(g, "Gösterilecek metrik yok", fDet, C.Mut, padX, padY);
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            Item it = items[i];
            float y = padY + i * stride;
            float lw = Wid(it.Label, fLab);
            Txt(g, it.Label, fLab, C.Mut, padX, y);
            if (it.Detail != null) Txt(g, it.Detail, fDet, C.Mut, padX + lw + 10 * Sc, y - 0.5f * Sc);
            TxtR(g, it.Value, fVal, C.Txt, right, y - 2 * Sc);

            // Gösterge çubuğu (DESIGN.md §9): 6px, r-full, durum rengine göre dolgu
            float by = y + 19 * Sc, bh = 6 * Sc, bw = BW - 2 * padX;
            Ui.FillRound(g, C.Track, new RectangleF(padX, by, bw, bh), bh / 2);
            float fw = (float)Math.Max(bh, bw * Math.Min(100, it.Pct) / 100.0);
            Ui.FillRound(g, C.Status(it.Pct), new RectangleF(padX, by, fw, bh), bh / 2);
        }
    }

    protected override int NextDelayMs() { return ss.IntervalMs; }
}

class SysPage : WidgetPage
{
    const int MaxDrives = 5;   // sayfa yüksekliğine sığacak kadar

    readonly SysSettings ss;
    Toggle tgCpu, tgGpu, tgRam;
    Segmented segInt;
    readonly List<Toggle> driveToggles = new List<Toggle>();
    readonly List<string> driveNames = new List<string>();

    public SysPage(SysSettings s) { ss = s; }

    public override void Build(SettingsForm f, Panel p)
    {
        int y = Ui.S(4);
        tgCpu = f.AddToggle(p, ref y, "İşlemci (CPU)", null, delegate(bool b) { ss.ShowCpu = b; });
        tgGpu = f.AddToggle(p, ref y, "Ekran kartı (GPU)", null, delegate(bool b) { ss.ShowGpu = b; });
        tgRam = f.AddToggle(p, ref y, "Bellek (RAM)", null, delegate(bool b) { ss.ShowRam = b; });

        // Diskler: bilgisayardaki sürücüler listelenir (ayarlar açılırken); istenenler eklenir.
        Label sec = f.Lab("DİSKLER", 11.5f, Ui.Muted, true, false);
        sec.Location = new Point(f.G, y + Ui.S(10));
        p.Controls.Add(sec);
        y += Ui.S(36);

        int shown = 0;
        foreach (DriveInfo d in SysStats.FindDrives())
        {
            if (shown >= MaxDrives) break;
            string name = d.Name.TrimEnd('\\');
            string label = "";
            try { label = d.VolumeLabel; } catch { }
            string title = name + (label.Length > 0 ? "  " + label : "") + "  ·  " + Math.Round(d.TotalSize / 1073741824.0) + " GB"
                         + (name == SysSettings.SystemDrive ? "  ·  sistem" : "");
            string captured = name;

            // Sıkı satır (36 px): 6 sürücü sayfaya sığsın.
            int rowH = Ui.S(36);
            Label t = f.Lab(title, 13.5f, Ui.Text2, false, false);
            t.Location = new Point(f.G, y + (rowH - t.PreferredHeight) / 2);
            p.Controls.Add(t);
            Toggle tg = new Toggle();
            tg.Location = new Point(f.CW - f.G - tg.Width, y + (rowH - tg.Height) / 2);
            tg.Toggled += delegate { f.Edit(delegate { ss.SetDrive(captured, tg.Checked); }); };
            p.Controls.Add(tg);
            y = f.Divider(p, y + rowH);

            driveToggles.Add(tg);
            driveNames.Add(name);
            shown++;
        }

        segInt = f.AddSeg(p, ref y, "Yenileme sıklığı", null, new string[] { "1 sn", "2 sn", "5 sn" }, delegate(int i) { ss.Interval = i; });
    }

    public override void Sync()
    {
        tgCpu.Checked = ss.ShowCpu;
        tgGpu.Checked = ss.ShowGpu;
        tgRam.Checked = ss.ShowRam;
        for (int i = 0; i < driveToggles.Count; i++) driveToggles[i].Checked = ss.DriveOn(driveNames[i]);
        segInt.Selected = ss.Interval;
    }
}
