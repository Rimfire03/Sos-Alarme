using System.Runtime.InteropServices;
using SosLan.Models;

namespace SosLan.Services.Windows;

/// <summary>
/// État des touches sous Windows via l'API user32 GetAsyncKeyState.
/// </summary>
public class WindowsKeyStateProvider : IKeyStateProvider
{
    private static readonly Dictionary<AppKey, int> VirtualKeyCodes = new()
    {
        [AppKey.A] = 0x41, [AppKey.B] = 0x42, [AppKey.C] = 0x43, [AppKey.D] = 0x44,
        [AppKey.E] = 0x45, [AppKey.F] = 0x46, [AppKey.G] = 0x47, [AppKey.H] = 0x48,
        [AppKey.I] = 0x49, [AppKey.J] = 0x4A, [AppKey.K] = 0x4B, [AppKey.L] = 0x4C,
        [AppKey.M] = 0x4D, [AppKey.N] = 0x4E, [AppKey.O] = 0x4F, [AppKey.P] = 0x50,
        [AppKey.Q] = 0x51, [AppKey.R] = 0x52, [AppKey.S] = 0x53, [AppKey.T] = 0x54,
        [AppKey.U] = 0x55, [AppKey.V] = 0x56, [AppKey.W] = 0x57, [AppKey.X] = 0x58,
        [AppKey.Y] = 0x59, [AppKey.Z] = 0x5A,

        [AppKey.D0] = 0x30, [AppKey.D1] = 0x31, [AppKey.D2] = 0x32, [AppKey.D3] = 0x33,
        [AppKey.D4] = 0x34, [AppKey.D5] = 0x35, [AppKey.D6] = 0x36, [AppKey.D7] = 0x37,
        [AppKey.D8] = 0x38, [AppKey.D9] = 0x39,

        [AppKey.F1] = 0x70, [AppKey.F2] = 0x71, [AppKey.F3] = 0x72, [AppKey.F4] = 0x73,
        [AppKey.F5] = 0x74, [AppKey.F6] = 0x75, [AppKey.F7] = 0x76, [AppKey.F8] = 0x77,
        [AppKey.F9] = 0x78, [AppKey.F10] = 0x79, [AppKey.F11] = 0x7A, [AppKey.F12] = 0x7B,

        [AppKey.Space] = 0x20,
        [AppKey.Tab] = 0x09,
        [AppKey.CapsLock] = 0x14,
        [AppKey.Escape] = 0x1B,
        [AppKey.Backspace] = 0x08,
        [AppKey.Delete] = 0x2E,
        [AppKey.Home] = 0x24,
        [AppKey.End] = 0x23,
        [AppKey.PageUp] = 0x21,
        [AppKey.PageDown] = 0x22,
        [AppKey.Left] = 0x25,
        [AppKey.Up] = 0x26,
        [AppKey.Right] = 0x27,
        [AppKey.Down] = 0x28,
    };

    public bool IsPressed(AppKey key)
    {
        if (!VirtualKeyCodes.TryGetValue(key, out var vk))
        {
            return false;
        }

        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
