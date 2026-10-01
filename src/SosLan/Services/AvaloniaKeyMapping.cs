using Avalonia.Input;
using SosLan.Models;

namespace SosLan.Services;

/// <summary>
/// Convertit une touche capturée par Avalonia (déjà normalisée par l'OS) vers notre
/// enum AppKey indépendant de la plateforme, utilisé pour la capture de la touche
/// d'alerte dans l'interface des paramètres.
/// </summary>
public static class AvaloniaKeyMapping
{
    private static readonly Dictionary<Key, AppKey> Map = new()
    {
        [Key.A] = AppKey.A, [Key.B] = AppKey.B, [Key.C] = AppKey.C, [Key.D] = AppKey.D,
        [Key.E] = AppKey.E, [Key.F] = AppKey.F, [Key.G] = AppKey.G, [Key.H] = AppKey.H,
        [Key.I] = AppKey.I, [Key.J] = AppKey.J, [Key.K] = AppKey.K, [Key.L] = AppKey.L,
        [Key.M] = AppKey.M, [Key.N] = AppKey.N, [Key.O] = AppKey.O, [Key.P] = AppKey.P,
        [Key.Q] = AppKey.Q, [Key.R] = AppKey.R, [Key.S] = AppKey.S, [Key.T] = AppKey.T,
        [Key.U] = AppKey.U, [Key.V] = AppKey.V, [Key.W] = AppKey.W, [Key.X] = AppKey.X,
        [Key.Y] = AppKey.Y, [Key.Z] = AppKey.Z,

        [Key.D0] = AppKey.D0, [Key.D1] = AppKey.D1, [Key.D2] = AppKey.D2, [Key.D3] = AppKey.D3,
        [Key.D4] = AppKey.D4, [Key.D5] = AppKey.D5, [Key.D6] = AppKey.D6, [Key.D7] = AppKey.D7,
        [Key.D8] = AppKey.D8, [Key.D9] = AppKey.D9,

        [Key.F1] = AppKey.F1, [Key.F2] = AppKey.F2, [Key.F3] = AppKey.F3, [Key.F4] = AppKey.F4,
        [Key.F5] = AppKey.F5, [Key.F6] = AppKey.F6, [Key.F7] = AppKey.F7, [Key.F8] = AppKey.F8,
        [Key.F9] = AppKey.F9, [Key.F10] = AppKey.F10, [Key.F11] = AppKey.F11, [Key.F12] = AppKey.F12,

        [Key.Space] = AppKey.Space,
        [Key.Tab] = AppKey.Tab,
        [Key.CapsLock] = AppKey.CapsLock,
        [Key.Escape] = AppKey.Escape,
        [Key.Back] = AppKey.Backspace,
        [Key.Delete] = AppKey.Delete,
        [Key.Home] = AppKey.Home,
        [Key.End] = AppKey.End,
        [Key.PageUp] = AppKey.PageUp,
        [Key.PageDown] = AppKey.PageDown,
        [Key.Left] = AppKey.Left,
        [Key.Right] = AppKey.Right,
        [Key.Up] = AppKey.Up,
        [Key.Down] = AppKey.Down,
    };

    public static AppKey? FromAvaloniaKey(Key key) => Map.TryGetValue(key, out var appKey) ? appKey : null;
}
