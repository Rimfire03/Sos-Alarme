using System.Windows.Forms;

namespace SosLan.Models;

public class AppSettings
{
    public string DisplayName { get; set; } = Environment.MachineName;

    public Keys HotKey { get; set; } = Keys.F8;

    public int HoldDurationSeconds { get; set; } = 5;

    public int Port { get; set; } = 51515;
}
