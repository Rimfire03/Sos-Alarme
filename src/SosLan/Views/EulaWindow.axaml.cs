using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SosLan.Views;

/// <summary>Présente le CLUF ; fermer la fenêtre ou la refuser équivaut à un refus (l'application se ferme).</summary>
public partial class EulaWindow : Window
{
    private bool _accepted;

    public EulaWindow()
    {
        InitializeComponent();
    }

    public EulaWindow(string text) : this()
    {
        // Rendu texte brut : on retire seulement les marques Markdown les plus visibles.
        EulaText.Text = text.Replace("**", "").Replace("\r\n", "\n").Replace("\n# ", "\n").TrimStart('#', ' ');
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        _accepted = true;
        Close();
    }

    private void OnDeclineClick(object? sender, RoutedEventArgs e) => Close();

    public Task<bool> ShowAndWaitAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        Closed += (_, _) => tcs.TrySetResult(_accepted);
        Topmost = true;
        Show();
        Activate();
        return tcs.Task;
    }
}
