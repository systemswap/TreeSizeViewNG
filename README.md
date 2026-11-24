<p align="center"><img src="Assets/Icon.png" width="96" alt="TreeSizeViewNG"></p>

# TreeSizeViewNG

Plattformübergreifende Ordnergrößen-Analyse mit **.NET 10**, **Avalonia 11.3** und dem ursprünglichen **MIT-lizenzierten TreeDataGrid 11.1.0**.

## Funktionen

- Scan eines Ordners oder ganzen Laufwerks im Hintergrund, jederzeit abbrechbar
- Baum mit Ordnern und Dateien: Größe, Anzahl Dateien/Ordner, Anteil in %, Dateityp-Symbole
- Ebenenweise auf- und zuklappen (− / +) oder alles auf einmal
- Sortieren per Klick auf die Spaltenüberschrift – auch bei Millionen Dateien, ohne dass die Oberfläche einfriert
  (Zahlenspalten zuerst absteigend, Namen in natürlicher Reihenfolge: „Datei2“ vor „Datei10“)
- Suche nach Ordner- und Dateinamen
- Mindestgröße für berücksichtigte Dateien
- **CSV-Export für Excel** (unten rechts), siehe unten
- Helles und dunkles Design, folgt automatisch der Windows-Einstellung
- Symbolische Links und Junctions werden ignoriert, nicht zugängliche Einträge übersprungen
- Unerwartete Fehler beenden das Programm nicht, sondern erscheinen in der Statusleiste und werden in
  `%LOCALAPPDATA%\TreeSizeViewNG\fehler.log` (Linux: `~/.local/share/TreeSizeViewNG/fehler.log`) protokolliert

## CSV-Export

Eine Zeile pro Ordner bzw. Datei, Trennzeichen und Dezimalzeichen passend zum System (deutsches Excel: `;` und `,`), UTF-8 mit BOM.

| Spalte | Inhalt |
|---|---|
| Typ | `Ordner` oder `Datei` |
| Größe (Bytes) | reine Zahl – zum Summieren, Sortieren und für Pivot-Tabellen |
| Größe (MB), Anteil (%) | Anteil bezogen auf den exportierten Ordner (= 100 %) |
| Dateien, Unterordner, Tiefe, Erweiterung, Name | |
| Ebene 1 … N | jede Ordnerebene in einer eigenen Spalte |
| Vollständiger Pfad | |

- **Ab welchem Ordner:** der im Baum ausgewählte Ordner (bei einer Datei deren Ordner), ohne Auswahl der ganze Scan – immer mit allen Unterebenen. Ein aktiver Suchfilter wirkt auch auf den Export.
- **„Übergeordnete Ebenen mitexportieren“:**
  - aus: `C:\ersteEbene\Test` gewählt → Ebene 1 = `Test`
  - an: Ebene 1 = `C:`, Ebene 2 = `ersteEbene`, Ebene 3 = `Test`
- **Summen:** Ordnerzeilen enthalten bereits die Summe ihres Inhalts – für Gesamtsummen nach Typ = `Datei` filtern.
- Excel fasst höchstens ca. 1,05 Mio. Zeilen; für größere Exporte Power Query verwenden oder die Mindestgröße erhöhen.

## Entwickeln

1. .NET 10 SDK installieren (`global.json` verlangt ein stabiles .NET-10-SDK).
2. `TreeSizeViewNG.sln` in Rider oder Visual Studio öffnen und starten.

## Herunterladen

Unter **Releases** liegen fertige Pakete für Windows (`…-windows-x64.exe`) und Linux (`…-linux-x64.tar.gz`). Sie entstehen automatisch bei jedem Push auf `main` (`.github/workflows/release.yml`): Version = Haupt-/Nebenversion aus `<Version>` im csproj plus laufende Nummer, z. B. `v1.0.0`, `v1.0.1` … Für einen Sprung auf `1.1.x` nur `<Version>` im csproj ändern.

Linux: entpacken und starten mit `tar -xzf TreeSizeViewNG-*-linux-x64.tar.gz && ./TreeSizeViewNG`.

## Windows veröffentlichen

`publish-windows.cmd` starten. Ergebnis: `publish/windows-x64/TreeSizeViewNG.exe` – eine einzelne Datei, auf dem Zielrechner muss nichts installiert sein.

Veröffentlicht wird als **ReadyToRun** (vorkompiliert, unkomprimiert). Das startete in Messungen am schnellsten – schneller als Native AOT, bei dem Avalonia/Skia länger zum Initialisieren braucht, und schneller als eine komprimierte Einzeldatei, die bei jedem Start erst entpackt wird. Preis: die exe ist ca. 136 MB groß.

## Linux veröffentlichen

```bash
chmod +x publish-linux.sh   # einmalig, falls das Ausführungsrecht fehlt
./publish-linux.sh          # Auswahl: 1 Linux x64, 2 Windows x64, 3 beide
```

Ausgabe unter `publish/linux-x64/` bzw. `publish/windows-x64/`. Dieses Skript erzeugt (noch) eine komprimierte Einzeldatei.

## Programmsymbol

`Assets/app.ico` und `Assets/Icon.png` werden von `tools/make-icon.cs` erzeugt:

```bash
dotnet run --file tools/make-icon.cs
```

## Paketversionen

Avalonia bleibt bewusst bei der neuesten **11er-Version**: TreeDataGrid ab 11.2 und alle 12.x-Versionen benötigen eine kostenpflichtige „Avalonia Accelerate“-Lizenz. TreeDataGrid 11.1.0 ist die letzte MIT-Version und läuft mit jeder Avalonia-11-Version; Avalonia 12 würde TreeDataGrid 12 voraussetzen. Material.Icons.Avalonia 2.3.1 ist die letzte Version für Avalonia 11.

## Lizenz

[MIT](LICENSE)
