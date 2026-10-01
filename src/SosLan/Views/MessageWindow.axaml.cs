using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SosLan.Views;

/// <summary>
/// Boîte de dialogue minimale (message + OK, ou message + OK/Annuler), Avalonia ne
/// fournissant pas d'équivalent cross-plateforme au MessageBox de WPF/WinForms.
/// </summary>
public partial class MessageWindow : Window
{
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

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    public static Task ShowInfo(Window owner, string title, string message) =>
        new MessageWindow(title, message).ShowDialog(owner);

    public static Task<bool> ShowConfirm(Window owner, string title, string message) =>
        new MessageWindow(title, message, showCancel: true).ShowDialog<bool>(owner);
}
