# SOS-LAN

Application **Windows et macOS** (.NET 8, UI [Avalonia](https://avaloniaui.net/)) qui tourne réduite dans la zone de notification et permet de déclencher une alerte sonore et visuelle sur tous les postes du réseau local qui l'exécutent.

## Fonctionnement

- L'application démarre réduite dans la zone de notification (icône système).
- Elle surveille en permanence un appui long sur une touche définie dans les paramètres (touche `F8` et durée de `5 secondes` par défaut).
- Quand l'appui long est détecté, une alerte est diffusée en UDP broadcast (port configurable, `51515` par défaut) à tous les postes du réseau local exécutant l'application, **sauf** celui qui l'a déclenchée.
- Chaque poste qui reçoit l'alerte affiche une popup rouge "Alerte en provenance de `<nom du poste>`" accompagnée d'un signal sonore en boucle.
- Le bouton **Acquitter** de la popup arrête le son et referme la fenêtre.
- Chaque poste peut définir son propre nom d'affichage, sa touche de déclenchement, la durée d'appui requise et le port réseau via le menu **Paramètres** de l'icône de la zone de notification.
- Pour changer la touche de déclenchement : **Paramètres → Modifier** (à côté de la touche), puis appuyer directement sur la touche souhaitée ; **Annuler** quitte le mode capture sans rien changer.

## Compatibilité multiplateforme

Le code métier (réseau, paramètres, mises à jour) est commun aux deux OS ; les parties dépendantes du système sont isolées derrière des interfaces dans `src/SosLan/Services`, avec une implémentation par plateforme (`Services/Windows`, `Services/MacOS`), sélectionnée automatiquement au démarrage (`PlatformServices`) :

| Fonctionnalité | Windows | macOS |
|---|---|---|
| Détection de l'appui long | `GetAsyncKeyState` (user32) | `CGEventSourceKeyState` (ApplicationServices) |
| Démarrage automatique | Clé de registre `HKCU\...\Run` | LaunchAgent (`~/Library/LaunchAgents`) |
| Signal sonore | `System.Media.SoundPlayer` | boucle sur l'utilitaire système `afplay` |
| Installeur | `SosLan-win.msi` (Windows Installer) | `.pkg` / `.zip` (Velopack) |

**Important — macOS uniquement** : la détection de l'appui long sur une touche en dehors de l'application nécessite que l'utilisateur accorde la permission **Accessibilité** à SOS-LAN (Réglages Système → Confidentialité et sécurité → Accessibilité). Sans cette autorisation, l'appui long ne sera pas détecté ; macOS ne permet pas d'accorder cette permission silencieusement.

**Non testé en conditions réelles sur macOS** : le build macOS compile et est packagé automatiquement par la CI, mais n'a pas pu être testé sur une machine macOS physique au moment de son développement. Un retour de test est bienvenu.

## Prérequis

- Windows 10/11 ou macOS 12+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) pour compiler

## Compilation et exécution

```bash
dotnet build
dotnet run --project src/SosLan/SosLan.csproj
```

Pour générer localement l'installeur Windows (nécessite l'outil `vpk`, voir [Installation et mises à jour](#installation-et-mises-à-jour)) :

```bash
dotnet publish src/SosLan/SosLan.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -o publish
vpk pack -u SosLan -v <version> -p publish -e SosLan.exe --icon src/SosLan/cloche.ico --packTitle "SOS-LAN" --packAuthors "Tomline Prod and Co" --msi --instLocation Either --instLicense installer/CLUF.md -r win-x64
```

Pour générer localement le paquet macOS (à exécuter sur un Mac) :

```bash
dotnet publish src/SosLan/SosLan.csproj -c Release -r osx-x64 --self-contained true -p:DebugType=None -o publish
vpk pack -u SosLan -v <version> -p publish -e SosLan --packTitle "SOS-LAN" --packAuthors "Tomline Prod and Co" -r osx-x64 --channel osx-x64
```

## Réseau

- Tous les postes doivent être sur le même réseau local (même sous-réseau) et utiliser le même port dans les paramètres.
- Les annonces de présence et les alertes sont émises par broadcast UDP sur **chaque interface réseau active** (adresse de broadcast de chaque sous-réseau), et non sur la seule interface par défaut : un poste ayant plusieurs adaptateurs (VPN, réseaux VMware/Hyper-V...) joint donc les postes de tous ses réseaux. Une alerte reçue plusieurs fois (poste multi-interfaces) n'est affichée qu'une fois.
- Machine virtuelle (VMware, VirtualBox, Parallels...) : en mode **réseau « Bridged/Pont »** la VM est un poste à part entière du LAN. En mode **NAT**, elle n'est dans le même réseau que l'hôte (et lui seul) que via le réseau virtuel de l'hyperviseur ; le broadcast ne traverse pas le NAT vers le LAN physique.
- Le pare-feu (Windows Defender ou le pare-feu applicatif macOS) peut demander une autorisation au premier lancement : autoriser l'accès réseau local pour que la diffusion et la réception UDP fonctionnent.

## Configuration

Les paramètres sont stockés par utilisateur dans :

```
%APPDATA%\SosLan\settings.json                              (Windows)
~/Library/Application Support/SosLan/settings.json          (macOS)
```

## Licence

L'application exige une licence validée par le serveur `https://licences.tlpc.fr` (produit `sos-alarme`). Code commun aux deux OS dans `src/SosLan/Licensing` ; seuls le stockage et l'identifiant machine sont propres à chaque plateforme.

- **Démarrage** : sans licence stockée, une fenêtre demande la clé (`/v1/activate`) ; fermer la fenêtre ferme l'application. Avec une licence stockée, `/v1/validate` est appelé à chaque lancement puis toutes les 24 h ; un refus (révoquée, expirée, poste non activé) efface la licence et ferme l'application.
- **Grâce hors-ligne** : serveur injoignable et validation réussie existante → utilisable 30 jours après la dernière validation, sans jamais dépasser `expiresAt`. Un refus explicite n'ouvre jamais de grâce.
- **Démo** : le bouton « Demander une démo (7 jours) » de la fenêtre de saisie (démarrage sans licence valide, jamais en licence gratuite ni dans « Changer la licence » avec une licence valide) appelle `/v1/request-demo` ; la clé reçue est stockée directement et n'est jamais affichée. Une redemande renvoie la même démo (même clé, même échéance : supprimer la licence ne donne aucun jour de plus) ; démo terminée ou révoquée → message dédié, la fenêtre reste ouverte. `demo` est traité comme `expiring` : grâce hors-ligne plafonnée à `expiresAt`, données de licence (dont `expiresAt`) remplacées à chaque validation réussie, donc une prolongation est prise en compte dès la validation suivante.
- **Licence expirée** (`license_expired`) : la clé stockée n'est pas effacée ; l'écran « Licence expirée » propose *Réessayer* (revalide, puis réactive avec la clé stockée si `device_not_activated`), *Saisir une autre licence* ou *Quitter*. Accès bloqué tant que la licence n'est pas valide.
- **Un poste = une activation** : `deviceId` = SHA-256 de `MachineGuid` (Windows) ou de `IOPlatformUUID` (macOS), l'identifiant brut n'est jamais envoyé.
- **Stockage** (aucun fichier) : Windows = `HKCU\Software\TomLine prod&co\SosLan`, valeur unique chiffrée DPAPI ; macOS = Keychain (service `SOS-LAN License`).
- **Paramètres → Licence** : titulaire, type, expiration, boutons *Changer* / *Supprimer la licence* (désactive le poste puis ferme l'application) et indicateur de mode hors-ligne.
- **Licence gratuite** (aucun contrôle, aucun appel réseau) : fichier `licence.ini` dans le dossier d'installation, soit la racine Velopack (dossier contenant `Update.exe`, qui survit aux mises à jour) sous Windows, et le dossier qui contient `SOS-LAN.app` sous macOS ; ou constante `LicenseConfig.Enabled = false` ([LicenseConfig.cs](src/SosLan/Licensing/LicenseConfig.cs)) pour une release sans licence.

## Versionnage automatique

Le numéro de version (`<Version>` dans `src/SosLan/SosLan.csproj`, affiché en bas de la fenêtre Paramètres) est incrémenté automatiquement à chaque commit par un hook Git.

Pour l'activer sur un nouveau clone du dépôt :

```bash
git config core.hooksPath .githooks
```

## Installation et mises à jour

### Windows

L'application se distribue sous forme d'un installeur **`SosLan-win.msi`** (Windows Installer, généré par [Velopack](https://velopack.io/)), qui :

- demande à l'utilisateur d'**accepter le CLUF** ([installer/CLUF.md](installer/CLUF.md)) avant de continuer ;
- laisse l'utilisateur **choisir le dossier d'installation** (page « Change... », par défaut `C:\Program Files\SosLan`) ;
- crée les raccourcis Bureau et menu Démarrer, et enregistre une entrée standard dans **Programmes et fonctionnalités** (désinstallation via Windows, aucun outil supplémentaire requis) ;
- lance l'application en fin d'installation.

Pour une installation silencieuse scriptée (déploiement de parc), le dossier peut être imposé via la propriété `VELOPACK_INSTALLDIR` :

```bash
msiexec /i SosLan-win.msi /qn VELOPACK_INSTALLDIR="D:\Applications\SosLan"
```

### macOS

L'application se distribue sous forme d'un paquet Velopack (`SosLan-osx-x64-Setup.pkg` pour Mac Intel, `SosLan-osx-arm64-Setup.pkg` pour Apple Silicon). Après installation, il faut accorder la permission **Accessibilité** (voir [Compatibilité multiplateforme](#compatibilité-multiplateforme)) pour que la touche d'alerte fonctionne.

### Dans tous les cas

- le démarrage automatique est **activé par défaut** au premier lancement (modifiable ensuite dans les Paramètres) ;
- au démarrage, l'application vérifie silencieusement s'il existe une nouvelle version publiée sur les [releases GitHub](https://github.com/Rimfire03/Sos-Alarme/releases) ; si oui, une **boîte de dialogue demande confirmation** avant de télécharger et d'installer la mise à jour (puis l'application redémarre automatiquement) ;
- une vérification manuelle est aussi disponible via le menu **Vérifier les mises à jour** de l'icône de la zone de notification ;
- la mise à jour automatique ne fonctionne que pour une installation faite via l'installeur officiel (pas pour un lancement via `dotnet run` ou un exécutable copié à la main).

Le CLUF ([installer/CLUF.md](installer/CLUF.md)) précise notamment que le Logiciel est la propriété de **Tomline Prod&Co** et qu'**aucun usage commercial n'est autorisé**.

## Signature du code

Les releases sont signées par la CI avec le **certificat auto-signé de l'éditeur** « TomLine prod&co » (empreinte SHA-1 `168FFE4B6B2D5E042808B001B5146C6D137F532C`, valable jusqu'au 06/10/2031), le même pour les deux OS **et pour tous les projets de l'éditeur** : les postes ne l'approuvent qu'une fois. La clé privée est archivée dans le dépôt privé `Rimfire03/TomLine-signing-keys` (mot de passe hors dépôt).

- **Windows** : MSI, exécutables et DLL signés (SHA-256, horodatage DigiCert) ;
- **macOS** : application signée avec ce certificat et les droits de [installer/SosLan.entitlements](installer/SosLan.entitlements) (nécessaires au runtime .NET). L'identité de l'app étant identique d'une version à l'autre, macOS **conserve la permission Accessibilité** lors des mises à jour (avec une signature ad hoc, elle serait redemandée à chaque version).

**Limites** : un certificat auto-signé n'est reconnu par aucune autorité. Tant qu'il n'est pas approuvé sur un poste, SmartScreen (Windows) et Gatekeeper (macOS) affichent encore leur avertissement, et l'app n'est pas notarisée par Apple. La signature garantit en revanche l'intégrité et l'origine des fichiers.

**Faire approuver le certificat sur un poste** (le fichier `TomLine-signature.cer` est joint à chaque release et présent dans [installer/](installer/TomLine-signature.cer)). Vérifier d'abord l'empreinte ci-dessus, puis :

```bash
# Windows (PowerShell administrateur) ; en parc, déployer par GPO
certutil -addstore -f Root TomLine-signature.cer
certutil -addstore -f TrustedPublisher TomLine-signature.cer

# macOS
sudo security add-trusted-cert -d -r trustRoot -p codeSign -k /Library/Keychains/System.keychain TomLine-signature.cer
```

**Mise en place / renouvellement** : `scripts/Initialize-CodeSigning.ps1` enregistre les secrets GitHub `SIGNING_CERT_PFX_BASE64` et `SIGNING_CERT_PASSWORD` et copie le `.cer` public dans `installer/`. Avec le certificat existant de l'éditeur (le mot de passe n'est jamais affiché) :

```powershell
.\scripts\Initialize-CodeSigning.ps1 -ImportFrom Rimfire03/TomLine-signing-keys -PasswordFile "$env:USERPROFILE\.tomline-signing\TomLine-signature.pfx.password.txt"
```

Sans `-ImportFrom`, le script génère un nouveau certificat (nouvelle empreinte à faire approuver). Sans les secrets, la CI continue de produire des releases **non signées**. Avant l'expiration (2031), renouveler le certificat dans `TomLine-signing-keys`, relancer ce script et republier.

## Releases automatiques

Chaque push sur `main` déclenche deux jobs GitHub Actions ([.github/workflows/release.yml](.github/workflows/release.yml)), un par OS (le job macOS attend que le job Windows ait publié la release pour y ajouter ses propres fichiers) :

1. compilation de l'application (self-contained, par RID : `win-x64`, `osx-x64`, `osx-arm64`) ;
2. packaging avec `vpk` en installeurs Velopack propres à chaque OS ;
3. publication sur une [release GitHub](https://github.com/Rimfire03/Sos-Alarme/releases) taguée avec le numéro de version courant (`<Version>` dans le `.csproj`), avec les installeurs et les fichiers de mise à jour Velopack en pièces jointes.

Aucune action manuelle n'est nécessaire : la version étant déjà incrémentée à chaque commit, chaque push produit une nouvelle release installable et détectable par les postes déjà installés, sur les deux OS.


