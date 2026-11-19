using System;
using System.Collections.Generic;
using System.Windows.Forms;

// WinWinget: tüm widget'lar tek süreçte, tek tepsi simgesi ve tek ayar penceresiyle.
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        WidgetApp.Run("WinWinget", args, delegate
        {
            // Eski tek-saat sürümünün ayarları ("DesktopClock") varsa saat onlardan devralır.
            ClockSettings cs = Settings.Load<ClockSettings>("clock", "DesktopClock");
            SysSettings ss = Settings.Load<SysSettings>("sysmon", null);
            NetSettings ns = Settings.Load<NetSettings>("netstatus", null);
            PomoSettings ps = Settings.Load<PomoSettings>("pomodoro", null);
            PomodoroForm pomo = new PomodoroForm(ps);

            // Sıra hem sekme hem varsayılan dizilim sırasıdır.
            List<WidgetDef> defs = new List<WidgetDef>();
            defs.Add(new WidgetDef("clock", "Saat", cs, new ClockForm(cs), new ClockPage(cs)));
            defs.Add(new WidgetDef("sysmon", "Sistem", ss, new SysMonForm(ss), new SysPage(ss)));
            defs.Add(new WidgetDef("netstatus", "Ağ", ns, new NetStatusForm(ns), new NetPage(ns)));
            defs.Add(new WidgetDef("pomodoro", "Pomodoro", ps, pomo, new PomoPage(ps)));

            return new HubApp(defs);
        });
    }
}
