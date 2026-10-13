using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TomLine.Licensing;

namespace SosLan.Views;

/// <summary>
/// Formulaire de demande de démo : civilité, nom, prénom, e-mail, téléphone, société / association et informations
/// facultatives. Se ferme avec le <see cref="DemoRequestProfile"/> validé, ou null si l'utilisateur annule.
/// </summary>
public sealed class DemoRequestWindow : Window
{
    private readonly ComboBox _civility = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _lastName = new(), _firstName = new(), _email = new(), _phone = new(), _organization = new(),
        _organizationId = new(), _jobTitle = new(), _postalCode = new(), _city = new(), _country = new();
    private readonly CheckBox _consent = new();
    private readonly CheckBox _newsletter = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.Red, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _organizationRow;

    public DemoRequestWindow() : this(null)
    {
    }

    public DemoRequestWindow(DemoRequestProfile? initial)
    {
        Title = "Demande de démo - SOS-LAN";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        foreach (var c in DemoRequestProfile.Civilities)
        {
            _civility.Items.Add(new ComboBoxItem { Content = DemoRequestProfile.CivilityLabel(c), Tag = c });
        }

        _consent.Content = new TextBlock
        {
            Text = "J'accepte d'être recontacté(e) au sujet de cette démonstration. *",
            TextWrapping = TextWrapping.Wrap
        };
        _newsletter.Content = new TextBlock
        {
            Text = "Je souhaite recevoir les actualités du logiciel.",
            TextWrapping = TextWrapping.Wrap
        };

        _organizationRow = Field("Nom de la société / de l'association *", _organization);

        var form = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        form.Children.Add(new TextBlock
        {
            Text = "Renseignez vos coordonnées : elles sont enregistrées avec votre démonstration (7 jours).",
            TextWrapping = TextWrapping.Wrap
        });
        form.Children.Add(Field("Civilité *", _civility));
        form.Children.Add(_organizationRow);
        form.Children.Add(Pair(Field("Nom *", _lastName), Field("Prénom *", _firstName)));
        form.Children.Add(Pair(Field("E-mail *", _email), Field("Téléphone *", _phone)));
        form.Children.Add(new TextBlock { Text = "Informations complémentaires (facultatif)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        form.Children.Add(Pair(Field("SIRET / n° RNA", _organizationId), Field("Fonction", _jobTitle)));
        form.Children.Add(Pair(Field("Code postal", _postalCode), Field("Ville", _city)));
        form.Children.Add(Field("Pays", _country));
        form.Children.Add(_consent);
        form.Children.Add(_newsletter);
        form.Children.Add(new TextBlock
        {
            Text = "Ces informations ne servent qu'à la gestion de votre démonstration et ne sont pas transmises à des tiers.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12
        });
        form.Children.Add(_error);

        var cancel = new Button { Content = "Annuler", Padding = new Thickness(16, 6) };
        cancel.Click += (_, _) => Close(null);
        var send = new Button { Content = "Demander la démo", Padding = new Thickness(16, 6), IsDefault = true };
        send.Click += (_, _) => Confirm();
        form.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Children = { cancel, send }
        });

        Content = new ScrollViewer { Content = form };

        _civility.SelectionChanged += (_, _) => UpdateOrganizationVisibility();
        if (initial != null)
        {
            Load(initial);
        }

        UpdateOrganizationVisibility();
    }

    private string SelectedCivility => (_civility.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void Load(DemoRequestProfile p)
    {
        _civility.SelectedIndex = DemoRequestProfile.Civilities.ToList().IndexOf(p.Civility);
        _lastName.Text = p.LastName;
        _firstName.Text = p.FirstName;
        _email.Text = p.Email;
        _phone.Text = p.Phone;
        _organization.Text = p.Organization;
        _organizationId.Text = p.OrganizationId;
        _jobTitle.Text = p.JobTitle;
        _postalCode.Text = p.PostalCode;
        _city.Text = p.City;
        _country.Text = p.Country;
        _consent.IsChecked = p.ContactConsent;
        _newsletter.IsChecked = p.NewsletterOptIn;
    }

    private void UpdateOrganizationVisibility() =>
        _organizationRow.IsVisible = SelectedCivility is DemoRequestProfile.CivilitySociete or DemoRequestProfile.CivilityAssociation;

    private void Confirm()
    {
        var p = new DemoRequestProfile
        {
            Civility = SelectedCivility,
            LastName = _lastName.Text ?? "",
            FirstName = _firstName.Text ?? "",
            Email = _email.Text ?? "",
            Phone = _phone.Text ?? "",
            Organization = _organization.Text ?? "",
            OrganizationId = _organizationId.Text ?? "",
            JobTitle = _jobTitle.Text ?? "",
            PostalCode = _postalCode.Text ?? "",
            City = _city.Text ?? "",
            Country = _country.Text ?? "",
            ContactConsent = _consent.IsChecked == true,
            NewsletterOptIn = _newsletter.IsChecked == true
        };

        var bad = p.Validate();
        if (bad.Count > 0)
        {
            _error.Text = "À corriger ou compléter : " + string.Join(", ", bad.Select(DemoRequestProfile.DescribeField)) + ".";
            return;
        }

        Close(p.Normalized());
    }

    private static StackPanel Field(string label, Control input)
    {
        var s = new StackPanel { Spacing = 2 };
        s.Children.Add(new TextBlock { Text = label });
        s.Children.Add(input);
        return s;
    }

    private static Grid Pair(Control left, Control right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        g.Children.Add(left);
        g.Children.Add(right);
        return g;
    }
}
