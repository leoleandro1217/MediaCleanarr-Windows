"""MediaCleanarr Windows - read-only local media inventory (alpha)."""
from __future__ import annotations

import csv
import re
import sys
from pathlib import Path

from PySide6.QtCore import QObject, QThread, Qt, Signal, Slot
from PySide6.QtWidgets import (
    QApplication, QFileDialog, QHBoxLayout, QLabel, QLineEdit, QMainWindow,
    QMessageBox, QProgressBar, QPushButton, QTableWidget, QTableWidgetItem,
    QVBoxLayout, QWidget, QHeaderView, QCheckBox,
)

VIDEO_EXTENSIONS = {".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".mpg", ".mpeg", ".webm"}
EPISODE = re.compile(r"(?i)(?:^|[. _-])S(\d{1,2})[ ._-]*E(\d{1,3})(?:\b|[. _-])")
YEAR = re.compile(r"(?<!\d)(19\d{2}|20\d{2})(?!\d)")
NOISE = re.compile(r"(?i)\b(?:2160p|1080p|720p|480p|bluray|brrip|webrip|web-dl|hdtv|x264|x265|h264|h265|hevc|remux|multi|french|vostfr|proper|repack)\b")


def recognize(path: Path) -> tuple[str, str, str]:
    stem = path.stem
    episode = EPISODE.search(stem)
    year = YEAR.search(stem)
    end = min([m.start() for m in (episode, year) if m] or [len(stem)])
    title = stem[:end]
    title = NOISE.sub(" ", title)
    title = re.sub(r"[._\-]+", " ", title)
    title = re.sub(r"\s+", " ", title).strip(" []() ")
    kind = "TV Show" if episode else "Movie"
    detail = f"S{int(episode.group(1)):02}E{int(episode.group(2)):02}" if episode else (year.group(1) if year else "")
    return kind, title or stem, detail


class Scanner(QObject):
    found = Signal(str, str, str, str, str)
    done = Signal(int, int)

    def __init__(self, folder: str, recursive: bool):
        super().__init__()
        self.folder = Path(folder)
        self.recursive = recursive
        self.cancelled = False

    @Slot()
    def run(self):
        count = errors = 0
        # os.walk allows graceful handling of unreadable directories.
        import os
        def onerror(_):
            nonlocal errors
            errors += 1

        for root, dirs, files in os.walk(self.folder, onerror=onerror, followlinks=False):
            if self.cancelled:
                break
            if not self.recursive:
                dirs.clear()
            for filename in files:
                if self.cancelled:
                    break
                p = Path(root) / filename
                if p.suffix.lower() not in VIDEO_EXTENSIONS:
                    continue
                try:
                    size = p.stat().st_size
                    kind, title, detail = recognize(p)
                    self.found.emit(kind, title, detail, f"{size / (1024**3):.2f}", str(p))
                    count += 1
                except OSError:
                    errors += 1
        self.done.emit(count, errors)


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("MediaCleanarr Windows — Alpha")
        self.resize(1120, 700)
        self.rows = []
        self.thread = None
        self.worker = None
        root = QWidget()
        self.setCentralWidget(root)
        layout = QVBoxLayout(root)
        layout.setSpacing(12)

        heading = QLabel("MediaCleanarr Windows")
        heading.setStyleSheet("font-size: 24px; font-weight: 700;")
        layout.addWidget(heading)
        note = QLabel("Local inventory preview • No files will be modified • Metadata/API integration coming later")
        note.setStyleSheet("color: #777;")
        layout.addWidget(note)

        chooser = QHBoxLayout()
        self.folder = QLineEdit()
        self.folder.setPlaceholderText("Choose a local folder or mapped network drive…")
        self.browse = QPushButton("Browse…")
        self.browse.clicked.connect(self.choose_folder)
        chooser.addWidget(self.folder, 1)
        chooser.addWidget(self.browse)
        layout.addLayout(chooser)

        actions = QHBoxLayout()
        self.recursive = QCheckBox("Include subfolders")
        self.recursive.setChecked(True)
        self.scan_button = QPushButton("Scan library")
        self.scan_button.clicked.connect(self.start_scan)
        self.cancel_button = QPushButton("Stop")
        self.cancel_button.clicked.connect(self.stop_scan)
        self.cancel_button.setEnabled(False)
        self.export_button = QPushButton("Export CSV")
        self.export_button.clicked.connect(self.export_csv)
        self.export_button.setEnabled(False)
        actions.addWidget(self.recursive)
        actions.addWidget(self.scan_button)
        actions.addWidget(self.cancel_button)
        actions.addStretch()
        actions.addWidget(self.export_button)
        layout.addLayout(actions)

        searchline = QHBoxLayout()
        searchline.addWidget(QLabel("Filter:"))
        self.search = QLineEdit()
        self.search.setPlaceholderText("Movie, series, year, filename…")
        self.search.textChanged.connect(self.filter_rows)
        searchline.addWidget(self.search)
        layout.addLayout(searchline)

        self.table = QTableWidget(0, 5)
        self.table.setHorizontalHeaderLabels(["Type", "Detected title", "Year / Episode", "Size (GiB)", "Full path"])
        self.table.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
        self.table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.table.setSortingEnabled(False)
        self.table.horizontalHeader().setSectionResizeMode(1, QHeaderView.ResizeMode.Stretch)
        self.table.horizontalHeader().setSectionResizeMode(4, QHeaderView.ResizeMode.Stretch)
        layout.addWidget(self.table, 1)
        self.progress = QProgressBar()
        self.progress.setRange(0, 0)
        self.progress.setVisible(False)
        layout.addWidget(self.progress)
        self.status = QLabel("Ready. Choose a folder to begin.")
        layout.addWidget(self.status)

    def choose_folder(self):
        path = QFileDialog.getExistingDirectory(self, "Select media folder", self.folder.text())
        if path:
            self.folder.setText(path)

    def start_scan(self):
        path = Path(self.folder.text().strip())
        if not path.is_dir():
            QMessageBox.warning(self, "Invalid folder", "Select an existing, accessible folder.")
            return
        self.rows.clear()
        self.table.setRowCount(0)
        self.scan_button.setEnabled(False)
        self.browse.setEnabled(False)
        self.export_button.setEnabled(False)
        self.cancel_button.setEnabled(True)
        self.progress.setVisible(True)
        self.status.setText("Scanning…")
        self.thread = QThread(self)
        self.worker = Scanner(str(path), self.recursive.isChecked())
        self.worker.moveToThread(self.thread)
        self.thread.started.connect(self.worker.run)
        self.worker.found.connect(self.add_result)
        self.worker.done.connect(self.finish_scan)
        self.worker.done.connect(self.thread.quit)
        self.thread.finished.connect(self.worker.deleteLater)
        self.thread.finished.connect(self.thread.deleteLater)
        self.thread.start()

    def stop_scan(self):
        if self.worker is not None:
            self.worker.cancelled = True
            self.status.setText("Stopping…")

    @Slot(str, str, str, str, str)
    def add_result(self, kind, title, detail, size, filename):
        row = (kind, title, detail, size, filename)
        self.rows.append(row)
        index = self.table.rowCount()
        self.table.insertRow(index)
        for col, value in enumerate(row):
            self.table.setItem(index, col, QTableWidgetItem(value))
        needle = self.search.text().lower().strip()
        if needle:
            self.table.setRowHidden(index, needle not in " ".join(row).lower())
        if len(self.rows) % 50 == 0:
            self.status.setText(f"Scanning… {len(self.rows)} media files")

    @Slot(int, int)
    def finish_scan(self, count, errors):
        self.scan_button.setEnabled(True)
        self.browse.setEnabled(True)
        self.cancel_button.setEnabled(False)
        self.export_button.setEnabled(bool(self.rows))
        self.progress.setVisible(False)
        self.status.setText(f"Scan finished: {count} video files, {errors} inaccessible entries. Nothing modified.")
        self.worker = None
        self.thread = None

    def filter_rows(self, term):
        needle = term.lower().strip()
        for i, row in enumerate(self.rows):
            self.table.setRowHidden(i, needle not in " ".join(row).lower())

    def export_csv(self):
        path, _ = QFileDialog.getSaveFileName(self, "Export inventory", "mediacleanarr-inventory.csv", "CSV files (*.csv)")
        if not path:
            return
        try:
            with open(path, "w", newline="", encoding="utf-8-sig") as handle:
                writer = csv.writer(handle)
                writer.writerow(["Type", "Detected title", "Year / Episode", "Size (GiB)", "Full path"])
                writer.writerows(self.rows)
            self.status.setText(f"Exported {len(self.rows)} entries to {path}")
        except OSError as exc:
            QMessageBox.critical(self, "Export failed", str(exc))


def main():
    app = QApplication(sys.argv)
    app.setStyle("Fusion")
    window = MainWindow()
    window.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
