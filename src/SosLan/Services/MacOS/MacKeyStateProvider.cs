using System.Runtime.InteropServices;
using SosLan.Models;

namespace SosLan.Services.MacOS;

/// <summary>
/// État des touches sous macOS via CGEventSourceKeyState (ApplicationServices / Core Graphics).
/// Nécessite que l'application ait la permission "Accessibilité" (Réglages Système >
/// Confidentialité et sécurité > Accessibilité) pour détecter les touches en dehors
/// de ses propres fenêtres ; sans cette permission, la méthode renvoie toujours false.
/// </summary>
public class MacKeyStateProvider : IKeyStateProvider
{
    // Codes de touches virtuelles macOS (constantes kVK_* standard, disposition clavier
    // US ANSI ; stables car basés sur la position physique, pas le caractère produit).
    private static readonly Dictionary<AppKey, ushort> VirtualKeyCodes = new()
    {
        [AppKey.A] = 0x00, [AppKey.S] = 0x01, [AppKey.D] = 0x02, [AppKey.F] = 0x03,
        [AppKey.H] = 0x04, [AppKey.G] = 0x05, [AppKey.Z] = 0x06, [AppKey.X] = 0x07,
        [AppKey.C] = 0x08, [AppKey.V] = 0x09, [AppKey.B] = 0x0B, [AppKey.Q] = 0x0C,
        [AppKey.W] = 0x0D, [AppKey.E] = 0x0E, [AppKey.R] = 0x0F, [AppKey.Y] = 0x10,
        [AppKey.T] = 0x11, [AppKey.D1] = 0x12, [AppKey.D2] = 0x13, [AppKey.D3] = 0x14,
        [AppKey.D4] = 0x15, [AppKey.D6] = 0x16, [AppKey.D5] = 0x17, [AppKey.D9] = 0x19,
        [AppKey.D7] = 0x1A, [AppKey.D8] = 0x1C, [AppKey.D0] = 0x1D, [AppKey.O] = 0x1F,
        [AppKey.U] = 0x20, [AppKey.I] = 0x22, [AppKey.P] = 0x23, [AppKey.L] = 0x25,
        [AppKey.J] = 0x26, [AppKey.K] = 0x28, [AppKey.N] = 0x2D, [AppKey.M] = 0x2E,

        [AppKey.F1] = 0x7A, [AppKey.F2] = 0x78, [AppKey.F3] = 0x63, [AppKey.F4] = 0x76,
        [AppKey.F5] = 0x60, [AppKey.F6] = 0x61, [AppKey.F7] = 0x62, [AppKey.F8] = 0x64,
        [AppKey.F9] = 0x65, [AppKey.F10] = 0x6D, [AppKey.F11] = 0x67, [AppKey.F12] = 0x6F,

        [AppKey.Space] = 0x31,
        [AppKey.Tab] = 0x30,
        [AppKey.CapsLock] = 0x39,
        [AppKey.Escape] = 0x35,
        [AppKey.Backspace] = 0x33,
        [AppKey.Delete] = 0x75, // touche "Suppr" avant (forward delete)
        [AppKey.Home] = 0x73,
        [AppKey.End] = 0x77,
        [AppKey.PageUp] = 0x74,
        [AppKey.PageDown] = 0x79,
        [AppKey.Left] = 0x7B,
        [AppKey.Right] = 0x7C,
        [AppKey.Down] = 0x7D,
        [AppKey.Up] = 0x7E,
    };

    // kCGEventSourceStateHIDSystemState : état matériel brut, indépendant de la session/app active.
    private const int HidSystemState = 1;

    public bool IsPressed(AppKey key)
    {
        if (!VirtualKeyCodes.TryGetValue(key, out var keyCode))
        {
            return false;
        }

        try
        {
            return CGEventSourceKeyState(HidSystemState, keyCode);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool CGEventSourceKeyState(int stateId, ushort key);
}
