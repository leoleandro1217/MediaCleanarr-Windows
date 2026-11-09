# MediaCleanarr Windows

**Your media library, your rules.** Native Windows desktop application written in C# / .NET 10 (WinForms).

## Download / Télécharger

**[Windows x64 releases](https://github.com/leoleandro1217/MediaCleanarr-Windows/releases)**

Download **v0.2.0-alpha.6** (`MediaCleanarr-Windows.exe`) after its GitHub Actions build succeeds. This version is **self-contained**: no Python, Qt, .NET SDK or .NET runtime installation needed. Windows 10/11 x64 only.

Télécharge la version **v0.2.0-alpha.6** après la compilation GitHub Actions. L'exécutable est **autonome**, sans installation de Python, Qt ou .NET. Windows 10/11 x64.

**Note:** Alpha binary is not code-signed. Windows SmartScreen can warn about unknown publishers. Confirm the source and publisher before running. Do not turn off Windows security settings.

## Alpha features / Fonctionnalités

- Native Windows folder picker (including accessible network paths) / Sélection d'un dossier local ou réseau
- Recursive local video inventory / Recherche récursive des vidéos
- Improved movie/TV parsing (filename, year, season and episode; strips 4K, UHD, HDR, codecs and release tags) / Reconnaissance des films et séries avec nettoyage des tags techniques (4K, HDR, UHD, codecs)
- Search and CSV export / Recherche et export CSV
- Cancel ongoing scan / Arrêter le scan
- **Read-only**: never renames, moves or deletes a file / **Lecture seule** : aucun fichier modifié

Extensions: `mkv`, `mp4`, `avi`, `mov`, `wmv`, `m4v`, `ts`, `mpg`, `mpeg`, `webm`.

**Optional metadata checks:** Select OMDb or TheTVDB, enter your own API key (plus a subscriber PIN if required for TheTVDB), scan files, then click **Check metadata**. The displayed title can include the movie or series year. Results are marked Matched / Review / Not found; inspect uncertain matches. Metadata requests send the detected title to the selected provider. Keys are held only in memory and are not stored in the app or exported to CSV.\n\n**Branding:** The Windows executable and application window use the MediaCleanarr custom logo icon. This does not suppress Windows SmartScreen warnings; the alpha remains unsigned.

**Not implemented yet:** C411 comparison, choosing a specific regional title, automatic renaming, movie organization. OMDb does not guarantee the original-language title; its returned title is only the service's preferred result.

## Development / Développement

Requirements for developers: .NET 10 SDK and Windows.

```powershell
dotnet run --project src/MediaCleanarr.Windows/MediaCleanarr.Windows.csproj
```

Build an independent portable exe:

```powershell
dotnet publish src/MediaCleanarr.Windows/MediaCleanarr.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```

GitHub Actions builds on a Windows runner at each push to `main` and publishes the pre-release `v0.2.0-alpha.6`.

## Privacy

The scanner reads filenames and file sizes on your computer. It doesn't contact media databases or upload your inventory.

## Project status

Early alpha for hands-on Windows testing. Title matching is heuristic and may be imperfect. License not yet selected.
