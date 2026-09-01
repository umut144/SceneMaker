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

Die Werkzeugauswahl besteht aus zwei unabhängigen, typisierten Dimensionen:

- `EditorMode` bestimmt den bearbeiteten Inhaltsbereich: `Terrain`, `Props`
  oder `Templates`.
- `EditorTool` bestimmt das primäre Werkzeug innerhalb dieses Modus.

Die linke Werkzeugleiste ist mode-aware und zeigt nur die Werkzeuge des aktiven
Modus. Beispiele sind:

- `Selector`
- `Pencil`
- `Line`
- `Terrain Fill`
- Template-bezogene Werkzeuge wie `Anchor Move`

Das primäre Werkzeug beschreibt, welche Art von Aktion ausgeführt wird.
Scene-Typ (`Instance` oder `Template`) und Editor-Modus bleiben voneinander
getrennte Konzepte.

Der aktive Tool-Kontext ist immer die Kombination aus Modus und Werkzeug. Das
horizontale `ContextMenu` zeigt diese Kombination beispielsweise als
`Terrain:Line` oder `Prop:Line`. Jeder Modus merkt sich sein zuletzt
ausgewähltes Werkzeug.

Die konkrete Pointer-Interaktion gehört ebenfalls zum Tool-Kontext:

- `Terrain:Pencil` hebt das Terrain-Tile unter dem Mauszeiger hervor und malt
  beziehungsweise löscht direkt.
- `Terrain:Line` beginnt beim Drücken der linken Maustaste, zeigt während des
  Ziehens alle betroffenen Tiles und führt die Linie beim Loslassen aus.
- `Prop:Line` behält Startpunkt, Endpunkt und die Bestätigung mit Enter als
  getrennte Schritte.

Ein aktiver `Eraser` verwendet für Terrain-Highlights die Löschfarbe, damit die
Auswirkung vor dem Ausführen sichtbar ist.

### Höhe

Das ContextMenu enthält durchgehend `Height` in Metern, in Schritten von 0,1 m.
Der Wert gilt für Terrain und Props gleichermaßen: gemalte Zellen und gesetzte
Props entstehen auf dieser Höhe.

Er ist Session-Zustand, kein Dokumentfeld. Beim Öffnen einer Scene startet er
auf deren `default_elevation_meters` — dem Wert, den der Autor beim Anlegen im
Feld `Ground height` gesetzt hat. Damit findet man die Vorgabe der Scene immer
wieder vor, kann aber pro Strich davon abweichen, ohne dass das Dokument sich
ändert.

### Prop:Line

Das ContextMenu von `Prop:Line` enthält den ganzzahligen Wert
`Prop Offset` in Authoring-Pixeln. Der Wert ist nicht negativ und wird zum
berechneten Footprint-Abstand entlang der Linie addiert. `0` erzeugt das
lückenlose Standardverhalten. Der Offset beeinflusst Vorschau, Platzierung und
Line-Eraser und bleibt reiner Session-Zustand.

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

## Eingabeverarbeitung

Der Canvas entscheidet nichts Fachliches. Er rechnet Godot-Ereignisse in
Koordinaten um und reicht sie an `ToolInteraction` weiter; die beantwortet jede
Eingabe mit genau einem `ToolOutcome` — nichts, eine Meldung oder eine
Bearbeitung. `SceneMakerMain` wendet sie an, zeichnet sie in der Historie auf und
zeigt das Ergebnis in der Statuszeile.

`ToolInteraction` hält dabei den flüchtigen Zustand: den festgelegten
Linien-Startpunkt, den gerade gezogenen Template-Anchor, die aktuelle Auswahl.
Ein neues Werkzeug wird dort ergänzt, nicht als weiteres Canvas-Ereignis.

## Bearbeiten, Rückgängig und Speichern

Bearbeitungen wirken zunächst nur im Speicher. Die Szene wird in den Workspace
geschrieben, sobald 1,5 Sekunden lang nichts mehr bearbeitet wurde, außerdem vor
jedem Wechsel von Szene oder Workspace, vor Export und Template-Vorschau, beim
Beenden und auf `Cmd/Strg+S`. Die Dokumentleiste zeigt `unsaved` oder `saved`.

Jede Bearbeitung landet in einer Historie unveränderlicher Scene-Dokumente:

- `Cmd/Strg+Z` macht rückgängig, `Cmd/Strg+Shift+Z` beziehungsweise `Cmd/Strg+Y`
  stellt wieder her. Dieselben Schritte liegen als Schaltflächen rechts in der
  Dokument-Infoleiste.
- Ein zusammenhängender Zug ist genau ein Schritt: Wer den Pencil über vierzig
  Zellen zieht, nimmt ihn mit einem einzigen Rückgängig zurück. Der Zug endet
  beim Loslassen der Maustaste.
- Rückgängig verwirft die aktuelle Auswahl und eine erzeugte Template-Vorschau,
  weil beide sich auf einen Zustand beziehen können, den es nicht mehr gibt.

## Aktueller Stand

Das horizontale `ContextMenu` zeigt den Namen des aktuell ausgewählten primären
Werkzeugs als Label. Die rechte `ToolOptionsBar` enthält den `Eraser` als
Icon-Toggle. Weitere feste Optionen können dort später vertikal ergänzt werden,
ohne das primäre Werkzeugmodell zu verändern.
