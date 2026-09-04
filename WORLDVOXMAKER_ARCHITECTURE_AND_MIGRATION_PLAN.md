# WorldVoxMaker: Architektur- und Migrationsplan

Status: Diskussionsvorlage, keine Implementierungsfreigabe  
Ausgangsbasis: SceneMaker, Scene-Schema 14, Workspace-Schema 8, Export-Schema 9  
Zweck: Vorlage für eine unabhängige Architekturprüfung, insbesondere durch Claude

## 1. Kurzfassung

WorldVoxMaker soll SceneMaker nicht durch einen 3D-Editor ersetzen. Die vertraute
2D-Draufsicht bleibt die primäre Authoring-Oberfläche. Geändert wird zuerst das
Weltmodell darunter: Aus einer Sammlung aus Terrain-Höhenfeld, Water-Kurven,
Route-Surfaces und Elevation-Regions wird ein echtes volumetrisches Voxelmodell.

Die zentrale UX für dieses Modell ist ein intelligenter horizontaler Schnitt:
Der 2D-Canvas zeigt die Arbeitsebene auf der aktuellen Elevation, den dort
vorhandenen oder entfernten Raum und einen unterscheidbaren Umriss der Geometrie
oberhalb der Schnittebene. Beim Zeichnen eines ansteigenden Pfads folgt die
Schnittebene der Höhe des aktuellen Pfadpunkts. So lassen sich Tunnel,
Unterführungen und mehrere Ebenen weiterhin in einer lesbaren 2D-Ansicht
authorisieren.

Eine 3D-LookDev-Ansicht ist ein späterer, zunächst rein lesender Verbraucher
derselben Voxel-Daten. Sie gehört ausdrücklich nicht in die erste Migration und
ist kein Ersatz für den 2D-Canvas.

Die Migration erfolgt in kleinen, testbaren Schnitten. Das alte und das neue
Geometriemodell dürfen nicht dauerhaft als zwei schreibbare Wahrheiten
nebeneinander existieren. Persistenz und Workspace-Daten werden erst umgestellt,
wenn das neue Modell, die 2D-Projektion und mindestens das Tile-Werkzeug zusammen
als vollständiger vertikaler Schnitt funktionieren.

## 2. Produktziele

WorldVoxMaker soll:

- echte Volumina mit Tunneln, Überhängen, Unterführungen und mehreren begehbaren
  Höhen an derselben horizontalen Position darstellen;
- weiterhin hauptsächlich aus der 2D-Draufsicht bedient werden;
- alle geometrischen Maße in Metern ausdrücken;
- 1 m × 1 m × 1 m große Voxel als kleinste persistierte Volumeneinheit nutzen;
- für Kurven, Kontrollpunkte und Formgrenzen ein 0,2-m-Unterraster anbieten;
- additive und subtraktive Formoperationen einheitlich behandeln;
- Tile, Hill und Path als die drei primären Authoring-Werkzeuge anbieten;
- Materialidentität weiterhin über stabile `asset_key`s aus der
  Workspace-Konfiguration beziehen;
- bestehende Templates, Anchors und Export-Verantwortlichkeiten in das neue
  Modell überführen;
- unterschiedliche Exporte aus derselben kanonischen Voxelwelt ableiten können.

## 3. Nicht-Ziele der ersten Migration

Die folgenden Punkte gehören nicht in den ersten Umbau:

- ein 3D-Authoring-Viewport;
- ein Austausch des bestehenden Godot-Layouts durch eine 3D-Oberfläche;
- Textur-, Licht- oder Kamera-LookDev;
- frei skalierbare Microvoxel oder persistierte 0,2-m-Voxel;
- eine Echtzeit-Mesh-Bearbeitung als zweite Authoring-Repräsentation;
- game-spezifische Abhängigkeiten in Core, Editor oder App;
- gleichzeitiges Bearbeiten desselben Inhalts im alten und neuen Geometriemodell;
- ein stiller Import unbekannter älterer Schema-Versionen.

## 4. Unveränderliche Leitplanken

### 4.1 Die 2D-Oberfläche bleibt das Authoring-Produkt

`SceneCanvas` bleibt ein Godot-`Control` mit Draufsicht, Pan und Zoom. Der Canvas
zeichnet eine Projektion der Welt auf die horizontale X/Z-Ebene. Bildschirm- und
Canvas-Y sind dabei Darstellungsachsen; die vertikale Weltachse wird im Modell
als Y bezeichnet und nicht mit der zweiten Canvas-Koordinate vermischt.

Navigation, Workspace-/Scene-Kontext, Werkzeugleiste, Kontextleiste,
Asset-Palette, Statuszeile und bestehende Eingabekonventionen bleiben
wiedererkennbar. Die Umstellung darf sich für den Autor wie ein Ausbau des
SceneMaker anfühlen, nicht wie ein anderes Programm.

### 4.2 Die bestehende Projektschichtung bleibt erhalten

| Projekt | Verantwortung im Zielbild |
| --- | --- |
| `SceneMaker.Core` | Voxelmodell, Validierung, Rasterisierung von Formen, Editing-Operationen, Persistenz, Templates, Exporte |
| `SceneMaker.Editor` | Werkzeugzustände, Schnittansicht, 2D-Projektion, Vorschauen, Hit-Tests, Undo-Historie |
| `SceneMaker.App` | Godot-Layout, Zeichnen, Controls, Dialoge und Weiterleitung von Eingaben |
| `SceneMaker.Cli` | Headless-Validierung und Auswahl eines Export-Adapters |

Persistierte Modelle bleiben engine-neutral. Godot-Klassen dürfen nicht in Core
oder Editor eindringen.

### 4.3 Der bestehende Eingabepfad bleibt der einzige

`SceneCanvas` übersetzt Godot-Ereignisse in logische Koordinaten und übergibt sie
an `ToolInteraction`. Pro Eingabe entsteht genau ein `ToolOutcome`.
`EditorController` wendet die resultierende Scene-Operation an und verwaltet
Undo, Workspace, Scene und Reports. Die Voxelwerkzeuge dürfen keine parallelen
Event-Handler oder eigene Undo-Pfade in der App erhalten.

### 4.4 Eine kanonische Authoring-Wahrheit

Nach dem Schema-Cut ist das Voxelmodell die einzige persistierte geometrische
Wahrheit für Terrainvolumen. Höhenfeld-Zellen, Water-Bodies, Route-Surfaces und
Elevation-Regions dürfen dann nicht als parallel editierbare Geometrie
weiterleben.

Ableitungen wie Schnittbilder, Meshes, Höhenkarten, Kollisionsdaten und
komprimierte Runtime-Voxel sind jederzeit neu erzeugbare Projektionen oder
Exports. Sie werden nicht zurück in das Authoring-Modell synchronisiert.

## 5. Koordinaten und Auflösung

### 5.1 Weltkoordinaten

- X: horizontal, links/rechts in der 2D-Draufsicht;
- Y: vertikale Höhe;
- Z: horizontal, unten/oben in der 2D-Draufsicht;
- ein Voxelindex `(x, y, z)` bezeichnet einen geschlossenen 1-m-Würfel im
  halboffenen Raum `[x,x+1) × [y,y+1) × [z,z+1)` Meter;
- die Grundebene muss eindeutig spezifiziert werden. Vorgeschlagen ist:
  `y = 0` bezeichnet den Voxel von 0 m bis 1 m, dessen Oberseite auf 1 m liegt.

Der letzte Punkt ist eine bewusste Abweichung von der umgangssprachlichen
Formulierung „Boden bei Y=0“. Vor der Implementierung muss entschieden werden,
ob Y=0 die Unterseite, die Oberseite oder der Mittelpunkt des ersten Bodenvoxels
meint. Ohne diese Festlegung werden Path-Elevation, Export und Schnittansicht
unvermeidlich um einen Meter gegeneinander verschoben.

### 5.2 0,2-m-Unterraster

Vorgeschlagene Semantik:

- persistierte Belegung bleibt auf 1-m-Voxeln;
- das 0,2-m-Raster quantisiert horizontale Kontrollpunkte, Breiten,
  Formgrenzen und optional vertikale Authoring-Werte;
- ein Form-Rasterizer entscheidet deterministisch, welche 1-m-Voxel von der
  feineren Form erfasst werden;
- das Unterraster erzeugt keine 125 Untervoxel pro Voxel.

Diese Semantik muss als Produktentscheidung bestätigt werden. Ein echtes
0,2-m-Volumenraster hätte 125-mal so viele Zellen und wäre ein grundsätzlich
anderes Daten-, Performance- und Exportmodell.

### 5.3 Workspace-Metriken

Die Workspace-Konfiguration bleibt Eigentümerin aller Metriken. Vorgeschlagen
werden explizite Felder wie:

- `voxel_size_meters`, für die erste Version zwingend `1.0`;
- `authoring_subgrid_meters`, zunächst `0.2`;
- `authoring_pixels_per_meter`, ausschließlich Darstellungsmaßstab;
- `game_pixels_per_meter`, weiterhin Export-/Consumer-Metrik.

`terrain_cell_meters`, `water_cell_meters` und `elevation_quantum_meters` werden
erst mit dem finalen Schema-Cut entfernt. Es gibt keine versteckten Defaults.

## 6. Kanonisches Voxelmodell

### 6.1 Minimales Zellmodell

Ein belegtes Voxel benötigt mindestens:

- ganzzahlige Koordinate `(x, y, z)`;
- `asset_key` als Material-/Surface-Identität.

Leere Zellen sind implizit und werden nicht gespeichert. Surface-Tokens bleiben
Eigenschaft des Workspace-Assets, nicht der einzelnen Zelle. Zusätzliche
game-spezifische Flags gehören nicht in das Voxel.

### 6.2 Sparse, gechunkte Speicherung

Empfohlen ist ein sparse Chunk-Store:

- die Welt wird logisch in kubische Chunks zerlegt;
- ausschließlich nichtleere Chunks und nichtleere Voxel werden gehalten;
- negative Koordinaten werden mittels mathematischer Floor-Division eindeutig
  einem Chunk zugeordnet;
- Iteration und Serialisierung erfolgen in kanonischer Reihenfolge;
- alle Mutationen liefern ein neues kanonisches Dokument beziehungsweise eine
  neue kanonische Chunk-Struktur.

Die Chunk-Kantenlänge ist eine Performanceentscheidung, keine sichtbare
Workspace-Eigenschaft. 16³ und 32³ sind Kandidaten; gewählt wird sie erst nach
Messungen mit realistischen Scene-Größen, Undo-Verläufen und Schnittabfragen.

### 6.3 Grenzen

Vor der Schema-Festlegung sind folgende Regeln zu entscheiden:

- Darf Y negativ sein, etwa für Keller unter der nominalen Grundebene?
- Sind X und Z weiterhin durch die Scene-Größe begrenzt?
- Gibt es eine feste oder Workspace-konfigurierte vertikale Grenze?
- Was geschieht, wenn ein Primitive teilweise außerhalb der Scene liegt:
  Abschneiden, Ablehnen oder explizite Erweiterung?

Empfehlung: X/Z bleiben in Version 1 durch die Scene begrenzt, Y darf innerhalb
expliziter Workspace-Grenzen negativ und positiv sein. Operationen werden an
den Grenzen deterministisch abgeschnitten und berichten die Zahl verworfener
Voxel. Stilles Integer-Overflow ist immer ein Fehler.

### 6.4 Baked Grid oder editierbare Formobjekte

Dies ist die wichtigste noch offene Architekturentscheidung.

**Variante A – Voxelgrid als direkt bearbeitete Wahrheit:** Tile, Hill und Path
rasterisieren beim Commit unmittelbar in das Grid. Kurvenpunkte bleiben nur
während des Entwurfs editierbar. Das Modell ist einfach, additive und
subtraktive Überlagerungen sind eindeutig und Export/Undo arbeiten auf genau
einem Zustand. Ein fertiger Path kann später jedoch nicht mehr durch Verschieben
seiner Kontrollpunkte geändert werden.

**Variante B – Operationen als Wahrheit:** Die Scene speichert eine geordnete
CSG-/Werkzeugliste; das Voxelgrid ist ein daraus berechneter Cache. Pfade und
Hügel bleiben editierbar, aber Reihenfolge, Materialvererbung, partielle
Neuberechnung, stabile IDs, Cache-Invalidierung und Undo werden wesentlich
komplexer.

Empfehlung für die erste belastbare Version ist Variante A. Sie entspricht der
Anforderung „Voxel-Grid als zentrale Datenstruktur“, vermeidet zwei Wahrheiten
und ermöglicht einen kleinen Migrationsschnitt. Nicht-destruktiv editierbare
Formobjekte sollten nur nach einer eigenen Produktentscheidung eingeführt
werden. Falls dauerhaft editierbare Pfade bereits für Version 1 zwingend sind,
muss stattdessen Variante B vor jeder Persistenzarbeit vollständig spezifiziert
werden; ein Mischmodell ist zu vermeiden.

## 7. Gemeinsame Operationen und Primitive

Alle Formen sollten auf eine kleine, engine-neutrale Schnittstelle abgebildet
werden:

1. Eine Form bestimmt für einen begrenzten Bereich eine Menge von Voxelindizes.
2. Additiv schreibt sie belegte Zellen mit einer definierten Materialregel.
3. Subtraktiv entfernt sie belegte Zellen unabhängig vom Material.
4. Vorschau und Commit verwenden exakt denselben Rasterizer.
5. Der Commit ist eine pure `SceneDocument -> SceneDocument`-Operation.

Die Primitive-Bibliothek umfasst zunächst Quader, Treppe, Hügelvolumen,
Turm/Zylinder, Pyramide und approximierte Kugel. Sie soll Geometrie liefern,
nicht UI-Zustand, Godot-Meshes oder Dokumentpersistenz besitzen.

Für jedes Primitive werden festgeschrieben:

- Bezugspunkt und Achsenorientierung;
- inklusive/exklusive Begrenzungen;
- Rundungsregel vom 0,2-m-Formraum auf 1-m-Voxel;
- Verhalten bei geraden Grenzberührungen;
- Materialregel im additiven Modus;
- Clipping und leerer Output;
- deterministische Reihenfolge der resultierenden Koordinaten.

## 8. Authoring-Werkzeuge

### 8.1 VoxelTile

VoxelTile ist der erste vertikale Produktschnitt und ersetzt die bisherige
Terrain-Malerei.

- Die 2D-Palette wählt ein Terrain-Asset per `asset_key`.
- Die Draufsicht zeigt den aktuell bearbeiteten horizontalen Layer.
- Additiv belegt die Zielzelle mit dem gewählten Material.
- Subtraktiv leert die Zielzelle.
- Pencil, Line und Fill können ihre vorhandenen Interaktionsmuster behalten.
- Vorschau, Commit, Eraser-Farbe und Undo bleiben konsistent zum bisherigen
  SceneMaker.

Vor der Umsetzung ist zu entscheiden, ob „Malen auf der Grundebene“ den Voxel
unter oder über Elevation 0 bezeichnet. Derselbe Vertrag gilt später für alle
Werkzeuge und Exporte.

### 8.2 VoxelHill

VoxelHill rasterisiert eine geschlossene Bezier-Kontur zu einem Volumen zwischen
einer Basis- und einer Zielhöhe.

Vorgeschlagene Regeln:

- Kontrollpunkte liegen auf dem 0,2-m-Unterraster, Handles bleiben frei oder
  werden nur auf ausdrücklichen Wunsch gerastert;
- die Kontur muss einfach und flächenhaltig sein;
- additive Füllung erzeugt ganze 1-m-Voxel;
- subtraktive Füllung entfernt das entsprechende Volumen;
- additive Voxel erben pro X/Z-Säule das Material des höchsten belegten Voxels
  unmittelbar unter der Füllung;
- existiert darunter kein Material, erzeugt die Operation an dieser Säule
  standardmäßig nichts und berichtet dies in der Vorschau.

Die letzte Regel übernimmt die bestehende materiallose Hill-Idee. Sie verhindert
Materialerfindung, muss aber mit dem gewünschten Verhalten „großes geschlossenes
Volumen erstellen“ abgeglichen werden. Falls ein Hill auch über leerem Raum
Volumen erzeugen soll, benötigt das Werkzeug ein explizit gewähltes Material;
ein versteckter Fallback ist nicht zulässig.

Zu klären ist außerdem, ob „Elevation 8 m“ eine absolute Oberkante oder eine
Höhe relativ zur lokalen Basis ist. Empfehlung: UI und Dokument unterscheiden
explizit `Base elevation` und `Top elevation`; eine abgeleitete `Height` darf
nicht dieselbe Zahl mehrdeutig bezeichnen.

### 8.3 VoxelPath

VoxelPath ersetzt die geometrischen Aufgaben von River und Ramp durch einen
volumetrischen Korridor entlang einer offenen Bezier-Kurve.

Jeder Punkt oder Abschnitt kann tragen:

- Breite;
- Querschnittshöhe;
- absolute Elevation;
- alternativ eine Steigung, aus der die nächste Elevation berechnet wird;
- Modus für schwebend oder bis zum Untergrund aufgefüllt.

Vor der Implementierung müssen folgende Semantiken eindeutig sein:

- Bezeichnet Elevation Boden, Mitte oder Oberkante des Pfadquerschnitts?
- Hat explizite Elevation Vorrang vor Steigung oder sind beide Modi gegenseitig
  exklusiv?
- Wird die Steigung über horizontale Bogenlänge oder 3D-Länge berechnet?
- Wie werden Breite, Höhe und Elevation zwischen Punkten interpoliert?
- Welche Kappe besitzen Start und Ende?
- Welcher Querschnitt gilt: Rechteck, abgerundetes Rechteck oder auswählbares
  Primitive?
- Bedeutet `fill to ground` bis zum ersten belegten Voxel, bis zu einer
  Referenzebene oder bis zur Scene-Untergrenze?
- Welches Material erhält ein additiver schwebender Pfad?

Empfehlung: Steigung ist `delta Y / horizontale Bogenlänge`; Elevation bezeichnet
die begehbare Unterkante beziehungsweise den Boden des Korridors; explizite
Elevation und abgeleitete Steigung sind pro Segment zwei klar getrennte Modi.
Additive Pfade tragen ein explizit gewähltes Terrain-Asset. Subtraktive Pfade
entfernen jedes Material in ihrem Querschnitt.

Rampe, Serpentine und Tunnel verwenden denselben Rasterizer. Ein Kreuzungspunkt
zweier subtraktiver Pfade ist deshalb natürlicherweise die Vereinigung der
entfernten Volumina und benötigt keine Sonderbehandlung.

## 9. Intelligentes Cross-Section-Viewport-System

### 9.1 Darstellungsvertrag

Die Standardansicht bleibt die 2D-Draufsicht. Sie bekommt einen expliziten
Arbeitslayer beziehungsweise eine horizontale Schnittebene.

Für eine Schnitthöhe zeigt der Canvas mindestens drei unterscheidbare Klassen:

- **Schnittbelegung:** Voxel, die von der aktuellen horizontalen Ebene
  geschnitten werden;
- **freier Arbeitsraum:** leere Zellen auf dieser Ebene, einschließlich bereits
  ausgeschnittener Tunnel;
- **Überkopf-Silhouette:** X/Z-Umriss von Volumen oberhalb der Ebene, gedämpft
  oder nur als Kontur.

Optional kann Volumen unterhalb der Ebene als vierte, schwächer dargestellte
Klasse erscheinen. Diese Ebene darf jedoch die Lesbarkeit von Tunnelraum und
Überkopfkontur nicht verringern.

Das System ist eine 2D-Projektion des Voxelgrids, keine Kamera, kein 3D-Node und
kein Mesh. Die Projektion gehört in Editor, die Abfrage des Voxelzustands und
etwaige räumliche Indizes in Core, das Zeichnen in App.

### 9.2 Folgen der Arbeitshöhe

Während eines Path-Entwurfs berechnet `ToolInteraction` aus dem aktuellen
Mauspunkt eine Vorschau-Station und deren Elevation. Dieser Wert steuert den
temporären Schnitt. Nach jedem neuen Kontrollpunkt wird die Ansicht auf dessen
Arbeitshöhe stabilisiert. Abbruch oder Werkzeugwechsel stellt den vorherigen
manuellen Layer wieder her.

„Zur normalen Draufsicht zurückkehren“ darf nicht implizit aus einem einzelnen
Terrainwert geraten werden. Vorgeschlagen ist:

- **Auto-Schnitt:** folgt der aktiven Werkzeugvorschau;
- **fixierter Schnitt:** Autor wählt eine Höhe manuell;
- **Surface overview:** projiziert die jeweils obersten sichtbaren Voxel.

Wenn die Auto-Schnittebene an jeder X/Z-Position oberhalb des höchsten Voxels
liegt, fällt die Darstellung visuell mit `Surface overview` zusammen. Der
Moduswechsel bleibt trotzdem explizit und testbar.

### 9.3 Editorzustand

Ein engine-neutraler `CrossSectionViewState` sollte mindestens enthalten:

- Modus: Overview, Fixed oder FollowTool;
- aktuelle Elevation beziehungsweise Voxel-Layer;
- zuvor fixierte Elevation für die Rückkehr nach einem Entwurf;
- Regeln für Sichtbarkeit und Auswahl;
- optional einen begrenzten Bereich für inkrementelle Neuberechnung.

Hit-Tests arbeiten immer gegen den sichtbaren Arbeitslayer. Die Ansicht selbst
verändert kein Dokument und landet nicht im Scene-Undo. Ein Werkzeug-Commit
landet genau einmal im Undo.

### 9.4 Akzeptanzszenario

Ein verbindlicher End-to-End-Test beziehungsweise manueller UX-Test verwendet:

1. einen additiven Hill bis 8 m;
2. einen subtraktiven Path mit 0,25 m Steigung pro horizontalem Meter;
3. einen zweiten Tunnel quer durch den Hill;
4. eine kontrollierte Kreuzung beider Hohlräume;
5. eine schrittweise mitwandernde 2D-Schnittansicht;
6. den Wechsel zurück zur Oberflächenübersicht am Austritt.

Die Phase gilt erst als fertig, wenn dieses Szenario ohne 3D-Ansicht verständlich
und authorisierbar ist.

## 10. Materialien, Surface-Tokens und Placements

Workspace-Assets bleiben die geschlossene Quelle für `asset_key`, Anzeigename,
Editorfarbe, Rolle und Surface-Token. Ein Voxel referenziert ausschließlich den
stabilen Schlüssel. PolyTools bleibt eine Workspace-lokale Importgrenze für
Placement-Geometrie und trägt nichts zur Terrain- oder Voxelidentität bei.

Placements bleiben zunächst eigenständige Scene-Objekte mit horizontaler
Position und absoluter Höhe. Für ihre Platzierung benötigt der Editor eine
Abfrage wie „oberste begehbare Oberfläche an X/Z“ oder eine Auswahl unter
mehreren sichtbaren Oberflächen. Die erste Version darf keine Unterstützung
oder Kollision aus Voxelmaterialien erraten.

## 11. Templates und Anchors

Templates müssen ganze Voxelvolumina verlustfrei versetzen können. Dafür sind
vor dem Schema-Cut zu entscheiden:

- erhalten Anchors zusätzlich eine Y-Koordinate oder bleibt ihre vertikale
  Einfügehöhe Werkzeug-/Sessionzustand?
- darf ein Template negative lokale Y-Koordinaten besitzen?
- überschreiben Template-Voxel vorhandene Materialien, füllen sie nur leeren
  Raum oder verwenden sie explizit additive/subtraktive Masken?
- wie werden Template- und Instance-Grenzen behandelt?

Empfehlung: Ein Template besitzt einen dreidimensionalen lokalen Ursprung und
einen dreidimensionalen Insertion-Anchor. Komposition übersetzt Voxelkoordinaten
und Placements deterministisch. Konfliktregeln werden als explizite
Kompositionsoption festgelegt; stilles Überschreiben ist zu vermeiden.

## 12. Persistenz- und Schema-Strategie

SceneMaker liest heute ausschließlich bekannte Schema-Versionen. Dieser Vertrag
bleibt bestehen.

### 12.1 Kein additives Übergangsschema

Es soll kein veröffentlichtes Scene-Schema geben, das gleichzeitig
`terrain_cells`, `water_bodies`, `route_surfaces`, `elevation_regions` und ein
schreibbares `voxel_grid` enthält. Ein solches Dokument könnte widersprüchliche
Geometrie speichern und würde die Frage nach der Wahrheit an jeden Consumer
weiterreichen.

Während der Entwicklung dürfen Test-Fixtures oder interne Adapter beide Modelle
im Speicher vergleichen. Persistierte Workspace-Scenes werden erst beim
bewussten Cut umgestellt.

### 12.2 Vorgeschlagener Scene-Cut

Das neue Schema enthält konzeptionell:

- Scene-Identität, Art und metrische X/Z-Ausdehnung;
- kanonische sparse Voxel-Chunks oder eine kanonische Voxel-Liste;
- Placements;
- Template-Definition und dreidimensionale Anchors;
- Scene-Default für eine Authoring-Elevation, falls weiterhin nötig.

Legacy-Geometriefelder entfallen gemeinsam. Die genaue JSON-Form wird erst nach
Messung realistischer Datenmengen festgelegt. Lesbarkeit, deterministische Diffs
und Dateigröße sind gemeinsam zu bewerten; voreilige Binärblobs würden die
Authoring-Daten schwer prüfbar machen.

### 12.3 Konvertierung bestehender Workspaces

Da keine allgemeine Migration im Reader vorgesehen ist, gibt es zwei saubere
Optionen:

1. Die wenigen authored Workspace-Dateien werden wie bisher kontrolliert von
   Hand beziehungsweise durch einen einmaligen, separat geprüften Konverter
   umgeschrieben.
2. Falls die Menge das unpraktisch macht, wird ein explizites CLI-Kommando
   `migrate` als IO-Grenze entworfen. Der normale Reader bleibt strikt und führt
   keine stille Migration aus.

Die Legacy-Konvertierung ist teilweise verlustbehaftet und braucht Regeln:

- Terrain-Zelle plus effektive Hill-Höhe wird zu einer massiven Materialsäule;
- Elevation-Regions sind nach dem Bake nicht mehr als Konturen editierbar;
- Route-Surfaces benötigen eine definierte Dicke, bevor sie Voxel werden können;
- Water-Korridore benötigen eine Produktentscheidung, ob sie Hohlraum plus
  Material, nur Hohlraum oder einen gesonderten Runtime-Inhalt darstellen;
- bestehende Tunnel-/Clearance-Semantik muss gegen 1-m-Rundung geprüft werden.

Der Konverter erzeugt vor dem Überschreiben ein Backup, validiert den Output und
schreibt atomar. Workspace-Dateien werden niemals implizit beim Öffnen geändert.

## 13. Exportarchitektur

Alle Exporter konsumieren denselben unveränderlichen, validierten Voxel-Snapshot.
Ein gemeinsamer Adaptervertrag trennt Zielauswahl von Geometrieableitung.

### 13.1 Höhenkarte plus Layer-/Materialinformation

Dieser Export ist für bestehende 2,5D-Consumer, aber notwendigerweise
verlustbehaftet. Vor seiner Implementierung muss sein Vertrag festlegen:

- wird pro X/Z nur die oberste Oberfläche exportiert oder werden mehrere
  vertikale Solid-/Air-Spans unterstützt?
- wie werden Tunnel, Brücken und Überhänge behandelt?
- wann ist Informationsverlust eine Warnung und wann ein Exportfehler?
- wie wird das sichtbare Material einer Säule gewählt?

Empfehlung: Der Kompatibilitätsexport verweigert keine ganze Scene, listet aber
präzise Positionen beziehungsweise Regionen, deren Mehrfachoberflächen oder
Hohlräume das Zielformat nicht darstellen kann. Ein Consumer darf diese Warnung
in CI zum Fehler hochstufen.

### 13.2 Optimierte 3D-Geometrie

Der Mesh-Export verschmilzt verdeckte Flächen und kann Greedy Meshing oder eine
vergleichbare deterministische Methode verwenden. Materialgruppen werden nach
`asset_key` getrennt. Das Mesh ist abgeleitet und niemals editierbare Quelle.

### 13.3 Komprimierte Voxel

Der Voxel-Export bewahrt das vollständige Volumen. Format, Chunking und
Kompression sind ein eigener versionierter Runtime-Vertrag und müssen nicht mit
der lesbaren Scene-Persistenz identisch sein.

### 13.4 Bestehende Export-Contracts

Die bisherigen Export-Contracts bleiben solange stabil, bis ihr aktueller
Consumer aus einer Voxelwelt mit eindeutig dokumentierter Verlustregel erzeugt
werden kann. Ein Schema-Bump wird mit dem Consumer koordiniert. WorldVoxMaker
darf keine game-spezifischen Felder in Core aufnehmen, um einen einzelnen
Runtime-Reader abzukürzen.

## 14. Migrationsphasen und Exit-Kriterien

### Phase 0 – Produktentscheidungen und ADRs

Noch keine Produktcode-Änderung.

- Grundebenen-/Voxelgrenzen-Semantik festlegen;
- Bedeutung des 0,2-m-Unterraster bestätigen;
- Baked Grid versus editierbare Operationen entscheiden;
- Materialvererbung bei Hill und Materialwahl bei Path festlegen;
- Path-Elevation, Steigung, Querschnitt und Fill-to-ground definieren;
- vertikale Scene-Grenzen und negative Y-Werte festlegen;
- Verlustvertrag des 2,5D-Exports skizzieren.

Exit: Entscheidungen sind als kurze ADRs dokumentiert und die Akzeptanzbeispiele
haben eindeutige Soll-Ergebnisse.

### Phase 1 – Voxel-Kernel in Core

- Koordinaten, Bounds, sparse Speicherung und kanonische Ordnung;
- additive/subtraktive Box-Operation als erster Referenzfall;
- Materialvalidierung gegen Workspace-Konfiguration;
- Abfragen für Slice, oberstes Voxel und vertikale Spans;
- Messungen für Speicher, Mutation und Undo auf realistischen Größen.

Noch kein neues Scene-Schema und keine UI-Umstellung.

Exit: reine Core-Tests decken Grenzen, negative Koordinaten, deterministische
Ergebnisse, Materialregeln und Performance-Budget ab.

### Phase 2 – 2D-Schnittprojektion in Editor

- `CrossSectionViewState` und Projektionstypen;
- Slice-/Overhead-Silhouette aus einem Fixture-Grid;
- Hit-Tests, Pan/Zoom und FollowTool-Verhalten;
- experimentelle Darstellung im bestehenden `SceneCanvas`, hinter einem
  Developer-Schalter oder ausschließlich in Test-Fixtures.

Exit: Die Hill/Tunnel-Kreuzung ist in der 2D-Ansicht lesbar, ohne bestehende
Scene-Dateien oder die Standardnavigation zu verändern.

### Phase 3 – VoxelTile als vollständiger vertikaler Schnitt

- neues Persistenzschema finalisieren und versionieren;
- Workspace-Metriken versionieren;
- Create/Open/Save/Validate für Voxel-Scenes;
- Tile Pencil, Line, Fill und Subtraktion über den bestehenden Eingabepfad;
- Undo/Redo und Vorschau;
- kontrollierte Konvertierung der Workspace-Dateien;
- bestehendes Layout und 2D-Canvas bleiben erhalten.

Exit: Eine reale Workspace-Scene kann ausschließlich als Voxel-Scene angelegt,
bearbeitet, gespeichert, neu geöffnet und validiert werden. Es existiert keine
zweite schreibbare Terrainwahrheit.

### Phase 4 – VoxelHill und Primitive-Bibliothek

- gemeinsame Primitive-Verträge;
- geschlossene Kontur, Rasterisierung, Basis/Top und Materialvererbung;
- additive und subtraktive Vorschau/Operation;
- Grenzen und leere Untergründe sichtbar berichten.

Exit: Überlappende Hills und ein vollständig durch einen Hill geschnittener
Hohlraum sind deterministisch, undoable und nach Reload identisch.

### Phase 5 – VoxelPath und Follow-Viewport

- offener Bezier-Pfad mit interpoliertem Querschnitt;
- Elevation-/Steigungsmodi;
- Floating versus Fill-to-ground;
- additive Rampe und subtraktiver Tunnel;
- mitwandernde Schnittebene im bestehenden Canvas.

Exit: Das Akzeptanzszenario aus Abschnitt 9.4 funktioniert vollständig in 2D.

### Phase 6 – Templates, Anchors und Placements

- 3D-Translation von Templatevolumen;
- explizite Konfliktregeln;
- Placement-Höhenwahl auf sichtbaren Voxeloberflächen;
- Validierung und Undo.

Exit: Ein Template mit Volumen und Placement wird reproduzierbar an mehreren
Höhen komponiert, ohne Inhalt still zu verlieren.

### Phase 7 – Exportadapter und Runtime-Abstimmung

- bestehender 2,5D-Kompatibilitätsexport samt Verlustdiagnostik;
- Mesh-Export;
- komprimierter Voxel-Export;
- CLI-Auswahl, Contract-Tests und Golden Files;
- koordinierter Consumer-Update bei Schemaänderungen.

Exit: Jeder Adapter ist unabhängig versioniert, deterministisch und meldet
nicht darstellbare Voxelmerkmale nachvollziehbar.

### Phase 8 – Entfernen des Legacy-Systems

Erst wenn alle realen Workspace-Dateien konvertiert und die benötigten Exporte
verfügbar sind:

- alte Höhenfeld-Editingpfade entfernen;
- Water-, Route-Surface- und Elevation-Region-Dokumente entfernen;
- 2,5D-spezifische Geometrie entfernen, sofern kein Exportadapter sie noch als
  abgeleiteten Algorithmus benötigt;
- veraltete UI-Modi und Dokumentation bereinigen.

Exit: `rg`- und Architekturprüfung finden keine schreibbare Legacy-Geometrie;
alle Prüfungen und ein manueller 2D-UX-Durchlauf sind grün.

### Phase 9 – Separate 3D-LookDev-Ansicht

Diese Phase beginnt erst nach stabiler 2D-Autorisierung und Exportpipeline.

- zunächst read-only;
- nutzt denselben validierten Voxel-Snapshot oder einen Mesh-Adapter;
- besitzt keinen eigenen Dokument-, Tool- oder Undo-Zustand;
- darf als separater View im Godot-App-Projekt entstehen;
- beeinflusst die 2D-Navigation und den Cross-Section-Workflow nicht.

## 15. Test- und Verifikationsstrategie

Jeder Implementierungsschnitt benötigt Tests auf der tiefstmöglichen Schicht.

### Core

- kanonische sparse Speicherung und JSON-Roundtrip;
- Add/Subtract-Idempotenz und Mengenoperationen;
- Materialvererbung und Überschreibregeln;
- Primitive-Golden-Tests bei Grenzberührung und 0,2-m-Rundung;
- Path-Rasterisierung, Steigung und Kreuzungen;
- Slice-, Silhouette- und Span-Abfragen;
- Template-Komposition;
- Export-Contracts und Verlustdiagnostik.

### Editor

- Mode-/Tool-Zustandsmaschine;
- genau ein Outcome pro Eingabe;
- Vorschau entspricht Commit-Raster;
- FollowTool-Schnitt und Wiederherstellung des fixierten Layers;
- Hit-Tests in Overview und Slice;
- ein Undo-Schritt pro fertigem Körper, Punkt-für-Punkt-Undo im Entwurf.

### App und manuelle Abnahme

- vorhandenes Layout und Navigation bleiben wiedererkennbar;
- Pan, Zoom, Palette, Kontextfelder und Statusmeldungen;
- Lesbarkeit der Schnittdarstellung mit großen und kleinen Karten;
- das verbindliche Hill-/Serpentinen-/Quertunnel-Szenario;
- Godot-Headless-Start und manueller Canvas-Durchlauf.

Die bestehende vollständige Verifikationsliste aus `AGENTS.md` bleibt für jeden
relevanten Commit verbindlich.

## 16. Risiken und Gegenmaßnahmen

| Risiko | Auswirkung | Gegenmaßnahme |
| --- | --- | --- |
| 0,2 m wird als Microvoxel missverstanden | 125-fache Datenmenge und andere Toolsemantik | Vor Phase 1 als ADR entscheiden |
| Baked Grid und editierbare Formen werden vermischt | Zwei Wahrheiten, unklare Reihenfolge | Genau eine Source-of-truth-Variante wählen |
| 1-m-Voxel sind für 0,25-m-Steigungen zu grob | Treppen statt kontinuierlicher Rampe | Erwartung und Mesh-/Exportregel explizit testen; gegebenenfalls Auflösung neu entscheiden |
| Materialvererbung trifft auf leeren Raum | Unvollständiger Hill oder erfundenes Material | Vorschau und explizite Produktregel |
| Höhenkartenexport verliert Tunnel | Runtime verhält sich anders als Authoring | Verlustdiagnostik und Consumer-spezifischer Vertrag |
| Slice wird visuell überladen | Innenräume bleiben schwer authorisierbar | Fixture-Prototyp vor Schema-Cut, klare Darstellungslegende |
| Große Volumenoperationen blähen Undo auf | Speicher- und Latenzprobleme | Chunk-Deltas messen und budgetieren |
| X/Z/Y und Canvas-X/Y werden verwechselt | Off-by-one- und Spiegelungsfehler | Benannte Typen und Roundtrip-Tests |
| Big-Bang-Schemawechsel beschädigt Workspaces | Datenverlust und lange Integrationsphase | Backup, atomarer Konverter, vertikale Schnitte |
| 3D-LookDev zieht Authoring früh in 3D | Produkt wird erneut unkenntlich | Phase 9 und read-only als feste Grenze |

## 17. Entscheidungen vor Implementierungsbeginn

| Thema | Vorschlag | Status |
| --- | --- | --- |
| Primäre Ansicht | bestehender 2D-Canvas | fest |
| 3D-LookDev | später, read-only beginnend | fest |
| Persistierte Voxelgröße | 1 m³ | fest |
| 0,2-m-Unterraster | Form-/Kontrollraster, keine Microvoxel | zu bestätigen |
| Source of truth | direkt bearbeitetes Voxelgrid | zu bestätigen |
| Grundebene | Y=0 als Unterseite des ersten Bodenvoxels | offen |
| Negative Y-Werte | innerhalb expliziter Grenzen erlauben | offen |
| Hill ohne Material darunter | nichts erzeugen und sichtbar berichten | offen |
| Path-Elevation | Boden des Querschnitts | offen |
| Steigung | ΔY pro horizontaler Bogenlänge | offen |
| Additiver Path | explizites Terrain-Asset | offen |
| Path-Fill-to-ground | bis erstes belegtes Voxel oder Referenzebene | offen |
| Template-Anchor | dreidimensional | offen |
| Legacy-Konvertierung | explizites einmaliges CLI/Tool, niemals beim Öffnen | offen |
| Chunk-Größe | erst nach Benchmark | offen |

## 18. Fragen für den unabhängigen Review

Claude soll insbesondere prüfen:

1. Ist das direkt bearbeitete Voxelgrid als erste Source of truth tragfähig, oder
   machen die erwarteten nachträglichen Kurvenänderungen eine geordnete
   Operationen-/CSG-Quelle bereits in Version 1 zwingend?
2. Ist ein persistiertes 1-m-Raster mit einem reinen 0,2-m-Authoring-Unterraster
   konsistent mit Rampen von 0,25 m Steigung und dem gewünschten 2,5D-Export?
3. Welche eindeutige Grundebenen- und Elevation-Semantik vermeidet Off-by-one-
   Fehler zwischen Voxel, Schnittansicht, Path und Runtime?
4. Reicht die vorgeschlagene Slice-/Overhead-Projektion aus, um Tunnelkreuzungen
   ohne 3D-Ansicht zuverlässig zu authorisieren? Welche zusätzliche 2D-Codierung
   wäre minimal notwendig?
5. Welche Materialregel für Hill über leerem Raum ist fachlich konsistent und
   bleibt für den Autor vorhersehbar?
6. Sollte `fill to ground` beim Path gegen belegte Voxel, eine Referenzebene
   oder eine explizite Stützform arbeiten?
7. Welche Chunk- und Undo-Repräsentation bietet in C# eine gute Balance zwischen
   Immutability, Dateidiffs und großen Volumenoperationen?
8. Wie sollte der 2,5D-Kompatibilitätsexport mit Höhlen, Überhängen und mehreren
   Oberflächen pro X/Z umgehen, ohne still Inhalt zu verlieren?
9. Ist der vorgeschlagene Schema-Cut nach dem Slice-Prototyp und zusammen mit
   VoxelTile der kleinste sichere vertikale Schnitt?
10. Welche Abhängigkeit oder Migrationsfalle im bestehenden Vier-Projekte-Modell
    wurde übersehen?

## 19. Review-Auftrag für Claude

Der folgende Text kann zusammen mit diesem Dokument weitergegeben werden:

> Prüfe diesen Architektur- und Migrationsplan kritisch als unabhängiger Senior-
> Architekt für Godot/C#-Authoring-Tools. Die zentrale Produktanforderung ist,
> dass WorldVoxMaker trotz echtem 3D-Voxelmodell ein vertrauter 2D-Top-down-
> Editor bleibt; eine 3D-LookDev-Ansicht kommt erst später und ist zunächst
> read-only. Suche nach Widersprüchen, fehlenden Entscheidungen, versteckten
> zweiten Wahrheiten, problematischen Koordinaten- oder Rastersemantiken,
> Performance-/Undo-Risiken und zu großen Migrationsschritten. Beurteile
> besonders das Verhältnis von 1-m-Voxeln zum 0,2-m-Unterraster und zu
> 0,25-m-Rampen, die Cross-Section-UX sowie die Verlustregeln der 2,5D-Exporte.
> Schlage konkrete Änderungen am Plan vor, aber implementiere nichts. Trenne
> Blocker, wichtige Verbesserungen und optionale spätere Optimierungen.

