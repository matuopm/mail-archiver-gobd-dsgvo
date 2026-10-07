# Architekturentscheidungen

Zurück: [Index](README.md) · Ausgangslage: [Ist-Stand Mail Archiver](ist-stand-original.md) · Reihenfolge: [Roadmap](roadmap.md)

## E1 – Original-EML mit SHA-256 in eigenem Schema `archive_worm`

- Die vom Server geholten Bytes werden unverändert gespeichert, bevor irgendetwas geparst wird.
- Dazu SHA-256 über genau diese Bytes, Größe und Zeitstempel; der Hash landet zusätzlich in `ArchivedEmails.ContentHash`.
- Eigenes Schema, damit die Rechte getrennt vom Schema `mail_archiver` vergeben werden können.
- **Warum:** erfüllt Originalformat und macht Unveränderbarkeit prüfbar ([Grundsätze](gobd-anforderungen.md#grundsätze)).

## E2 – App-Rolle darf nur INSERT und SELECT

- Die Datenbankrolle der Anwendung bekommt auf `archive_worm` kein UPDATE, DELETE oder TRUNCATE.
- **Warum:** Die heutige Sperre beruht auf einer Spalte, die die App selbst zurücksetzen kann. Rechte auf Rollenebene kann die App nicht aushebeln, auch nicht bei einem Fehler oder einer kompromittierten Anwendung.

## E3 – Trigger als zweite Schicht

- Trigger auf `archive_worm` weisen UPDATE und DELETE ab, unabhängig von der Rolle.
- **Warum:** Schutz auch gegen falsch vergebene Rechte; die Fehlermeldung macht den Versuch sichtbar.

## E4 – Löschjob mit eigener Rolle

- Nur eine eigene Rolle darf löschen, und nur Einträge, deren Frist abgelaufen ist und die keine Löschsperre tragen. Jede Löschung wird protokolliert.
- **Warum:** einziger legitimer Löschweg, erfüllt das [Löschkonzept](dsgvo-anforderungen.md#löschkonzept), ohne die Sperre für alle zu öffnen. Kontolöschung läuft künftig ebenfalls nur über diesen Weg.

## E5 – Mail und EML in einer Transaktion

- Metadatenzeile und Original-EML werden gemeinsam geschrieben oder gar nicht.
- **Warum:** keine Mail ohne Original und kein Original ohne Mail; Vollständigkeit bleibt prüfbar.

## E6 – Später optional: MinIO mit Object Lock

- Ablage der EML in S3-kompatiblem Speicher im Compliance-Modus mit Aufbewahrungsdatum je Objekt.
- **Warum später:** PostgreSQL genügt für den Start und hält alles in einem Backup. Object Lock schützt zusätzlich gegen den Datenbank-Superuser und entlastet die Datenbank bei großen Beständen. Bei gehostetem Speicher AV-Vertrag nötig ([AV-Vertrag (Art. 28)](dsgvo-anforderungen.md#av-vertrag-art-28)).

## E7 – Kein Schalter: Originale werden immer gespeichert

- Entschieden vom Projektinhaber am 05.10.2026: Der Fork hat ausschließlich den GoBD-Zweck. Wer das nicht braucht, nimmt das Original-Projekt.
- Der zunächst gebaute Schalter `Compliance__StoreOriginalMime` (Standard aus) entfällt; das Speichern ist fest aktiv.
- **Folge:** Der Fork ist nicht mehr als optionale Erweiterung an das Original-Projekt zurückgebbar. Dafür kann niemand das Speichern versehentlich ausgeschaltet lassen.

## E8 – Altbestand wird nicht nachgerüstet

- Entschieden vom Projektinhaber am 05.10.2026: Eine Installation startet neu mit einem Postfach und archiviert, was dort liegt. Jede dabei abgeholte Mail bekommt ihr Original, egal wie alt sie ist.
- Archive, die schon ohne Original gelaufen sind, werden nicht nachträglich ergänzt. Wer umsteigt, setzt neu auf und lässt die Postfächer frisch abholen.
- Die Herkunft „rekonstruiert“ bleibt im Speicher möglich, wird aber nicht genutzt.

## E9 – Löschsperre fest, kein manuelles Löschen

- Entschieden vom Projektinhaber am 05.10.2026: `DeletionPolicy__DeletionAllowed` ist im Fork kein Schalter mehr. Manuelles Löschen von Mails wird ausgebaut.
- Gelöscht wird ausschließlich automatisch nach Fristablauf, mit Protokolleintrag ([Löschkonzept](dsgvo-anforderungen.md#löschkonzept)).

## E10 – Einheitliche Frist: 8 Jahre für alle Mails

- Entschieden vom Projektinhaber am 05.10.2026: keine Unterscheidung nach Dokumentart, keine Regeln je Postfach. Alle Mails 8 Jahre ab Ende des Kalenderjahres.
- **Warum:** Die App kann Belege nicht zuverlässig von Korrespondenz unterscheiden. 8 Jahre decken Buchungsbelege (8) und Handelsbriefe (6) ab; ein Fehler kann so nur zu „zu lange“ führen, nicht zu „zu früh gelöscht“.
- **Preis:** Reine Korrespondenz liegt 2 Jahre länger als gesetzlich nötig. Begründung für das Löschkonzept: Trennung technisch nicht zuverlässig möglich. Gehört in die [Verfahrensdokumentation](verfahrensdokumentation-muster.md) und sollte der Datenschutzbeauftragte des Kunden mittragen.
- **Grenze:** Unterlagen mit 10 Jahren Frist (Bücher, Jahresabschlüsse) dürfen nicht nur im Mail-Archiv liegen; sie gehören in die Buchhaltung.
- **Fristbeginn:** das Archivierungsdatum, nicht das Sende- oder Empfangsdatum (von der Projektinhaber am 05.10.2026 im Code-Thread entschieden). Frisch abgeholte alte Mails liegen dadurch länger als nötig, nie kürzer.
- **Löschpause:** Für laufende Prüfungen lässt sich das Löschen aussetzen (vom Projektinhaber entschieden, Ablaufhemmung, [Aufbewahrungsfristen](gobd-anforderungen.md#aufbewahrungsfristen)). Fristen lassen sich nur verlängern, nie verkürzen.

## E11 – Zugriffsprotokoll: nur anfügbar und verkettet

- Das Protokoll liegt im Schema `archive_worm`. Die App darf nur lesen und anfügen; Ändern, Löschen und Leeren weist die Datenbank ab.
- Jeder Eintrag enthält den SHA-256 seines Vorgängers. „Protokoll prüfen“ rechnet die Kette nach und nennt den ersten beschädigten Eintrag.
- **Warum beides:** Die Sperre verhindert Änderungen durch die App. Die Verkettung macht Eingriffe sichtbar, die am Schutz vorbei geschehen, etwa durch den Datenbank-Verwalter.
- Neue GoBD-Einträge werden als Ereignis gespeichert und in der Sprache des Betrachters angezeigt (Wunsch des Projektinhabers, 06.10.2026).

## E12 – Prüfer-Rolle: nur lesen, alles sehen

- Eigene Rolle „Prüfer (nur Lesen)“: sieht alle Postfächer ohne Zuweisung, darf suchen, öffnen, als EML exportieren und das Protokoll prüfen. Kein Wiederherstellen ins Postfach, keine Benutzer-, Konten- oder Aufbewahrungsverwaltung. Nicht mit Admin kombinierbar.
- Passt zu dem Anwendungsfall des Projektinhabers: Admin bindet alle Postfächer ein, ein Lesebenutzer schaut hinein.
- **Nicht gebaut:** Begrenzung auf Zeitraum oder einzelne Postfächer. Für eine Betriebsprüfung, die nur bestimmte Jahre betrifft, wäre das datenschutzrechtlich die sauberere Lösung.

## Offen

- Prüfer-Rolle auf Zeitraum oder Postfächer begrenzen (E12).
- Zugriffsprotokoll im Z3-Export mit fester Sprache.
- Restrisiko Datenbank-Superuser: organisatorisch regeln (Vier-Augen-Prinzip, getrennte Zugangsdaten) oder über E6.
