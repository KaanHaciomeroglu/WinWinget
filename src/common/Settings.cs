using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Win32;

// Accent presetleri (Design/DESIGN.md §2.2): koyu = 300-400 tonu, açık = 600-700 tonu.
static class Palette
{
    public static readonly string[] Names = { "Mor", "Neon Mavi", "Zümrüt", "Amber", "Gül", "Kızıl", "Menekşe", "Kobalt", "Titanyum" };
    static readonly string[] Dark = { "B39DDB", "38BDF8", "34D399", "FBBF24", "F472B6", "F87171", "C084FC", "60A5FA", "94A3B8" };
    static readonly string[] Light = { "7C3AED", "0284C7", "059669", "D97706", "DB2777", "DC2626", "9333EA", "2563EB", "475569" };

    public static int Count { get { return Dark.Length; } }

    public static Color Get(int i, bool light)
    {
        return Hex((light ? Light : Dark)[i]);
    }

    public static Color Hex(string h)
    {
        return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16),
                              Convert.ToInt32(h.Substring(2, 2), 16),
                              Convert.ToInt32(h.Substring(4, 2), 16));
    }
}

// Tüm widget'ların ortak görünümü (tema, accent, ölçek, opaklık, üstte) → kartlar hep aynı genişlikte/stilde.
class AppSettings
{
    public static readonly AppSettings I = new AppSettings();
    public const string Folder = "WinWinget";

    public int Theme;            // 0 koyu, 1 açık
    public int Accent = 7;       // Palette indeksi (Kobalt)
    public int Scale = 100;      // %
    public int Opacity = 88;     // % (yalnızca arka plan)
    public bool TopMost;
    public bool MoveTogether;    // bir widget sürüklenince hepsi birlikte hareket eder

    // "Ev monitörü": widget'ların son bulunduğu ekrandan bir nokta. Dizilim/sıfırlama bu ekranda yapılır.
    public bool HasHome;
    public int HomeX, HomeY;
    public void SetHome(int x, int y) { HasHome = true; HomeX = x; HomeY = y; }

    public bool Light { get { return Theme == 1; } }
    public Color CardAccent { get { return Palette.Get(Accent, Light); } }
    public Color UiAccent { get { return Palette.Get(Accent, false); } }

    public static string Dir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Folder); }
    }

    AppSettings() { Load(); }

    static int Cl(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }

    void Load()
    {
        // Eski tek-saat sürümünün ayarları varsa görünümü oradan devral (bir kerelik).
        bool hhx = false, hhy = false;
        string path = Path.Combine(Dir, "app.txt");
        if (!File.Exists(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopClock", "settings.txt");
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                int n;
                bool isInt = int.TryParse(v, out n);
                switch (k)
                {
                    case "topmost": TopMost = v == "1"; break;
                    case "together": MoveTogether = v == "1"; break;
                    case "theme": if (isInt) Theme = n; break;
                    case "accent": if (isInt) Accent = n; break;
                    case "scale": if (isInt) Scale = n; break;
                    case "opacity": if (isInt) Opacity = n; break;
                    case "homex": if (isInt) { HomeX = n; hhx = true; } break;
                    case "homey": if (isInt) { HomeY = n; hhy = true; } break;
                }
            }
        }
        catch { }
        HasHome = hhx && hhy;
        Theme = Cl(Theme, 0, 1);
        Accent = Cl(Accent, 0, Palette.Count - 1);
        Scale = Cl(Scale, 60, 200);
        Opacity = Cl(Opacity, 20, 100);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            List<string> l = new List<string>();
            l.Add("theme=" + Theme); l.Add("accent=" + Accent); l.Add("scale=" + Scale); l.Add("opacity=" + Opacity);
            l.Add("topmost=" + (TopMost ? "1" : "0"));
            l.Add("together=" + (MoveTogether ? "1" : "0"));
            if (HasHome) { l.Add("homex=" + HomeX); l.Add("homey=" + HomeY); }
            File.WriteAllLines(Path.Combine(Dir, "app.txt"), l.ToArray());
        }
        catch { }
    }
}

// Bir widget'ın kendi ayarları (göster/gizle, sabitle, konum + widget'a özel alanlar).
// Görünüm alanları ortak AppSettings'e yönlenir; böylece alt sınıflar/arayüz değişmeden çalışır.
class Settings
{
    public static readonly List<Settings> All = new List<Settings>();
    public static event EventHandler AnyChanged;

    public string Id;
    public bool Pinned, HasPos, Enabled = true;
    public int X, Y;

    public int Theme { get { return AppSettings.I.Theme; } set { AppSettings.I.Theme = value; } }
    public int Accent { get { return AppSettings.I.Accent; } set { AppSettings.I.Accent = value; } }
    public int Scale { get { return AppSettings.I.Scale; } set { AppSettings.I.Scale = value; } }
    public int Opacity { get { return AppSettings.I.Opacity; } set { AppSettings.I.Opacity = value; } }
    public bool TopMost { get { return AppSettings.I.TopMost; } set { AppSettings.I.TopMost = value; } }

    public bool Light { get { return AppSettings.I.Light; } }
    public Color CardAccent { get { return AppSettings.I.CardAccent; } }
    public Color UiAccent { get { return AppSettings.I.UiAccent; } }

    string FilePath { get { return Path.Combine(AppSettings.Dir, Id + ".txt"); } }

    // Herhangi bir ayar değişince tüm widget'lar ve arayüz yenilenir.
    public void Raise() { RaiseAll(); }
    public static void RaiseAll() { if (AnyChanged != null) AnyChanged(null, EventArgs.Empty); }

    public static void SaveAll()
    {
        foreach (Settings s in All) s.SaveOwn();
        AppSettings.I.Save();
    }

    protected static int Cl(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }

    protected virtual void Parse(string k, string v, int n, bool isInt) { }
    protected virtual void WriteExtra(List<string> l) { }
    protected virtual void Normalize() { }

    // legacyDir: eski tek-widget sürümünün ayar klasörü (yoksa null). Konum devralınmaz (genişlik değişti).
    public static T Load<T>(string id, string legacyDir) where T : Settings, new()
    {
        T s = new T();
        s.Id = id;
        string path = s.FilePath;
        bool legacy = false;
        if (!File.Exists(path) && legacyDir != null)
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), legacyDir, "settings.txt");
            legacy = true;
        }
        bool hx = false, hy = false;
        int lx = 0, ly = 0;
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                int n;
                bool isInt = int.TryParse(v, out n);
                switch (k)
                {
                    case "pinned": s.Pinned = v == "1"; break;
                    case "enabled": s.Enabled = v == "1"; break;
                    case "x": if (isInt) { if (legacy) lx = n; else s.X = n; hx = true; } break;
                    case "y": if (isInt) { if (legacy) ly = n; else s.Y = n; hy = true; } break;
                    default: s.Parse(k, v, n, isInt); break;
                }
            }
        }
        catch { }
        // Eski sürümün konumu genişlik değiştiği için devralınmaz; yalnızca hangi monitörde olduğu ev monitörü olur.
        if (legacy && hx && hy && !AppSettings.I.HasHome) AppSettings.I.SetHome(lx + 1, ly + 1);
        s.HasPos = !legacy && hx && hy;
        s.Normalize();
        All.Add(s);
        return s;
    }

    public void Save()
    {
        SaveOwn();
        AppSettings.I.Save();
    }

    void SaveOwn()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            List<string> l = new List<string>();
            l.Add("enabled=" + Bit(Enabled));
            l.Add("pinned=" + Bit(Pinned));
            if (HasPos) { l.Add("x=" + X); l.Add("y=" + Y); }
            WriteExtra(l);
            File.WriteAllLines(FilePath, l.ToArray());
        }
        catch { }
    }

    protected static string Bit(bool b) { return b ? "1" : "0"; }
}

static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Name = "WinWinget";

    public static bool IsOn()
    {
        try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Name) != null; }
        catch { return false; }
    }

    public static void Set(bool on)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (on) k.SetValue(Name, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                else k.DeleteValue(Name, false);
            }
        }
        catch { }
    }
}
