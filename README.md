# MediaCleanarr Windows

**Your media library, your rules.** Native Windows desktop application written in C# / .NET 10 (WinForms).

## Download / Télécharger

**[Windows x64 releases](https://github.com/leoleandro1217/MediaCleanarr-Windows/releases)**

Download **v0.2.0-alpha.2** (`MediaCleanarr-Windows.exe`) after its GitHub Actions build succeeds. This version is **self-contained**: no Python, Qt, .NET SDK or .NET runtime installation needed. Windows 10/11 x64 only.

Télécharge la version **v0.2.0-alpha.2** après la compilation GitHub Actions. L'exécutable est **autonome**, sans installation de Python, Qt ou .NET. Windows 10/11 x64.

**Note:** Alpha binary is not code-signed. Windows SmartScreen can warn about unknown publishers. Confirm the source and publisher before running. Do not turn off Windows security settings.

## Alpha features / Fonctionnalités

- Native Windows folder picker (including accessible network paths) / Sélection d'un dossier local ou réseau
- Recursive local video inventory / Recherche récursive des vidéos
- Approximate movie/TV parsing (filename, year, season and episode) / Reconnaissance approximative des films et séries
- Search and CSV export / Recherche et export CSV
- Cancel ongoing scan / Arrêter le scan
- **Read-only**: never renames, moves or deletes a file / **Lecture seule** : aucun fichier modifié

Extensions: `mkv`, `mp4`, `avi`, `mov`, `wmv`, `m4v`, `ts`, `mpg`, `mpeg`, `webm`.

**Not implemented yet:** OMDb / TheTVDB, C411 comparison, choosing international titles, automatic renaming, movie organization.

## Development / Développement

Requirements for developers: .NET 10 SDK and Windows.

```powershell
dotnet run --project src/MediaCleanarr.Windows/MediaCleanarr.Windows.csproj
```

Build an independent portable exe:

```powershell
dotnet publish src/MediaCleanarr.Windows/MediaCleanarr.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```

GitHub Actions builds on a Windows runner at each push to `main` and publishes the pre-release `v0.2.0-alpha.2`.

## Privacy

The scanner reads filenames and file sizes on your computer. It doesn't contact media databases or upload your inventory.

## Project status

Early alpha for hands-on Windows testing. Title matching is heuristic and may be imperfect. License not yet selected.
