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
Canvas-Bereich. Oberhalb des Canvas befindet sich das horizontale `ContextMenu`.
Unterhalb davon liegt die schmale, vertikale `ToolOptionsBar` rechts neben dem
Canvas und damit gegenüber der linken `ToolBar`.

```text
┌──────────────────────────────────────────────────────────────┐
│ Navigation                                                   │
├──────────────────────────────────────────────────────────────┤
│ Workspace / Scene information                                │
├──────────────┬───────────────────────────────────────────────┤
│              │ ContextMenu                                   │
│ ToolBar      ├───────────────────────────────────────┬───────┤
│              │ Canvas                                │ Tool- │
│              │                                       │Options│
├──────────────┴───────────────────────────────────────┴───────┤
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

Die `ToolOptionsBar` ist eine feste, vertikale Optionsleiste rechts neben dem
Canvas und kein dynamisches `ContextMenu`. Ihre Elemente wechseln nicht
abhängig von Perspective, Scene-Typ oder Template-Modus. Sie enthält zusätzliche
Bearbeitungsoptionen, die mit dem primären Werkzeug kombiniert werden können.

Die erste Option ist der Toggle `Eraser`. Er ist ein unabhängiger
Bearbeitungszustand und kein eigenes primäres Werkzeug:

- `Pencil` + `Eraser` löscht einzelne Terrain-Zellen.
- `Line` + `Eraser` löscht entlang einer Linie.
- `Terrain Fill` + `Eraser` löscht einen zusammenhängenden Bereich.
- Ohne aktivierten `Eraser` führen diese Werkzeuge ihre normale Zeichen- oder
  Platzierungsaktion aus.

Der Toggle bleibt auch bei Werkzeugen sichtbar, für die er keine Wirkung hat,
beispielsweise `Selector` oder Template-Werkzeuge. Die Optionsleiste bleibt
dadurch statisch und vorhersehbar.

Damit wird zwischen zwei unabhängigen Dimensionen unterschieden:

- Linke Leiste: **Welche Aktion?**
- `ToolOptionsBar`: **Mit welchen Bearbeitungsoptionen?**

Weitere Optionen können später in derselben Leiste ergänzt werden, ohne das
primäre Werkzeugmodell zu verändern.

## Aktueller Stand

Das horizontale `ContextMenu` zeigt den Namen des aktuell ausgewählten primären
Werkzeugs als Label. Die rechte `ToolOptionsBar` enthält den `Eraser` als
Icon-Toggle. Weitere feste Optionen können dort später vertikal ergänzt werden,
ohne das primäre Werkzeugmodell zu verändern.
