namespace SosLan.Models;

public class AppSettings
{
    public string DisplayName { get; set; } = Environment.MachineName;

    public AppKey HotKey { get; set; } = AppKey.F8;

    public int HoldDurationSeconds { get; set; } = 5;

    public int Port { get; set; } = 51515;

    public int MaxAlertDurationSeconds { get; set; } = 60;
}
