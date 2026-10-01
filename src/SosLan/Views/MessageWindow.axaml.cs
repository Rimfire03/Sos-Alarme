using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SosLan.Views;

/// <summary>
/// Boîte de dialogue minimale (message + OK, ou message + OK/Annuler), Avalonia ne
/// fournissant pas d'équivalent cross-plateforme au MessageBox de WPF/WinForms.
/// Affichée sans fenêtre "owner" : l'application est un utilitaire de zone de
/// notification sans fenêtre principale.
/// </summary>
public partial class MessageWindow : Window
{
    private bool _result;

    public MessageWindow()
    {
        InitializeComponent();
    }

    public MessageWindow(string title, string message, bool showCancel = false) : this()
    {
        Title = title;
        MessageText.Text = message;
        CancelButton.IsVisible = showCancel;
        if (showCancel)
        {
            OkButton.Content = "Oui";
            CancelButton.Content = "Non";
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }

    private Task<bool> ShowAndWaitAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        Closed += (_, _) => tcs.TrySetResult(_result);
        Topmost = true;
        Show();
        Activate();
        return tcs.Task;
    }

    public static Task ShowInfo(string title, string message) =>
        new MessageWindow(title, message).ShowAndWaitAsync();

    public static Task<bool> ShowConfirm(string title, string message) =>
        new MessageWindow(title, message, showCancel: true).ShowAndWaitAsync();
}
