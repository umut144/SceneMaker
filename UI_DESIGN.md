# SceneMaker UI Design

Dieses Dokument beschreibt die grundlegende Struktur und die Bedienlogik der
SceneMaker-Oberfläche. Es dient als gemeinsame Referenz für die weitere
Entwicklung der UI.

## Grundlayout

Die Oberfläche ist vertikal aufgebaut:

1. Obere Navigationsleiste für Workspace- und Scene-Kontext.
2. Dokument-Infoleiste mit Workspace- und Scene-Informationen.
3. Arbeitsbereich mit linker Werkzeugleiste und Canvas-Bereich.
4. Statusleiste am unteren Rand.

Im Arbeitsbereich liegt rechts neben der linken Werkzeugleiste der eigentliche
Canvas-Bereich. Oberhalb des Canvas befindet sich die `ToolOptionsBar`.

```text
┌──────────────────────────────────────────────────────────────┐
│ Navigation                                                   │
├──────────────────────────────────────────────────────────────┤
│ Workspace / Scene information                                │
├──────────────┬───────────────────────────────────────────────┤
│              │ ToolOptionsBar                                │
│ ToolBar      ├───────────────────────────────────────────────┤
│              │ Canvas                                        │
├──────────────┴───────────────────────────────────────────────┤
│ Status                                                       │
└──────────────────────────────────────────────────────────────┘
```

## Werkzeugmodell

Die linke Werkzeugleiste wählt das primäre Werkzeug aus. Beispiele sind:

- `Selector`
- `Pencil`
- `Line`
- `Terrain Fill`
- Template-bezogene Werkzeuge wie `Anchor Move`

Das primäre Werkzeug beschreibt, welche Art von Aktion ausgeführt wird.

## ToolOptionsBar

Die `ToolOptionsBar` ist eine feste Optionsleiste und kein dynamisches
`ContextMenu`. Ihre Elemente wechseln nicht abhängig von Perspective, Scene-
Typ oder Template-Modus. Sie enthält zusätzliche Bearbeitungsoptionen, die mit
dem primären Werkzeug kombiniert werden können.

Die erste Option ist der Toggle `Eraser`:

- `Pencil` + `Eraser` löscht einzelne Terrain-Zellen.
- `Line` + `Eraser` löscht entlang einer Linie.
- `Terrain Fill` + `Eraser` löscht einen zusammenhängenden Bereich.
- Ohne aktivierten `Eraser` führen diese Werkzeuge ihre normale Zeichen- oder
  Platzierungsaktion aus.

Damit wird zwischen zwei unabhängigen Dimensionen unterschieden:

- Linke Leiste: **Welche Aktion?**
- `ToolOptionsBar`: **Mit welchen Bearbeitungsoptionen?**

Weitere Optionen können später in derselben Leiste ergänzt werden, ohne das
primäre Werkzeugmodell zu verändern.

## Aktueller Prototyp

Die Leiste zeigt zunächst nur den Namen des aktuell ausgewählten primären
Werkzeugs als Label. Dieses Label dient als sichtbare Funktionsprüfung und wird
später durch die festen Tool-Optionen ergänzt.

