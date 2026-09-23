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

Pour un exécutable autonome (sans installer le runtime .NET sur le poste cible) :

```bash
dotnet publish src/SosLan/SosLan.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
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
