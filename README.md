# MediaCleanarr Windows

**Your media library, your rules.** Native Windows desktop application written in C# / .NET 10 (WinForms).

## Download / Télécharger

**[Windows x64 releases](https://github.com/leoleandro1217/MediaCleanarr-Windows/releases)**

Download **v0.2.0-alpha.7** (`MediaCleanarr-Windows.exe`) after its GitHub Actions build succeeds. This version is **self-contained**: no Python, Qt, .NET SDK or .NET runtime installation needed. Windows 10/11 x64 only.

Télécharge la version **v0.2.0-alpha.7** après la compilation GitHub Actions. L'exécutable est **autonome**, sans installation de Python, Qt ou .NET. Windows 10/11 x64.

**Note:** Alpha binary is not code-signed. Windows SmartScreen can warn about unknown publishers. Confirm the source and publisher before running. Do not turn off Windows security settings.

## Alpha features / Fonctionnalités

- Native Windows folder picker (including accessible network paths) / Sélection d'un dossier local ou réseau
- Recursive local video inventory / Recherche récursive des vidéos
- Improved movie/TV parsing (filename, year, season and episode; strips 4K, UHD, HDR, codecs and release tags) / Reconnaissance des films et séries avec nettoyage des tags techniques (4K, HDR, UHD, codecs)
- Search and CSV export / Recherche et export CSV
- Cancel ongoing scan / Arrêter le scan
- **Read-only**: never renames, moves or deletes a file / **Lecture seule** : aucun fichier modifié

Extensions: `mkv`, `mp4`, `avi`, `mov`, `wmv`, `m4v`, `ts`, `mpg`, `mpeg`, `webm`.

**Metadata lookup:** TMDb is the default option. Choose TMDb (or OMDb / TheTVDB), enter your own API key, scan files, then click **Check metadata**. TMDb accepts its v3 API key or an API Read Access Token. TMDb title selection: **Original**, **French**, **English**. The displayed title can include the release year. Confidence is marked **Matched**, **Review**, or **Not found**. Uncertain matches do not overwrite the local displayed title.

**SQLite cache:** Enabled by default. Disable with the **SQLite cache** checkbox. The database is stored at `%LOCALAPPDATA%\MediaCleanarr-Windows\metadata-cache.sqlite`. It contains provider, language, title/type/year lookup keys and responses, but **no credentials or file paths**. Positive results expire after 90 days; negative results after 7 days. API keys are only held in memory. Online lookups send the searched title to the provider.

**TMDb attribution:** This product uses the TMDB API but is not endorsed or certified by TMDB. [The Movie Database](https://www.themoviedb.org) · [Official logo and attribution](https://www.themoviedb.org/about/logos-attribution).

**Branding:** The Windows executable and application window use the MediaCleanarr custom logo icon. This does not suppress Windows SmartScreen warnings; the alpha remains unsigned.

**Not implemented yet:** C411 comparison, titles for arbitrary regions beyond French/English, automatic renaming, movie organization. For original titles, choose TMDb instead of OMDb.

## Development / Développement

Requirements for developers: .NET 10 SDK and Windows.

```powershell
dotnet run --project src/MediaCleanarr.Windows/MediaCleanarr.Windows.csproj
```

Build an independent portable exe:

```powershell
dotnet publish src/MediaCleanarr.Windows/MediaCleanarr.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```

GitHub Actions builds on a Windows runner at each push to `main` and publishes the pre-release `v0.2.0-alpha.7`.

## Privacy

The scanner reads filenames and file sizes locally. Only when you click **Check metadata** does it send title searches to your chosen provider.

## Project status

Early alpha for hands-on Windows testing. Title matching is heuristic and may be imperfect. License not yet selected.
