# SOS-LAN

Application Windows (.NET 8 / WPF) qui tourne réduite dans la zone de notification et permet de déclencher une alerte sonore et visuelle sur tous les postes du réseau local qui l'exécutent.

## Fonctionnement

- L'application démarre réduite dans la zone de notification (icône système).
- Elle surveille en permanence un appui long sur une touche définie dans les paramètres (touche `F8` et durée de `5 secondes` par défaut).
- Quand l'appui long est détecté, une alerte est diffusée en UDP broadcast (port configurable, `51515` par défaut) à tous les postes du réseau local exécutant l'application, **sauf** celui qui l'a déclenchée.
- Chaque poste qui reçoit l'alerte affiche une popup rouge "Alerte en provenance de `<nom du poste>`" accompagnée d'un signal sonore en boucle.
- Le bouton **Acquitter** de la popup arrête le son et referme la fenêtre.
- Chaque poste peut définir son propre nom d'affichage, sa touche de déclenchement, la durée d'appui requise et le port réseau via le menu **Paramètres** de l'icône de la zone de notification.

## Prérequis

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) pour compiler

## Compilation et exécution

```bash
dotnet build
dotnet run --project src/SosLan/SosLan.csproj
```

Pour générer localement l'installeur (nécessite l'outil `vpk`, voir [Installation et mises à jour](#installation-et-mises-à-jour)) :

```bash
dotnet publish src/SosLan/SosLan.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -o publish
vpk pack -u SosLan -v <version> -p publish -e SosLan.exe --icon src/SosLan/cloche.ico --packTitle "SOS-LAN" --packAuthors "Tomline Prod and Co" --msi --instLocation Either --instLicense installer/CLUF.md -r win-x64
```

## Réseau

- Tous les postes doivent être sur le même réseau local (même sous-réseau) et utiliser le même port dans les paramètres.
- Le pare-feu Windows peut demander une autorisation au premier lancement : autoriser l'accès réseau privé pour que la diffusion et la réception UDP fonctionnent.

## Configuration

Les paramètres sont stockés par utilisateur dans :

```
%APPDATA%\SosLan\settings.json
```

## Versionnage automatique

Le numéro de version (`<Version>` dans `src/SosLan/SosLan.csproj`, affiché en bas de la fenêtre Paramètres) est incrémenté automatiquement à chaque commit par un hook Git.

Pour l'activer sur un nouveau clone du dépôt :

```bash
git config core.hooksPath .githooks
```

## Installation et mises à jour

L'application se distribue sous forme d'un installeur **`SosLan-win.msi`** (Windows Installer, généré par [Velopack](https://velopack.io/)), qui :

- demande à l'utilisateur d'**accepter le CLUF** ([installer/CLUF.md](installer/CLUF.md)) avant de continuer ;
- laisse l'utilisateur **choisir le dossier d'installation** (page « Change... », par défaut `C:\Program Files\SosLan`) ;
- crée les raccourcis Bureau et menu Démarrer, et enregistre une entrée standard dans **Programmes et fonctionnalités** (désinstallation via Windows, aucun outil supplémentaire requis) ;
- lance l'application en fin d'installation.

Après installation :

- le démarrage automatique avec Windows est **activé par défaut** au premier lancement (modifiable ensuite dans les Paramètres) ;
- au démarrage, l'application vérifie silencieusement s'il existe une nouvelle version publiée sur les [releases GitHub](https://github.com/Rimfire03/Sos-Alarme/releases) ; si oui, une **boîte de dialogue demande confirmation** avant de télécharger et d'installer la mise à jour (puis l'application redémarre automatiquement) ;
- une vérification manuelle est aussi disponible via le menu **Vérifier les mises à jour** de l'icône de la zone de notification ;
- la mise à jour automatique ne fonctionne que pour une installation faite via `SosLan-win.msi` (pas pour un lancement via `dotnet run` ou un exécutable copié à la main).

Le CLUF ([installer/CLUF.md](installer/CLUF.md)) précise notamment que le Logiciel est la propriété de **Tomline Prod&Co** et qu'**aucun usage commercial n'est autorisé**.

Pour une installation silencieuse scriptée (déploiement de parc), le dossier peut être imposé via la propriété `VELOPACK_INSTALLDIR` :

```bash
msiexec /i SosLan-win.msi /qn VELOPACK_INSTALLDIR="D:\Applications\SosLan"
```

## Releases automatiques

Chaque push sur `main` déclenche un workflow GitHub Actions ([.github/workflows/release.yml](.github/workflows/release.yml)) qui :

1. compile l'application (self-contained, win-x64) ;
2. la package avec `vpk` en installeur Velopack (`SosLan-win.msi`) ;
3. publie une [release GitHub](https://github.com/Rimfire03/Sos-Alarme/releases) taguée avec le numéro de version courant (`<Version>` dans le `.csproj`), avec l'installeur et les fichiers de mise à jour Velopack en pièces jointes.

Aucune action manuelle n'est nécessaire : la version étant déjà incrémentée à chaque commit, chaque push produit une nouvelle release installable et détectable par les postes déjà installés.

