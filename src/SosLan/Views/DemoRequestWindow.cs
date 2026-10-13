using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TomLine.Licensing;

namespace SosLan.Views;

/// <summary>
/// Mode « informations client demandées par le serveur » de <see cref="DemoRequestWindow"/> : formulaire prérempli,
/// champs manquants mis en évidence, consentement facultatif, « Enregistrer » inactif tant que le formulaire est
/// invalide et « Plus tard » qui ne bloque rien.
/// </summary>
public sealed class ProfileFormOptions
{
    public string Title { get; init; } = "Vos informations";
    public string Intro { get; init; } = "Pour compléter votre dossier, merci de renseigner les informations suivantes.";
    public string SaveText { get; init; } = "Enregistrer";
    public string LaterText { get; init; } = "Plus tard";

    /// <summary>Champs à mettre en évidence au départ (codes de <c>ProfileRequest.Missing</c>).</summary>
    public IReadOnlyCollection<string> Highlight { get; init; } = Array.Empty<string>();

    /// <summary>Envoi de la saisie (<c>LicenseService.SubmitProfileAsync</c>) : succès, motif, champs refusés.</summary>
    public required Func<DemoRequestProfile, Task<(bool Done, string? Reason, IReadOnlyList<string> Fields)>> SubmitAsync { get; init; }
}

/// <summary>
/// Formulaire client : civilité, nom, prénom, e-mail, téléphone, société / association et informations facultatives.
/// Deux usages : demande de démo (consentement obligatoire) et informations demandées par le serveur
/// (<see cref="ProfileFormOptions"/>). Se ferme avec le <see cref="DemoRequestProfile"/> validé, ou null (annulation).
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
    private readonly Button _send;
    private readonly ProfileFormOptions? _options;
    private readonly Dictionary<string, TemplatedControl> _controls;
    private readonly HashSet<string> _flagged = new();
    private DemoRequestProfile? _result;
    private bool _busy;

    public DemoRequestWindow() : this(null, null)
    {
    }

    public DemoRequestWindow(DemoRequestProfile? initial, ProfileFormOptions? options = null)
    {
        _options = options;
        var demo = options == null;
        Title = (demo ? "Demande de démo" : options!.Title) + " - SOS-LAN";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        foreach (var c in DemoRequestProfile.Civilities)
        {
            _civility.Items.Add(new ComboBoxItem { Content = DemoRequestProfile.CivilityLabel(c), Tag = c });
        }

        _controls = new Dictionary<string, TemplatedControl>
        {
            ["civility"] = _civility, ["lastName"] = _lastName, ["firstName"] = _firstName, ["email"] = _email,
            ["phone"] = _phone, ["organization"] = _organization, ["organizationId"] = _organizationId,
            ["jobTitle"] = _jobTitle, ["postalCode"] = _postalCode, ["city"] = _city, ["country"] = _country
        };

        _consent.Content = new TextBlock
        {
            Text = demo
                ? "J'accepte d'être recontacté(e) au sujet de cette démonstration. *"
                : "J'accepte d'être recontacté(e) au sujet de mon logiciel.",
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
            Text = demo
                ? "Renseignez vos coordonnées : elles sont enregistrées avec votre démonstration (7 jours)."
                : options!.Intro,
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
            Text = demo
                ? "Ces informations ne servent qu'à la gestion de votre démonstration et ne sont pas transmises à des tiers."
                : "Ces informations ne servent qu'à la gestion de votre licence et ne sont pas transmises à des tiers.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12
        });
        form.Children.Add(_error);

        var cancel = new Button { Content = demo ? "Annuler" : options!.LaterText, Padding = new Thickness(16, 6) };
        cancel.Click += (_, _) => CloseWith(null);
        _send = new Button { Content = demo ? "Demander la démo" : options!.SaveText, Padding = new Thickness(16, 6), IsDefault = true };
        _send.Click += async (_, _) => await ConfirmAsync();
        form.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Children = { cancel, _send }
        });

        Content = new ScrollViewer { Content = form };

        _civility.SelectionChanged += (_, _) => { UpdateOrganizationVisibility(); Refresh(); };
        foreach (var tb in new[] { _lastName, _firstName, _email, _phone, _organization, _organizationId, _jobTitle, _postalCode, _city, _country })
        {
            tb.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Refresh(); };
        }

        _consent.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) Refresh(); };

        if (initial != null)
        {
            Load(initial);
        }

        if (options != null)
        {
            foreach (var f in options.Highlight)
            {
                _flagged.Add(f);
            }
        }

        UpdateOrganizationVisibility();
        Refresh();
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

    private DemoRequestProfile Read() => new()
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

    /// <summary>Mode serveur : « Enregistrer » inactif tant que le formulaire est invalide ; un champ mis en évidence
    /// (manquant ou refusé par le serveur) reste rouge tant qu'il est invalide.</summary>
    private void Refresh()
    {
        if (_options == null || _send == null)
        {
            return;
        }

        var invalid = Read().Validate(requireConsent: false);
        _send.IsEnabled = !_busy && invalid.Count == 0;
        _flagged.RemoveWhere(f => !invalid.Contains(f));
        foreach (var (name, control) in _controls)
        {
            if (_flagged.Contains(name))
            {
                control.BorderBrush = Brushes.Red;
                control.BorderThickness = new Thickness(2);
            }
            else
            {
                control.ClearValue(TemplatedControl.BorderBrushProperty);
                control.ClearValue(TemplatedControl.BorderThicknessProperty);
            }
        }
    }

    private async Task ConfirmAsync()
    {
        var p = Read();
        if (_options == null)
        {
            // Demande de démo : validation complète (consentement obligatoire), la saisie est renvoyée à l'appelant.
            var bad = p.Validate();
            if (bad.Count > 0)
            {
                _error.Text = "À corriger ou compléter : " + string.Join(", ", bad.Select(DemoRequestProfile.DescribeField)) + ".";
                return;
            }

            CloseWith(p.Normalized());
            return;
        }

        if (_busy || p.Validate(requireConsent: false).Count > 0)
        {
            return;
        }

        _busy = true;
        _send.IsEnabled = false;
        _error.Foreground = Brushes.Gray;
        _error.Text = "Enregistrement...";
        try
        {
            var (done, reason, fields) = await _options.SubmitAsync(p.Normalized());
            if (done)
            {
                CloseWith(p.Normalized());
                return;
            }

            foreach (var f in fields)
            {
                _flagged.Add(f);
            }

            _error.Foreground = Brushes.Red;
            _error.Text = reason switch
            {
                "profile_invalid" when fields.Count > 0 =>
                    "À corriger : " + string.Join(", ", fields.Select(DemoRequestProfile.DescribeField)) + ".",
                "network" => "Le serveur de licences est injoignable : vos informations seront redemandées plus tard.",
                _ => LicenseReasons.Describe(reason)
            };
        }
        catch (Exception ex)
        {
            _error.Foreground = Brushes.Red;
            _error.Text = "Erreur : " + ex.Message;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void CloseWith(DemoRequestProfile? profile)
    {
        _result = profile;
        Close(profile);
    }

    /// <summary>Affiche la fenêtre (modale si un parent est fourni, sinon au premier plan) et renvoie la saisie validée,
    /// ou null si l'utilisateur a choisi « Plus tard » / « Annuler » ou fermé la fenêtre.</summary>
    public async Task<DemoRequestProfile?> ShowAndWaitAsync(Window? owner = null)
    {
        if (owner != null)
        {
            await ShowDialog<DemoRequestProfile?>(owner);
            return _result;
        }

        var tcs = new TaskCompletionSource<DemoRequestProfile?>();
        Closed += (_, _) => tcs.TrySetResult(_result);
        Topmost = true;
        Show();
        Activate();
        return await tcs.Task;
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
