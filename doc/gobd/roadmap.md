# Roadmap

Zurück: [Index](README.md) · Begründungen: [Architekturentscheidungen](architekturentscheidungen.md) · Lücken: [Fehlt](ist-stand-original.md#fehlt)

| # | Schritt | Inhalt | Entscheidung | Status |
|---|---|---|---|---|
| 1 | Hash + EML | Original-EML und SHA-256 in `archive_worm`, `ContentHash` befüllen; immer aktiv, kein Schalter; kein Nachrüsten alter Archive | E1, E5, E7, E8 | Speicher (PR #1) und Speichern beim IMAP-Sync (PR #2) fertig und getestet ([Testprotokoll 2026-10-05](testprotokoll-2026-10.md)); Schalter entfernt, Löschsperre fest, Löschknöpfe ausgebaut (PR #2, getestet); Datei-Import mit Original gebaut, getestet und im Hauptzweig (PR #6, Commit `ad5cc51`); offen: Microsoft 365 |
| 2 | Sperre härten | App-Rolle nur INSERT/SELECT, Trigger, Kontolöschung nicht mehr an der Sperre vorbei | E2, E3, E4 | gebaut und getestet (PR #1 bis #3); Härtung ist ein Schritt für den Administrator (`doc/WormStorage.md`) |
| 3 | Protokoll schützen | `AccessLogs` im geschützten Schema, nur anfügbar, SHA-256-Verkettung, Knopf „Protokoll prüfen“ | E11 | gebaut und getestet (PR #4, [Testprotokoll 2026-10-05](testprotokoll-2026-10.md)) |
| 4 | Frist und Löschlauf | Einheitlich 8 Jahre ab Jahresende, Verlängerung bei Prüfung, automatisches Löschen nach Ablauf, kein manuelles Löschen | E4, E9, E10 | gebaut und getestet (PR #3, [Testprotokoll 2026-10-05](testprotokoll-2026-10.md)) |
| 5 | Prüfer-Rolle | Nur lesend, sieht alle Postfächer, kein Wiederherstellen, keine Verwaltung (Z1) | E12 | gebaut und getestet (PR #5, [Testprotokoll 2026-10-05](testprotokoll-2026-10.md)); nicht gebaut: Begrenzung auf Zeitraum oder Postfächer |
| ∥ | Muster-Verfahrensdoku | Muster zum Ausfüllen je Kunde: [Verfahrensdokumentation](verfahrensdokumentation-muster.md) | – | Entwurf vom 06.10.2026, rechtlich nicht geprüft |

## Ideen für später

- **KI-Kennzeichnung** (Idee des Projektinhabers, 05.10.2026): Mails im Archiv automatisch als „Rechnung“, „Angebot“, „Vertrag“ usw. kennzeichnen, zum Filtern und Suchen. Ohne Einfluss auf Frist und Löschen ([Architekturentscheidungen](architekturentscheidungen.md) E10), ohne Verschieben im Postfach, bevorzugt mit lokalem Modell ([AV-Vertrag (Art. 28)](dsgvo-anforderungen.md#av-vertrag-art-28)).

## Reihenfolge

1 vor 2: Erst muss das Original existieren, dann lohnt es, es zu schützen. 3 vor 4: Löschungen nach Fristablauf brauchen ein beweiskräftiges Protokoll. 5 zuletzt, weil die Prüfer-Rolle auf allem anderen aufsetzt.

## Abnahme je Schritt

- **1:** Exportierte EML ist bytegleich mit dem Original; Hash stimmt nach erneutem Berechnen.
- **2:** UPDATE und DELETE mit der App-Rolle schlagen fehl, auch per direktem SQL; Kontolöschung bei aktiver Sperre wird abgewiesen.
- **3:** Geänderter oder entfernter Protokolleintrag wird verhindert oder erkannt.
- **4:** Mail vom 03.02.2026 ist bis 31.12.2034 gesperrt und wird danach automatisch gelöscht, außer bei Löschsperre.
- **5:** Prüfer-Konto kann nichts ändern, löschen oder außerhalb seines Zeitraums sehen.

Maßstab: [GoBD-Anforderungen](gobd-anforderungen.md) und [DSGVO-Anforderungen](dsgvo-anforderungen.md). Repositories: [Links](quellen-und-links.md).
