# MediaCleanarr Windows

**Your media library, your rules.**

A native Windows desktop application to explore local and network media libraries.

**Alpha 0.1 — read-only prototype.** This is an early test build, not a finished media renamer.

## Download / Télécharger

Download the Windows executable from [Releases](https://github.com/leoleandro1217/MediaCleanarr-Windows/releases). Open `MediaCleanarr-Windows.exe` on Windows 10/11. No Python installation required. The alpha build is unsigned, so Windows SmartScreen may display a warning; only proceed if you trust the release and repository.

Télécharge l'exécutable depuis les [Releases](https://github.com/leoleandro1217/MediaCleanarr-Windows/releases). Lance `MediaCleanarr-Windows.exe` sous Windows 10/11. Python n'est pas nécessaire. L'exécutable n'est pas signé, donc Windows peut afficher un avertissement.

## Features / Fonctionnalités

- Select a folder with the native Windows folder picker / Choix du dossier dans l'explorateur
- Scan supported video files recursively / Scan récursif des vidéos
- Heuristically detect movie and TV series titles, years, SxxExx numbers / Détection approximative du titre, de l'année et des épisodes
- Search/filter inventory / Recherche et filtrage
- Export UTF-8 CSV / Export CSV
- Read-only: does **not** rename, move or delete files / Lecture seule : **aucune modification** des fichiers

Supported extensions: MKV, MP4, AVI, MOV, WMV, M4V, TS, MPG, MPEG, WEBM.

## Roadmap / À venir

- Metadata integrations: OMDb and TheTVDB (API keys required)
- Original/international title choice
- Preview-and-confirm renaming, safe dry runs
- Optional C411 comparison
- Better recognition for ambiguous filenames and series

These features are **not yet implemented**.

## Build from source

Requires Python 3.12+:

```powershell
python -m pip install -r requirements.txt
python main.py
```

Package on Windows:

```powershell
python -m pip install pyinstaller==6.16.0
pyinstaller --noconfirm --clean --onefile --windowed --name MediaCleanarr-Windows main.py
```

The CI workflow builds on a Windows GitHub Actions runner. Every push to `main` uploads an artifact and refreshes the `v0.1.0-alpha.1` prerelease executable. Tags named `v*` also create releases.

## Security & privacy

The scanner only reads directory entries and file sizes; it never uploads your file list or contacts any media API in this alpha version. Do not scan directories you cannot access. A network path may be used if Windows has access to it.

## License

Copyright © contributors. License to be chosen.
