# WATER-01 — Ihr habt die richtige Zeile genommen

Kurz, weil ihr baut.

## Die Zahlen gehoerten zur anderen Zeile

Nachgerechnet, ihr habt recht:

```
upper_valley aus, branch aus   1101 fallen weg,  9 bleiben belegt, 1092 werden leer
upper_valley aus, branch an    1101 fallen weg, 20 bleiben belegt, 1081 werden leer
```

Unsere Tabellenzeile war die erste, unsere Zellenzahlen waren die zweite. Wir
haben eine Aufloesungstabelle und einen Satz Zellenzahlen in einem Atemzug
geschrieben, und die beiden meinten verschiedene Schalterstellungen. Fuer die
Abnahme gilt eure Wahl: **9 und 1092**, die Karte, wie sie aufmacht.

Auch eure 11 stimmen: von den 27 Zellen haengen 16 an `river_0001`, 17 an
`river_0002`, 6 an beiden — also 11, die nur `river_0002` stuetzt.

## Eure zweite Beobachtung ist die wertvollere

Dass ein Schalter eine Abnahme verkleinert, ohne dass sich eine Zelle bewegt,
und dass der Lauf davon gruen bleibt — das ist die Sorte Befund, gegen die kein
Test hilft, weil er ja nicht bricht. Bei uns steht jetzt als dritte Zeile in
derselben Regel: eine Zahl ueber Wasserzellen ist eine Aussage ueber **eine
Stellung jedes Schalters**, also gehoert die Stellung dazu. "Diese Zellen
ueberlappen" gilt unbedingt, "diese Zellen treffen fliessendes Wasser" nicht,
und der Unterschied ist unsichtbar, bis jemand umlegt.

Die drei Zeilen zusammen sind diese Woche entstanden, jede aus einem Fehler von
uns: die richtige Datei lesen, Zellen statt Eintraege zaehlen, die
Schalterstellung dazusagen. Wir schulden euch die drei.

## Wir lassen die Karte, wie sie ist — und warum

Man kann es tauschen, aber nicht beides haben:

- `upper_valley` auf `river_0002`: die Kaskade bezeugt **zwei Stufen Vererbung**
  — `river_0004` erbt von `river_0002`, `river_0005` von `river_0004`. Das ist
  der Unterschied zwischen "der Leser laeuft die Kette" und "der Leser schaut
  einen Schritt hoch". Dafuer haengen 11 der 27 Zellen an einer Stellung.
- `upper_valley` auf `river_0004`: die 27 Zellen waeren wieder unbedingt, die
  Kaskade bezeugte nur noch **eine Stufe**.

Alle 27 unbedingt zu haben verlangt, dass `river_0002` unbedingt ist, und genau
das ist der Koerper, dessen Abschalten die Kette beweist. Da ihr die
Vorbedingung ohnehin aufgeschrieben habt und sagt, es sei kein Fehler in der
Karte, nehmen wir die staerkere Kaskade. Sagt Bescheid, wenn ihr es andersherum
wollt — es ist ein Feld.

Viel Erfolg beim Leser.
