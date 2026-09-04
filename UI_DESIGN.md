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

Die Werkzeugauswahl besteht aus drei unabhängigen Dimensionen:

- `EditorMode` bestimmt den **Bereich**: `Terrain`, `River`, `Mountain`,
  `Placements` oder `Templates`.
- `EditorTool` bestimmt das primäre Werkzeug innerhalb dieses Bereichs.
- Das gewählte Terrain-Asset bestimmt das **Material** und sonst nichts.

Die Navigation ist zweistufig, wie bei den übrigen Bereichen auch:

```text
Übersicht:  [ Terrain ] [ Landscape ] [ Placements ] [ Scene Templates ] [ Map ]
                              │
                              ▼
Kontext:    [ ← ]  Landscape ›  ( River )  ( Mountain )
```

Das Material erscheint dort nicht noch einmal. Ein Bereich, der Zellen malt,
trägt es als **Palette** — eine Leiste von Assetknöpfen, zwischen denen man
beim Malen wechselt. Ein Bereich, der eine Kurve zeichnet, trägt es als
**Eigenschaft des Körpers**, den man gerade zieht, und damit in der
Kontextleiste seines Werkzeugs neben Breite und Höhen:

```text
Terrain-Assetleiste:  Terrain ›  ( Grass ) ( Sand )
River-Kontext:        Surface [ Water ▾ ] · Point [ Linear ▾ ] · Width […]
                      · Surface level […] · Snap · Depth […] · Clearance […]
```

`Landscape` ist der Weg hinein, kein Ort: ein Klick öffnet die Kontextleiste und
landet direkt in einem der beiden Bereiche — in dem, in dem man zuletzt war —,
der andere ist einen Klick daneben. `Landscape` ist deshalb **kein**
`EditorMode`; es gibt keinen Zustand „irgendwo in Landscape", der beantworten
müsste, was Zeichnen dort bedeutet.

**Der Bereich entscheidet über die Assets, nicht das Asset über das Werkzeug.**
Ob ein Terrain-Asset gemalt oder gezeichnet wird, steht weiterhin in der
Workspace-Konfiguration (`authoring: "cells" | "curve"`), nicht in seinem
Surface-Token — aber gefragt wird es jetzt von der Assetleiste:

| Bereich | Werkzeuge | angebotene Assets |
| --- | --- | --- |
| `Terrain` | `Pencil`, `Line`, `Terrain Fill` | `authoring: "cells"`, als Palette |
| `River` | `Draw River` | `authoring: "curve"`, als `Surface`-Feld |
| `Mountain` | `Draw Mountain` | — |
| `Placements` | `Selector`, `Pencil`, `Line` | — |
| `Templates` | `Selector`, `Anchor Move` | — |

`Mountain` bietet keine Assets an, weil ein Bergkörper keines trägt: er
bestimmt Form und Höhe, das Material bleibt Sache des gemalten Terrains. Wer
die Oberfläche eines Berges ändern will, malt sie im Bereich `Terrain` — dort
etwa Sand über das Grass des Bergrückens. Jeder Bereich merkt sich sein zuletzt
gewähltes Asset getrennt, damit man den Pinsel dort wiederfindet, wo man ihn
abgelegt hat.

Beim Betreten eines Bereichs bleibt das gemerkte Asset, wenn es zum Bereich
passt; sonst wird das erste passende gewählt. Bietet ein Workspace für einen
Bereich gar kein passendes Asset an, bleibt der Bereich erreichbar, sein
Zeichenwerkzeug ist aber deaktiviert und die Statuszeile sagt, welcher Assettyp
fehlt. Ein inkompatibles Asset wird nie gewählt, und ein toter aktiver
Zeichenknopf wird nie angeboten. Das gilt nur für Bereiche, die überhaupt ein
Material brauchen: `Mountain` bietet keines an und ist deshalb nie aus diesem
Grund deaktiviert.

Die angebotenen Namen und Rollen kommen aus SceneMakers Workspace-Konfiguration,
nicht aus PolyTools. Ein Asset kann deshalb etwa `Water` heißen und die Rolle
`Terrain` tragen, selbst wenn ein gleichnamiger PolyTools-Schlüssel anders
benannt oder klassifiziert ist. Nur ein Placement benötigt unter demselben
`asset_key` eine importierte Geometrie für Footprint und Anchor.

Ein Assetwechsel **innerhalb** eines Bereichs wechselt nur das Material — ob
über die Palette oder über das `Surface`-Feld: die Geometrie eines laufenden
Entwurfs bleibt stehen, und kein Werkzeug wird unter der Hand getauscht. Das
ersetzt die frühere automatische Werkzeugumschaltung, die einen halb
gezeichneten Berg verschluckte, sobald man ein Curve-Asset wählte.

Die Assetleisten oben zeigen jedes Asset in seiner eigenen Farbe. Die Auswahl
liegt deshalb auf dem **Hintergrund** — eine gefüllte Fläche in der Assetfarbe
mit Rahmen —, nicht auf der Schrift: Godots Standard-Theme färbt die Schrift
eines gedrückten Knopfes weiß, und damit wäre ausgerechnet das gewählte Asset
das einzige, dessen Farbe man nicht mehr sieht.

Das primäre Werkzeug beschreibt, welche Art von Aktion ausgeführt wird.
Scene-Typ (`Instance` oder `Template`) und Editor-Modus bleiben voneinander
getrennte Konzepte.

Der aktive Tool-Kontext ist immer die Kombination aus Modus und Werkzeug. Das
horizontale `ContextMenu` zeigt diese Kombination beispielsweise als
`Terrain:Line` oder `Placement:Line`. Jeder Modus merkt sich sein zuletzt
ausgewähltes Werkzeug. `Placement` ist dabei die Sprache der Oberfläche; der
interne Modus `EditorMode.Props` und das persistierte Feld `props` bleiben bis
zu einer bewusst geplanten Schemaänderung unverändert.

Die konkrete Pointer-Interaktion gehört ebenfalls zum Tool-Kontext:

- `Terrain:Pencil` hebt das Terrain-Tile unter dem Mauszeiger hervor und malt
  beziehungsweise löscht direkt.
- `Terrain:Line` beginnt beim Drücken der linken Maustaste, zeigt während des
  Ziehens alle betroffenen Tiles und führt die Linie beim Loslassen aus.
- `Placement:Line` behält Startpunkt, Endpunkt und die Bestätigung mit Enter als
  getrennte Schritte.
- `River:Draw River` zeichnet eine offene Bezier-Kurve: Drücken legt einen
  Kurvenpunkt auf dem Wasserraster fest, Ziehen zieht sein Handle heraus,
  Loslassen setzt ihn. Enter schließt den Fluss ab, Escape nimmt Punkt für Punkt
  zurück.
- `Mountain:Draw Mountain` zeichnet eine geschlossene Bezier-Kontur auf dem
  Terrainraster. Enter schließt und authoriert den ganzen Berg; Escape oder
  Rückgängig nimmt während des Entwurfs jeweils den letzten Punkt zurück.

Ein aktiver `Eraser` verwendet für Terrain-Highlights die Löschfarbe, damit die
Auswirkung vor dem Ausführen sichtbar ist.

### Höhe

Das ContextMenu enthält durchgehend `Height` in Metern. Seine Schrittweite ist
`elevation_quantum_meters` aus dem offenen Workspace; direkt eingegebene Werte
werden ebenfalls darauf gerundet. Der Wert gilt für Terrain und Placements
gleichermaßen: gemalte Zellen und gesetzte Placements entstehen auf dieser Höhe.

Ohne offenen Workspace sind `Height`, `Water` und das `Ground height` des
Scene-Dialogs deaktiviert, weil es ohne Workspace kein gültiges Höhenquantum
gibt. Beim Workspace-Wechsel werden Schrittweite und sichtbarer Wert aller drei
Eingaben neu gesetzt, auch wenn eine davon im aktuellen Werkzeug verborgen ist.

Er ist Session-Zustand, kein Dokumentfeld. Beim Öffnen einer Scene startet er
auf deren `default_elevation_meters` — dem Wert, den der Autor beim Anlegen im
Feld `Ground height` gesetzt hat. Damit findet man die Vorgabe der Scene immer
wieder vor, kann aber pro Strich davon abweichen, ohne dass das Dokument sich
ändert.

### River:Draw River

Das ContextMenu von `River:Draw River` enthält Material, Grundriss und Schnitt.

`Surface` wählt das Terrain-Asset, aus dem der Korridor besteht — eines der
curve-authorierten Assets des Workspace. Es steht hier und nicht in einer
Assetleiste, weil es eine Eigenschaft des gezogenen Körpers ist wie `Width`:
man wählt es einmal für diesen Fluss, nicht ständig wie einen Pinsel. Ein
Wechsel mitten im Entwurf behält die Kurve und bestimmt nur, was der fertige
Körper trägt. Bietet der Workspace genau ein passendes Asset an, bleibt das Feld
sichtbar, zeigt dieses Asset und lässt sich nicht aufklappen; bietet er keines
an, bleibt es ebenso sichtbar und leer, und die Statuszeile sagt, welcher
Assettyp fehlt. Intern ist das eine Wahl des `asset_key`; das Runtime-`surface`
eines Assets liest SceneMaker dabei nie.

Ein Fluss ist deshalb auch nicht auf Wasser festgelegt. Was durch einen Korridor
fließt — Wasser, Lava, Schlamm, Treibsand —, entscheidet das gewählte Asset und
sein Profil in der Runtime; `Surface`, `Surface level`, `Depth`, `Clearance` und
`Snap` bleiben materialneutral. Die persistierten Typen heißen weiterhin
`water_bodies` und `water_raster`; das ist Dokument- und Exportformat und wird
getrennt betrachtet.

`Point` schaltet
zwischen `Linear` und `Aligned` um: `Linear` macht die angrenzenden Segmente
gerade, `Aligned` hält die beiden Handles eines Punktes kollinear. Fluss und Berg
teilen sich dieses eine Bedienelement, aber nicht seinen Wert: jedes Werkzeug
merkt sich seinen eigenen Punktmodus, und der Dropdown zeigt den des aktiven. Wie beim
Bezier-Werkzeug von PolyTools ist das Sitzungszustand und gilt für den *nächsten*
Punkt — es lässt sich also mitten im Zeichnen umschalten und rührt die bereits
gesetzten Punkte nicht an. `Width` ist die Breite des Korridors am nächsten
Punkt, in Schritten einer Wasserzelle. Wie das vertikale Profil wird sie
zwischen gesetzten Punkten über die Bogenlänge interpoliert.

Handles folgen dem Zeiger ungerastert. Ein Handle ist eine Kurvensteuerung und
kein Ort; würde es einrasten, rastete die Form der Kurve mit ein. Ein Zug unter
einer halben Wasserzelle zählt als Klick — ein `Aligned`-Punkt, den man nur
anklickt, bekommt seine Handles automatisch aus seinen Nachbarn.

Der erste Punkt ist die Quelle, der letzte die Mündung; mehr sagt das Dokument
über die Fließrichtung nicht.

Der Schnitt steht daneben: `Surface level` ist die Oberkante des Korridors,
`Depth` die Tiefe des Bettes darunter, `Clearance` die Kopfhöhe darüber, die aus
dem Terrain ausgeschnitten wird. Alle drei gelten für den *nächsten* Punkt und
werden zwischen gesetzten Punkten über die Bogenlänge interpoliert.

`Snap` steht direkt neben `Surface level`, weil er dessen Wert bestimmt: er ist
standardmäßig an und nimmt die Oberflächenhöhe aus dem Terrain unter dem
gesetzten Punkt — ein Fluss folgt damit seinem Tal, ohne dass man eine Zahl
tippt. Für einen Tunnel schaltet man ihn ab, denn dort ist der ganze Sinn, dass
das Wasser dem Berg gerade *nicht* folgt. Gespeichert wird auch dann nur die
Zahl: es gibt keinen Terrain-Bezug im Dokument, ein später umgemaltes Gelände
verschiebt also keinen Fluss. Liegt unter einem Punkt kein Terrain, erbt er die
Höhe seines Vorgängers, und die Statuszeile sagt es.

`Height` ist bei `Draw River` deshalb ausgeblendet: es authoriert Terrain und
Placements, und Wasser bringt seine eigenen drei Werte mit.

Der fertige Fluss ist **ein** Bearbeitungsschritt: ein Rückgängig nimmt danach
den ganzen Fluss zurück, nicht seinen letzten Punkt. Während des Zeichnens gilt
die Entwurfsregel weiter unten — da nimmt Rückgängig Punkt für Punkt zurück. `Draw River` + `Eraser` löscht den ganzen
Body unter dem Zeiger. Einzelne Zellen sind nicht radierbar, weil sie aus der
Kurve abgeleitet sind.

Ein Scene Template kann kein Wasser tragen; das Werkzeug sagt das beim ersten
Klick, statt es beim Speichern scheitern zu lassen.

### Mountain:Draw Mountain

`Draw Mountain` braucht kein Asset und ist deshalb immer verfügbar. Jeder Klick
setzt einen Konturpunkt auf das Terrainraster; `Point` schaltet wie beim Fluss
zwischen geraden Kanten und `Aligned`-Bezierpunkten. Die Kontur ist zyklisch:
auch der erste und der letzte Punkt sind Nachbarn, und automatische Handles
formen deshalb die Schließkante genauso wie jede andere Kante.

Die Vorschau kennt genau drei Zustände, und sie beantworten dieselbe Frage:
Was passiert, wenn ich jetzt Enter drücke?

- **Unfertig** — cyan, nur Umriss und Punkte, keine Füllung. Es sind noch keine
  drei Punkte gesetzt. Das ist kein Fehler, sondern der normale Zustand einer
  Kontur, die gerade entsteht; die Statuszeile sagt, was noch fehlt.
- **Bereit** — gelber Umriss und genau die Terrainzellen, die dieser Berg
  anheben würde, jede in der Farbe des dort gemalten Assets. Der Berg färbt
  nichts um: eine Kontur über Sand und Grass zeigt Sand und Grass. Gezeigt wird
  der Unterschied, den Enter macht, nicht die überdeckte Fläche — unbemalte
  Zellen und Zellen, die ein höherer Berg schon hält, bleiben leer. Gelb ist
  eine Zusage: geprüft sind die Höhe auf dem Workspace-Quantum, die Kontur
  selbst und die Faltung mit den vorhandenen Bergen.
- **Blockiert** — roter Umriss, keine Füllung. Enter würde scheitern, und der
  Grund steht in der Vorschau und in der Statuszeile. Blockiert ist eine Frage
  der Geometrie und der Höhe: Selbstkontakt, eine Kontur ohne Fläche, eine Höhe
  außerhalb des Quantums.

Eine gültige Kontur, die nichts anhebt, ist **nicht** blockiert. Sie bleibt
bereit, ihre Füllung ist leer, und Vorschau wie Statuszeile sagen es aus:
`Mountain is valid but currently raises no Terrain cells.` So bleibt die
Reihenfolge „erst Kontur, dann Terrain" möglich, ohne dass eine leere Vorschau
wie ein kaputtes Werkzeug aussieht.

Vorschau und Enter fragen dieselbe Core-Operation, nicht zwei ähnliche. Eine
gelbe Kontur kann deshalb nicht abgelehnt und eine rote nicht angenommen werden,
und beide nennen denselben Grund.

Das `Height`-Feld ist die absolute Oberkante des ganzen Bergkörpers. Ein fertiger
Berg ist ein Bearbeitungsschritt.

Mit eingeschaltetem `Eraser` hebt schon das Überfahren den **ganzen** Bergkörper
hervor, den ein Klick entfernen würde: seine Kontur plus alle Terrainzellen, die
er gerade anhebt, in der Löschfarbe — nicht nur die Zelle unter dem Zeiger, aber
auch nicht jede überdeckte Zelle. Gezeigt wird, was tatsächlich absinkt; eine
Zelle, die ein anderer Berg höher hält oder die niemand gemalt hat, sinkt nicht.
Deshalb bleibt die Kontur auch dann sichtbar, wenn der Körper nichts anhebt.
Getroffen wird über die Terrainzelle unter dem Zeiger, nicht über die
mathematische Zeigerposition — sonst könnten an einer Randzelle die gefüllte
Zelle und der getroffene Körper um eine halbe Zelle auseinanderliegen. Liegen
mehrere Berge übereinander, gewinnt der höchste sichtbare; ein Klick entfernt
genau diesen einen ganzen Körper.

### Fertige Bergkörper auf dem Canvas

Jeder gespeicherte Bergkörper trägt eine dauerhaft sichtbare geschlossene
Kontur in einer eigenen Farbe. Die Fläche bleibt, was die Faltung ergibt — das
gemalte Terrain-Asset in seiner eigenen Farbe —, damit das Oberflächenmaterial
lesbar bleibt; die Kontur sagt, **wo ein Körper aufhört**, und genau das können
gefaltete Zellen nicht: zwei Berge über demselben gemalten Asset sind ohne sie
eine einzige Fläche. Die Höhenansicht war bisher der einzige Weg, ihre Grenzen
zu erraten; sie bleibt eine Höhenanalyse und ist dafür nicht mehr nötig, weshalb
die Kontur unabhängig davon gezeichnet wird, ob die Ansicht an ist.

Die Farbe stammt aus einer kleinen festen Editorpalette und wird stabil aus der
`mountain_body_id` abgeleitet: aufeinanderfolgend gezeichnete Berge bekommen
verschiedene Farben, und derselbe Berg hat nach einem Neustart dieselbe. Sie ist
reine Editorfarbe — sie steht nicht im Dokument, nicht im Export und bedeutet im
Spiel nichts. In `Terrain` und `Mountain` ist die Kontur voll sichtbar — im Terrain-Bereich
ist sie das einzige unmittelbare Signal, dass dort ein authorierter Bergkörper
liegt. Der `Map`-Kontext ist eine strukturelle Übersicht und zeigt sie ebenfalls
voll. In `River`, `Placements` und `Templates` wird sie wie alles Bereichsfremde
gedimmt.

Ein begonnener Entwurf verschwindet nie stillschweigend. Ein Bereichswechsel,
ein Werkzeugwechsel und das Einschalten des `Eraser` verwerfen den Entwurf — und
sagen in der Statuszeile, wie viele Punkte dabei verloren gingen. Für Fluss und
Berg gilt dieselbe Eraser-Regel: kein Entwurf bleibt im Hintergrund erhalten,
während radiert wird. Das Ausschalten des `Eraser` beginnt keinen neuen Entwurf,
und ein Assetwechsel verwirft nichts.

Die Canvasdarstellung verwendet bereits das gefaltete Höhenfeld aus gemalten
Zellen und Bergkörpern. Dadurch sieht der Autor genau die Terrainzellen, die
auch der Export erhält.

### Placements und Untergrund

Ein Placement braucht kein Terrain unter sich. Seine Höhe ist absolut, also
steht schon fest, wo es ist; der Boden darunter ist eine eigene Tatsache und
keine Bedingung. Ein frei stehendes Placement wird deshalb weder markiert noch
gewarnt noch blockiert: die Vorschau ist normal gültig, das fertige Placement
wird wie jedes andere gezeichnet, und Radieren von Terrain oder eines
Bergkörpers unter ihm ändert an ihm nichts. Blockiert wird weiterhin nur, was
die Geometrie allein entscheiden kann — ein Footprint außerhalb der Scene und
ein Footprint, der ein anderes Placement berührt.

### Placement:Line

Das ContextMenu von `Placement:Line` enthält den ganzzahligen Wert
`Placement Offset` in Authoring-Pixeln. Der Wert ist nicht negativ und wird zum
berechneten Footprint-Abstand entlang der Linie addiert. `0` erzeugt das
lückenlose Standardverhalten. Der Offset beeinflusst Vorschau, Platzierung und
Line-Eraser und bleibt reiner Session-Zustand.

## ToolOptionsBar

Die rechte Leiste trennt zwei Sorten Schalter durch einen Separator. Oberhalb
steht, was die Werkzeuge *anders arbeiten* lässt — derzeit der `Eraser`.
Unterhalb steht, was die Karte *anders aussehen* lässt.

Dort sitzt die Höhenansicht (`m`). Sie färbt Terrain, Placements und Wasser nach ihrer
Höhe statt nach ihrem Asset, mit einem einzigen Blauton von dunkel nach hell:
tiefer Grund tritt zum Hintergrund zurück, hoher Grund hebt sich ab. Wasser hat
drei relevante Grenzen; die Auswahl unter dem aktivierten Schalter zeigt
wahlweise `Surface`, `Bed` oder `Cut top`. Terrain und Placements behalten dabei ihre
eigene Elevation, sodass alles auf derselben Skala vergleichbar bleibt. Die
Skala spannt sich über die tatsächlich vorkommenden Höhen der Scene, und eine
Legende oben rechts auf der Canvas nennt Auswahl und beide Enden — ohne sie
wären die Farben bedeutungslos. Eine Scene mit nur einer Höhe sagt das statt
eine Spanne zu zeigen.

Die Höhenansicht ist eine Sicht, kein Modus: Zeichnen, Platzieren, Radieren und
Auswählen funktionieren unverändert weiter.

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
- Solange ein Werkzeug einen **unfertigen Entwurf** hält — ein Fluss im
  Zeichnen, eine Placement-Linie mit festgelegtem Startpunkt — gehören beide Tasten
  dem Entwurf. Rückgängig nimmt den zuletzt gesetzten Punkt zurück, genau wie
  Escape; Wiederherstellen hat dort nichts zurückzugeben und sagt das. Erst wenn
  der Entwurf leer ist, greifen beide wieder auf die Historie zu. Andernfalls
  würde ein Rückgängig mitten im Zeichnen eine längst abgeschlossene Bearbeitung
  zurücknehmen und den halben Fluss stehen lassen.
- Wird ein Entwurf durch einen Werkzeug-, Perspektiv- oder Eraserwechsel
  verworfen, sagt die Statuszeile das ausdrücklich. Die Entscheidung darüber
  liegt in `ToolInteraction`; die Godot-Oberfläche zeigt nur das Ergebnis.
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
