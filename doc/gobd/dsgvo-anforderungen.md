# DSGVO-Anforderungen

Zurück: [Index](README.md) · Gegenstück: [GoBD-Anforderungen](gobd-anforderungen.md)

> [!NOTE]
> **Gegen Quellen geprüft am 07.10.2026**
> Die zitierten Artikel der DSGVO (5, 6, 13, 15, 17, 28, 30, 32), § 87 BetrVG und § 38 BDSG sind mit den Gesetzestexten abgeglichen. Die Einordnung, was daraus für ein Mail-Archiv folgt, bleibt meine Auslegung und keine Rechtsberatung. Fundstellen: [Rechtsquellen](quellen-und-links.md#rechtsquellen).

**Rechtsgrundlage der Archivierung:** Art. 6 Abs. 1 lit. c DSGVO (rechtliche Verpflichtung aus AO und HGB). Sie trägt nur so weit und so lange, wie die Aufbewahrungspflicht reicht.

## Löschkonzept

- Speicherbegrenzung (Art. 5 Abs. 1 lit. e): Nach Fristablauf entfällt die Rechtsgrundlage, die Mail muss gelöscht werden.
- Löschung automatisch nach einheitlich 8 Jahren ab Jahresende ([Architekturentscheidungen](architekturentscheidungen.md) E10). Kein manuelles Löschen, kein „freiwilliges“ Weiteraufbewahren ohne eigenen Grund.
- Jede Löschung wird protokolliert (was, wann, aufgrund welcher Regel), ohne den Inhalt zu protokollieren.
- Löschen heißt vollständig: Original-EML, Datenbankzeile, Anhänge, Suchindex. Backups laufen über ihre eigene Aufbewahrungszeit aus.
- Löschanträge Betroffener (Art. 17) greifen während der Frist nicht (Art. 17 Abs. 3 lit. b). Stattdessen: Verarbeitung auf die Aufbewahrung beschränken.
- Ausnahme vom Löschen: manuelle Löschsperre bei laufender Prüfung oder Rechtsstreit.

## Zugriffsschutz (Art. 32)

- Rollen nach dem Need-to-know-Prinzip: Nutzer sieht nur zugewiesene Postfächer, Admin verwaltet, Prüfer liest nur. Die Prüfer-Rolle im Fork sieht ohne Einstellung alle Postfächer und Zeiträume; sie lässt sich auf Postfächer und einen Prüfzeitraum begrenzen (PR #12, getestet am 09.10.2026, im Hauptzweig; [Architekturentscheidungen](architekturentscheidungen.md) E12).
- Zwei-Faktor-Anmeldung für privilegierte Konten.
- Jeder Zugriff auf Inhalte wird protokolliert; das Protokoll selbst ist geschützt (Schritt 3 der [Roadmap](roadmap.md)).
- Verschlüsselung bei Transport und Ablage, getestete Backups.
- Auskunftsersuchen (Art. 15) müssen aus dem Archiv bedienbar sein: Suche über alle Postfächer durch berechtigte Rolle.

## Privatnutzungsverbot

Ist private E-Mail-Nutzung erlaubt oder geduldet, landen private Mails im Archiv. Dafür gibt es keine Rechtsgrundlage, und die Sperre verhindert das Löschen. Deshalb: **private Nutzung der dienstlichen Postfächer schriftlich untersagen** (Richtlinie oder Arbeitsvertrag) und das Verbot auch durchsetzen, sonst entsteht eine Duldung.

## Betriebsrat

- Archiv und Zugriffsprotokoll sind zur Verhaltens- und Leistungskontrolle geeignet: Mitbestimmung nach § 87 Abs. 1 Nr. 6 BetrVG, üblich ist eine Betriebsvereinbarung.
- Postfächer mit besonderer Vertraulichkeit (Betriebsrat, Betriebsarzt, Datenschutzbeauftragter) von der Archivierung ausnehmen.
- Ohne Betriebsrat: Beschäftigte transparent informieren (Art. 13).

## AV-Vertrag (Art. 28)

Nötig, sobald ein Dritter Zugriff auf die Daten haben kann: Hoster, externer Admin, Cloud-Speicher (z. B. gehostetes S3/MinIO), Backup-Dienst. Bei reinem Eigenbetrieb auf eigener Hardware entfällt er.

## Dokumentation

- Eintrag im Verzeichnis von Verarbeitungstätigkeiten (Art. 30).
- Technische und organisatorische Maßnahmen beschreiben; deckt sich weitgehend mit der [Verfahrensdokumentation](verfahrensdokumentation-muster.md).

## Abgleich

Was davon technisch fehlt, steht in [Fehlt](ist-stand-original.md#fehlt).
