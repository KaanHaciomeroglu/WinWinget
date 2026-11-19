using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

// Sistem monitörü ve ağ widget'ı için ortak ağ yardımcıları.
static class NetInfo
{
    static readonly string[] Ignore = { "teredo", "isatap", "6to4", "vethernet", "hyper-v", "vmware", "virtualbox", "loopback",
                                        "pseudo", "bluetooth", "wi-fi direct", "wfp", "npcap", "qos packet" };
    static readonly string[] VpnWords = { "vpn", "wireguard", "openvpn", "wintun", "tap-windows", "tap-win", "nordlynx", "tailscale",
                                          "zerotier", "anyconnect", "fortinet", "forticlient", "proton", "mullvad", "surfshark",
                                          "pritunl", "softether", "warp", "cisco", "globalprotect", "pangp" };

    static bool Has(string s, string[] words)
    {
        foreach (string w in words) if (s.Contains(w)) return true;
        return false;
    }

    // 0 = yok say, 1 = fiziksel (Wi-Fi/Ethernet), 2 = VPN
    public static int Kind(NetworkInterface ni)
    {
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) return 0;
        string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
        if (Has(d, Ignore)) return 0;
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Ppp || Has(d, VpnWords)) return 2;
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return 0;
        return 1;
    }

    [DllImport("iphlpapi.dll")]
    static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

    // İnternet trafiğinin (1.1.1.1'e giden yolun) çıktığı arayüzün IPv4 dizini; yol yoksa -1.
    // VPN'i "bağdaştırıcı açık" diye değil, trafiği gerçekten taşıyor diye saymak için
    // (Radmin/Hamachi/ZeroTier gibi sanal LAN'lar hep açıktır ama varsayılan yol değildir).
    public static int BestIfIndex()
    {
        try
        {
            uint idx;
            uint dest = BitConverter.ToUInt32(new byte[] { 1, 1, 1, 1 }, 0);
            return GetBestInterface(dest, out idx) == 0 ? (int)idx : -1;
        }
        catch { return -1; }
    }

    public static int IfIndex(NetworkInterface ni)
    {
        try { return ni.GetIPProperties().GetIPv4Properties().Index; }
        catch { return -2; }
    }

    // Radmin VPN kapatılınca bağdaştırıcısı "Up" kalır; gerçek durum güç düğmesinin kayıt değeridir
    // (HKLM\SOFTWARE\WOW6432Node\Famatech\RadminVPN\1.0\PowerOn, yönetici gerekmez). Değer yoksa null.
    public static bool? RadminOn()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey b = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32))
            using (Microsoft.Win32.RegistryKey k = b.OpenSubKey(@"SOFTWARE\Famatech\RadminVPN\1.0"))
            {
                object v = k == null ? null : k.GetValue("PowerOn");
                if (v is int) return (int)v != 0;
            }
        }
        catch { }
        return null;
    }

    // Fiziksel arayüzlerin toplam bayt sayaçları (VPN çift sayılmasın diye hariç).
    public static void Sample(out long rx, out long tx)
    {
        rx = 0; tx = 0;
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || Kind(ni) != 1) continue;
                IPInterfaceStatistics st = ni.GetIPStatistics();
                rx += st.BytesReceived; tx += st.BytesSent;
            }
        }
        catch { }
    }

    public static string Rate(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return Math.Round(bytesPerSec) + " B/s";
        double kb = bytesPerSec / 1024;
        if (kb < 1000) return (kb < 100 ? kb.ToString("0.0") : kb.ToString("0")) + " KB/s";
        double mb = kb / 1024;
        return (mb < 100 ? mb.ToString("0.0") : mb.ToString("0")) + " MB/s";
    }
}
