# Verfahrensdokumentation (Muster)

Zurück: [Index](README.md) · Pflicht aus [Verfahrensdokumentation](gobd-anforderungen.md#verfahrensdokumentation) · Technik: [Architekturentscheidungen](architekturentscheidungen.md) · Nachweise: [Testprotokoll 2026-10-05](testprotokoll-2026-10.md)

> [!WARNING]
> **Entwurf, Stand 06.10.2026**
> Muster zum Ausfüllen je Kunde. Stellen in «Klammern» sind zu ergänzen. Beschreibt den Fork mit allen fünf Pull Requests (Stand PR #5). Rechtlich nicht geprüft; vor Verwendung mit Steuerberater und, falls vorhanden, Datenschutzbeauftragtem abstimmen.

## 1 Allgemeine Beschreibung

| | |
|---|---|
| Unternehmen | «Firma, Rechtsform, Anschrift» |
| Verantwortlich für das Verfahren | «Name, Funktion» |
| Technischer Betrieb | «Name oder Dienstleister» |
| Gültig ab / Version | «Datum» / «Nr.» |

**Zweck:** Erfüllung der Aufbewahrungspflichten für geschäftliche E-Mails nach § 147 AO und § 257 HGB. Rechtsgrundlage der Verarbeitung ist Art. 6 Abs. 1 lit. c DSGVO.

**Geltungsbereich:** alle dienstlichen Postfächer des Unternehmens: «Liste der Mitarbeiter- und Sammelpostfächer». Nicht eingebunden werden: «falls vorhanden: Betriebsrat, Betriebsarzt, Datenschutzbeauftragter».

**Eingesetzte Software:** Mail-Archiver, GoBD-Fork (`matuopm/mail-archiver-gobd-dsgvo`), Version «Commit oder Versionsnummer», Datenbank PostgreSQL «Version».

## 2 Ablauf aus Sicht der Anwender

- Mitarbeiter arbeiten wie gewohnt in ihrem Mailprogramm. Sie müssen nichts tun und können die Archivierung nicht beeinflussen.
- Das Archiv holt alle «15» Minuten neue Mails aus jedem eingebundenen Postfach ab, ein- und ausgehende, aus allen Ordnern außer «ausgeschlossene Ordner, z. B. Kalender, Kontakte».
- Eine Mail, die ein Mitarbeiter zwischen zwei Abrufen empfängt und sofort endgültig löscht, wird nicht erfasst. Maßnahme dagegen: «z. B. serverseitige Kopie aller Mails in ein Sammelpostfach, das ebenfalls archiviert wird».
- Im Archiv arbeitet nur der Administrator. Mitarbeiter haben keinen Zugang, außer «Ausnahmen».

## 3 Technische Beschreibung

### 3.1 Erfassung im Original
Das Archiv ruft jede Mail per IMAP als unveränderte Bytefolge ab. In einem einzigen Schritt werden gespeichert:
- die durchsuchbare Fassung (Betreff, Text, Anhänge),
- das Original als `.eml` im geschützten Speicher,
- der SHA-256-Hash des Originals, Zeitpunkt der Erfassung und Ende der Aufbewahrungsfrist.

Schlägt ein Teil fehl, wird nichts gespeichert. Es gibt keine per IMAP archivierte Mail ohne Original.

### 3.2 Unveränderbarkeit
Die Datenbank selbst setzt die Regeln durch, unabhängig von der Anwendung:
- Beim Speichern rechnet sie den Hash nach und setzt Größe, Zeitpunkt und Fristende selbst. Vorgaben von außen werden überschrieben.
- Ändern eines Originals wird immer abgelehnt.
- Löschen wird bis zum Fristende abgelehnt, auch wenn die zugehörige Mail oder das ganze Postfach gelöscht werden soll.
- Die Anwendung meldet sich mit einer Rolle an, die im geschützten Speicher nur lesen und anfügen darf. Die Schutzregeln kann sie nicht entfernen.

### 3.3 Aufbewahrungsfrist und Löschung
- Einheitliche Frist: 8 Jahre ab Ende des Kalenderjahres der Archivierung. Begründung: Buchungsbelege (8 Jahre) lassen sich technisch nicht zuverlässig von Handelsbriefen (6 Jahre) trennen; die längere Frist gilt für alle.
- Unterlagen mit 10 Jahren Frist (Bücher, Jahresabschlüsse) werden in «Buchhaltungssystem» aufbewahrt, nicht allein im Mail-Archiv.
- Das Unternehmen ist kein Kreditinstitut, keine Versicherung und kein Wertpapierinstitut; für diese gilt bei Buchungsbelegen eine Frist von 10 Jahren (§ 257 Abs. 4 Satz 2 HGB).
- Nach Fristende löscht ein täglicher Lauf Mail, Anhänge und Original und vermerkt dies im Protokoll.
- Manuelles Löschen einzelner Mails ist nicht möglich.
- Bei Betriebsprüfung oder Rechtsstreit hält der Administrator die Löschung mit Angabe des Grundes an (Seite „Aufbewahrung“). Beginn und Ende stehen im Protokoll.

### 3.4 Zugriffsprotokoll
- Protokolliert werden Anmeldungen, Suchen, das Öffnen und Herunterladen von Mails, Kontoänderungen, Löschläufe und Löschpausen, jeweils mit Benutzer und Zeitpunkt.
- Das Protokoll ist nur anfügbar. Jeder Eintrag enthält den Hash seines Vorgängers; eine nachträgliche Änderung bricht die Kette.
- Die Funktion „Protokoll prüfen“ rechnet die Kette nach und nennt den ersten veränderten Eintrag.

### 3.5 Rollen

| Rolle | Darf | Darf nicht |
|---|---|---|
| Administrator «Name» | Postfächer einbinden, Benutzer verwalten, suchen, lesen, exportieren, Löschpause setzen | Mails ändern oder löschen |
| Prüfer (nur Lesen) | alle Postfächer durchsuchen, lesen, als EML exportieren, Protokoll einsehen und prüfen | verwalten, Mails ins Postfach zurückkopieren, Prüfdaten-Export |
| Datenbank-Verwalter «Name» | Wartung, Updates der Datenbankstruktur | (technisch alles, siehe 5) |

## 4 Betrieb

| | |
|---|---|
| Standort, Hardware | «Server, Ort» |
| Installation | Docker, Konfiguration in «Pfad» |
| Datensicherung | «Verfahren, Häufigkeit, Aufbewahrung der Sicherungen, Ort» |
| Wiederherstellung getestet am | «Datum» |
| Updates | durch «Name». Ändert ein Update den geschützten Speicher, führt der Datenbank-Verwalter vorher das mitgelieferte Skript aus. Ohne dieses Skript startet die Anwendung nicht und meldet den Grund. |
| Überwachung | «wie Ausfälle des Abrufs bemerkt werden» |

Die Datensicherung muss den geschützten Speicher einschließen. Sicherungen werden nicht länger aufbewahrt als «Zeitraum», damit gelöschte Mails nicht dauerhaft in Sicherungen verbleiben.

## 5 Internes Kontrollsystem

1. **Vollständigkeit:** «monatlich» prüft der Administrator auf der Seite „Aufbewahrung“, dass die Zahl „E-Mails ohne gespeichertes Original“ 0 ist, und auf der Kontenseite, dass jedes Postfach aktuell synchronisiert wurde.
2. **Protokoll:** «monatlich» „Protokoll prüfen“ ausführen und den angezeigten Prüfwert mit Datum außerhalb des Systems notieren («wo»). Ein späterer Vergleich zeigt, ob das Protokoll bis zu diesem Stand unverändert ist.
3. **Zugriffe:** «vierteljährlich» Durchsicht des Protokolls auf ungewöhnliche Zugriffe durch «Name, nicht der Administrator selbst».
4. **Datenbank-Verwalter:** Der Datenbank-Verwalter kann die Schutzregeln technisch umgehen. Maßnahmen: getrennte Zugangsdaten, Kenntnis nur bei «Name», Zugriff nur nach Vier-Augen-Prinzip. Eingriffe am Protokoll werden durch Kontrolle 2 erkannt.

## 6 Datenschutz

- Private Nutzung der dienstlichen Postfächer ist untersagt seit «Datum», geregelt in «Richtlinie oder Arbeitsvertrag».
- Die Mitarbeiter wurden am «Datum» über Archivierung, Zugriffsberechtigte und Frist informiert (Art. 13 DSGVO).
- «Falls vorhanden: Betriebsvereinbarung vom …»
- Auftragsverarbeitung: «keine, Betrieb auf eigener Hardware» oder «Vertrag mit … vom …».
- Die Verarbeitung ist im Verzeichnis von Verarbeitungstätigkeiten eingetragen: «Nr.».
- Löschanträge Betroffener werden während der Frist unter Hinweis auf Art. 17 Abs. 3 lit. b DSGVO abgelehnt.
- Die einheitliche Frist von 8 Jahren überschreitet für reine Korrespondenz die gesetzliche Pflicht um 2 Jahre. Begründung siehe 3.3.

## 7 Zugriff der Finanzverwaltung

| Art | Umsetzung |
|---|---|
| Z1, unmittelbar | Der Prüfer erhält ein Konto mit der Rolle „Prüfer (nur Lesen)“ für die Dauer der Prüfung und nutzt es vor Ort an einem Gerät des Unternehmens; ein Fernzugriff der Finanzbehörde ist nicht vorgesehen (GoBD Rz. 165). Es wird von «Name» angelegt und nach Abschluss deaktiviert. Die Rolle sieht alle Postfächer und Zeiträume. |
| Z2, mittelbar | Der Administrator sucht und exportiert nach Vorgabe des Prüfers. |
| Z3, Datenträger | „Prüfdaten-Export“ erzeugt ein ZIP mit `INDEX.XML`, Mail- und Anhangstabelle für den gewählten Zeitraum. Einzelne Mails werden als `.eml` exportiert. |

## 8 Bekannte Grenzen

- Mails aus Microsoft-365-Konten (Anbindung über die Microsoft-Schnittstelle statt IMAP) erhalten derzeit kein Original und keine Frist. Dieser Weg wird bei «Kunde» nicht genutzt.
- Importierte Dateien (EML, MBOX) werden mit Original, Hash und Frist gespeichert und tragen die Herkunft „Import“. Ihre Beweiskraft ist geringer als bei direkt abgeholten Mails, weil der Zustand der Datei vor dem Import nicht belegbar ist. Importiert wurde bei «Kunde»: «nichts / Bestand X am Datum».
- Der Hash belegt die Unverändertheit ab dem Abruf durch das Archiv. Was vorher auf dem Mailserver geschah, liegt außerhalb des Verfahrens.
- Die Prüfer-Rolle lässt sich nicht auf einzelne Postfächer oder Zeiträume begrenzen.
- Der automatische Löschlauf wurde durch Programmtests geprüft; im Betrieb fällt die erste Löschung «Jahr» an.

## 9 Änderungshistorie

| Version | Datum | Änderung | Freigegeben von |
|---|---|---|---|
| «1.0» | «Datum» | Erstfassung | «Name» |

Frühere Fassungen werden so lange aufbewahrt wie die Mails, die unter ihnen archiviert wurden.
