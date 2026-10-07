# Testprotokoll 2026-10-05

Zurück: [Index](README.md) · Getestet: Schritt 1 der [Roadmap](roadmap.md), Teil „Speicher“ ([Architekturentscheidungen](architekturentscheidungen.md) E1 bis E3)

**Gegenstand:** Branch `feature/original-eml`, Commit `517e8bf` (PR #1 im Fork).
**Umgebung:** Teststack `mailarchiver-gobd-test` auf dem Testserver, eigene Datenbank `MailArchiverTest` (PostgreSQL 17), Port 5099, getrennt von der produktiven Instanz. Ein echtes Postfach mit 188 Mails, ohne Löschregeln.
**Durchführung:** Projektinhaber im Terminal des Servers, Skripte unter `\Testserver\coding\mail-archiver-gobd-test\tests`.

## Ergebnisse

| # | Prüfung | Erwartet | Ergebnis |
|---|---|---|---|
| 1 | Schema `archive_worm` mit Tabelle `ArchivedEmailSources` vorhanden | ja | bestanden |
| 2A | Original mit falschem Hash speichern | abgelehnt | bestanden (`hash mismatch`) |
| 2B | Original mit richtigem Hash speichern; Größe 0 und Frist 2000 mitgegeben | angenommen, Datenbank setzt Größe und Frist selbst | bestanden (Größe 58, Frist 2037-01-01) |
| 3C | Original ändern | abgelehnt | bestanden (`write-once`) |
| 3D | Original löschen | abgelehnt | bestanden (`retained until 2037`) |
| 3E | Tabelle leeren | abgelehnt | bestanden (`cannot be truncated`) |
| 3F | Mail löschen, nachdem `IsLocked` aufgehoben wurde | abgelehnt | bestanden |
| 4a | Härtung: eigene App-Rolle, eigene Besitzer-Rolle | App nur INSERT/SELECT, kein Superuser | bestanden |
| 4b G–I | Als App: Trigger entfernen, Funktion ersetzen, Trigger abschalten | abgelehnt | bestanden (`must be owner`, `permission denied`) |
| 4b J | Als App: Original ändern, löschen | abgelehnt | bestanden (`permission denied`) |
| 4b K | Als App: weiteres Original anfügen | angenommen | bestanden |
| 5 | App startet mit eingeschränkter Rolle und synchronisiert | ja | **erst nach Korrektur bestanden:** Start-Einrichtung brach mit `permission denied for schema public` ab (siehe Befund). Nach `GRANT CREATE ON SCHEMA public` und Neustart 0 Rechtefehler im Log |

## Zweiter Durchgang: Sync speichert Originale

**Gegenstand:** Branch `feature/original-eml-capture`, Commit `4920e82` (PR #2), Schalter `Compliance__StoreOriginalMime=true`, gleiche gehärtete Testdatenbank.

| # | Prüfung | Erwartet | Ergebnis |
|---|---|---|---|
| 6a | Neues Image bauen, App startet mit eingeschränkter Rolle | 0 Rechtefehler | bestanden |
| 6b | Testmail mit Anhang senden, Sync anstoßen | Original mit Herkunft `imap` | bestanden (Mail 189, 132.207 Bytes, Frist 2037-01-01) |
| 6b | Hash aus den gespeicherten Bytes neu berechnen | stimmt mit `Sha256` und mit `ContentHash` der Mail überein | bestanden (1 von 1, 0 abweichend) |
| 6b | Inhalt des Originals | Kopfzeilen des Mailservers | bestanden (`X-Envelope-From`, `Return-Path`, `ARC-Seal` …) |
| 7 | Postfach `<Testpostfach>` in der Oberfläche löschen (189 Mails, davon 1 mit Original) | abgelehnt, nichts gelöscht | bestanden (Meldung „… originals are stored write-once and still within their retention period …“, weiterhin 189 Mails) |

Beobachtungen zu 7, keine Fehler: Die Meldung ist nur englisch. Die Bestätigungsseite kündigt die Löschung noch an und lehnt erst nach dem Klick ab. Ein einziges Original sperrt die Löschung des ganzen Postfachs.

Damit schreibt die App erstmals selbst `ContentHash` und das Original, beides in einem Zug mit der Mail.

## Dritter Durchgang (06.10.2026): 8 Jahre, Löschlauf, Löschpause, keine Löschknöpfe

**Gegenstand:** Branch `feature/retention-auto-delete`, Commit `b8cbdc7` (PR #3), gleiche gehärtete Testdatenbank.

| # | Prüfung | Erwartet | Ergebnis |
|---|---|---|---|
| 8a | Neue Version ohne Datenbank-Skript starten | klare Meldung, kein Weiterlaufen | **teilweise:** Meldung „archive_worm is owned by another role …“ im Log, App lief aber weiter (Befund 2) |
| 8b | Skript als Datenbank-Verwalter ausführen, Neustart | Funktion auf 8 Jahre, Besitzer unverändert, 0 Fehler | bestanden |
| 8c | Neue Mail senden und abholen | Frist 2035-01-01, ältere bleiben 2037-01-01 | bestanden (Mail 190) |
| 8d | Mail-Ansicht | kein Löschknopf | bestanden |
| 8d | Auswahlmodus | nur Kopieren und Exportieren | bestanden |
| 8d | Postfach löschen | deutscher Hinweis statt Bestätigungsseite | bestanden (davor noch das alte Popup, Befund 3) |
| 8d | Seite „Aufbewahrung“ | Zahlen stimmen | bestanden (5 mit Original, Fristende 01.01.2035, 186 ohne Original) |
| 8d | Löschpause setzen und beenden | Anzeige, Liste mit Von/Bis/Grund, Protokolleintrag | bestanden (Eintrag zum Beginn gesehen, zum Ende nicht geprüft) |
| 9a | Startabbruch (Commit `31a49ea`): App-Rolle absichtlich ein Recht entzogen, neu gestartet | App beendet sich | bestanden (Container „Exited“, Grund im Log; Form unschön: unbehandelter Fehler, Code 139) |
| 9b | Recht zurückgegeben, Start | läuft, 0 Fehler | bestanden |
| 9c | Nachtest Oberfläche (Commit `31a49ea`) | Hinweisseite direkt, Pausenzeiten in deutscher Zeit, Beginn und Ende der Pause im Protokoll, neuer Systemtext | bestanden. Abgelehnter Löschversuch erscheint nicht im Protokoll: Die Hinweisseite bietet keinen Löschknopf mehr, es kommt also gar nicht zu einem Versuch |
| 10a | Startabbruch geordnet (Commit `2fd8f75`): Recht entzogen, neu gebaut | „Exited (1)“ und klare Fehlerzeile | bestanden („FATAL: Database initialization failed … permission denied for schema public. Fix the database … and start again.“) |
| 10b | Recht zurück, Start; Pause setzen; Protokoll auf Deutsch und Englisch | neue Einträge in der gewählten Sprache | bestanden („Automatische Löschung angehalten: Test Nr.3“ bzw. „Automatic deletion paused: Test Nr.3“; Systemeintrag zur Löschsperre deutsch). Meldung nur noch einmal, Überschrift „Automatische Löschung ist angehalten“, Zeiten in deutscher Zeit. Ältere Einträge bleiben englisch |

## Vierter Durchgang (06.10.2026): geschütztes Protokoll und Prüfer-Rolle

**Gegenstand:** Branch `feature/auditor-role`, Commit `1a8efe0` (PR #5, enthält PR #4), gleiche gehärtete Testdatenbank.

| # | Prüfung | Erwartet | Ergebnis |
|---|---|---|---|
| 11a | Admin-Skript `MigrateV2610_3-audit-log.sql`, neu bauen | Protokoll in `archive_worm`, App nur INSERT/SELECT, Kette intakt, 0 Fehler | bestanden (31 Einträge verkettet) |
| 11b | „Protokoll prüfen“ | grün | bestanden |
| 11b | Eintrag Nr. 3 als Datenbank-Verwalter gefälscht, erneut prüfen | rot, Stelle benannt | bestanden („Protokoll beschädigt: Eintrag Nr. 3 passt nicht zur Kette. Die 2 Einträge davor sind unverändert“) |
| 11b | Fälschung zurückgenommen | wieder grün, gleicher Prüfwert, Änderungen wieder gesperrt | bestanden (Prüfwert `8ab70124…` wie vorher) |
| 11c | Benutzer mit Rolle „Prüfer (nur Lesen)“ anlegen, anmelden | Menü nur Dashboard, Archiv, Protokoll; sieht alle Postfächer | bestanden (192 Mails sichtbar ohne Zuweisung) |
| 11c | Mail als Prüfer öffnen | „Export als EML“ da, „in Postfach kopieren“ fehlt | bestanden |
| 11c | Als Prüfer `/Retention` direkt aufrufen | „Zugriff verweigert“ | bestanden |

Nicht bestätigt: ob die Kombination Prüfer + Admin beim Anlegen abgelehnt wird (keine Rückmeldung), ob Zugriffe des Prüfers im Protokoll erscheinen, und die übrigen gesperrten Adressen (Benutzer, E-Mail Konten, Prüfdaten-Export, Wiederherstellen).

## Fünfter Durchgang (06.10.2026): Neuinstallation aus der Vorlage

**Gegenstand:** Hauptzweig nach Übernahme der fünf PRs (Commit `55189e8`, inhaltsgleich mit `1a8efe0`), Installationsvorlage `mail-archiver-gobd-vorlage`, leere Datenbank PostgreSQL 17, eigene Instanz auf Port 5010.

| # | Prüfung | Erwartet | Ergebnis |
|---|---|---|---|
| 12a | Erster Start: App richtet leere Datenbank mit eingeschränkter Rolle selbst ein | beide Container laufen, 0 Fehler | bestanden |
| 12b | `haerten.sql` | beide Tabellen bei `mailarchiver_worm_owner`, App nur INSERT/SELECT, keine Superuser | bestanden |
| 12c | Postfach einbinden, erster Abruf | jede Mail hat ein Original, Hash stimmt | bestanden: 191 Mails, 191 Originale, 0 ohne Original, 0 abweichend |
| 12c | Herkunft und Frist | nur `imap`, alle 2035-01-01 | bestanden (13 MB Originale) |
| 12c | Alte Mails | auch Mails von vor der Installation haben ein Original | bestanden (älteste vom 25.01.2026) |
| 12c | Protokoll-Kette | intakt | bestanden (5 Einträge) |

| 13 | Datei-Import (PR #6, Commit `25ead63`): eine `.eml` als ZIP über die Oberfläche importiert | Original mit Herkunft `eml-import`, Frist 2035-01-01, Datum der Mail bleibt | bestanden (Mail 192, Datum der Mail 27.09.2026, archiviert 06.10.2026, 27.791 Bytes; weiterhin 0 Mails ohne Original) |

| 13 | Hash der Datei auf dem PC (`Get-FileHash`) mit dem gespeicherten Hash verglichen | identisch | bestanden (`da95b09d…34ae8e`): Die Datei ist Byte für Byte so gespeichert, wie sie hochgeladen wurde |

Nicht geprüft bei 13: MBOX-Import, Import über die Kommandozeile, zweiter Import derselben Datei.

Damit ist belegt, was zuvor nur aus dem Code abgeleitet war: Bei einer frischen Installation bekommt jede vorhandene Mail beim ersten Abruf ihr Original ([Architekturentscheidungen](architekturentscheidungen.md) E8).

## Befund

Die Härtungsanleitung im Fork (`doc/WormStorage.md`) ist ab PostgreSQL 15 unvollständig. Die App führt bei jedem Start `CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"` aus und braucht dafür das Recht CREATE im Schema `public`. Seit PostgreSQL 15 haben normale Rollen es nicht mehr automatisch; Test- und Produktivinstanz laufen auf 17.

- **Folge:** Die Datenbank-Einrichtung beim Start brach ab. Übersprungen wurden Migrationsprüfung, Admin-Anlage und das Anwenden der Löschsperre. Die App lief trotzdem weiter, im kurzen Log fiel es nicht auf.
- **Behebung im Test:** `GRANT CREATE ON SCHEMA public TO mailarchiver_app;` (`tests/schritt5-fix.sql`).
- **Offen:** Anleitung im PR ergänzen und Härtung gegen PostgreSQL 17 testen. An den Code-Thread weitergegeben.

### Befund 2: Fehlendes Skript, App läuft bei fehlgeschlagener Einrichtung weiter

- Das in Meldung und Doku genannte Skript `doc/sql/MigrateV2610_2-archive_worm.sql` fehlte im Commit. Im Test durch ein inhaltsgleiches ersetzt (`tests/schritt8b.sql`).
- Die App startete trotz abgebrochener Datenbank-Einrichtung. Neue Mails hätten weiter 10 Jahre bekommen.
- In Commit `d7021b6` behoben (Skript nachgeliefert, App beendet sich). Abbruch am 06.10.2026 nachgetestet (9a); gemeldet: geordnet mit festem Fehlercode und klarer Meldung beenden.

### Befund 3: Kleinigkeiten in der Oberfläche

- Altes Popup „Soll der Account … gelöscht werden?“ erscheint vor der Hinweisseite.
- Seite „Aufbewahrung“ zeigt Zeiten in Weltzeit, Protokoll und Kopfzeile in deutscher Zeit.
- Abgelehnte Löschversuche stehen nicht im Zugriffsprotokoll.
- Protokolltexte englisch; Start-Eintrag nennt noch die abgeschaffte Einstellung `DeletionAllowed`.
- Erfolgsmeldungen erscheinen doppelt.

Alle an den Code-Thread gemeldet.

## Was damit belegt ist

- Der Hash wird von der Datenbank nachgerechnet; Größe, Zeitpunkt und Frist lassen sich nicht von außen vorgeben.
- Ein gespeichertes Original lässt sich vor Fristablauf weder ändern noch löschen, auch nicht über das Löschen der Mail (bisherige Lücke „Kontolöschung umgeht Sperre“ in [Fehlt](ist-stand-original.md#fehlt) ist für Mails mit Original geschlossen).
- Nach der Härtung kann die App die Schutzregeln nicht entfernen.

## Was noch nicht getestet ist

- Verhalten bei Aufbewahrungsregel, Einzel- und Mehrfachlöschung einer Mail mit Original (im Code vorhanden, hier nicht ausprobiert; Kontolöschung siehe Prüfung 7).
- Unabhängiger Vergleich des Originals mit der Mail auf dem Mailserver (der Hash belegt nur, dass seit dem Abholen nichts verändert wurde).
- Echter Löschlauf nach Fristablauf (erst 2035 beobachtbar, nur durch automatische Tests abgedeckt).
- Protokolleinträge für abgelehnte Löschversuche (über die Oberfläche nicht mehr auslösbar).
- EML/MBOX-Import und Microsoft-365-Konten: speichern noch kein Original. Mails ohne Original werden nie automatisch gelöscht; bei einer frischen Installation muss die Zahl auf der Seite „Aufbewahrung“ 0 sein.
- Restrisiko Datenbank-Superuser (`mailuser_test`): unverändert, siehe [Offen](architekturentscheidungen.md#offen).
- Positiver Nachweis, dass die Start-Einrichtung nach der Korrektur vollständig durchlief (gezählt wurden nur Fehlermeldungen: 0).
