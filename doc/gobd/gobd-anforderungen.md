# GoBD-Anforderungen

Zurück: [Index](README.md) · Gegenstück: [DSGVO-Anforderungen](dsgvo-anforderungen.md)

Grundlagen: § 147 AO, §§ 238, 257 HGB, GoBD (BMF-Schreiben vom 28.11.2019, GZ IV A 4 - S 0316/19/10003 :001, geändert am 11.03.2024 und 14.07.2025).

> [!NOTE]
> **Gegen Quellen geprüft am 07.10.2026**
> Die Angaben dieser Notiz sind mit den Gesetzestexten auf gesetze-im-internet.de und mit dem GoBD-Schreiben von 2019 abgeglichen (Randziffern in Klammern). Die Änderungen von 2024 und 2025 lagen nur als Zusammenfassung aus zweiter Hand vor. Fundstellen: [Rechtsquellen](quellen-und-links.md#rechtsquellen). Das ersetzt keine Rechtsberatung.

## Was aufbewahrt werden muss

Maßgeblich sind Inhalt und Funktion, nicht die Bezeichnung. E-Mails mit der Funktion eines **Handels- oder Geschäftsbriefs** oder eines **Buchungsbelegs** sind in elektronischer Form aufbewahrungspflichtig (GoBD Rz. 121). Dient eine Mail nur als „Transportmittel“, etwa für eine angehängte Rechnung, und enthält sonst nichts Aufbewahrungspflichtiges, ist sie selbst nicht aufbewahrungspflichtig, wie früher der Briefumschlag (Rz. 121). In der Praxis lässt sich das nicht sauber trennen, deshalb wird alles Geschäftliche archiviert.

Handelsbriefe sind nur Schriftstücke, die ein Handelsgeschäft betreffen (§ 257 Abs. 2 HGB).

## Grundsätze

| Grundsatz | Bedeutung für das Archiv | Fundstelle |
|---|---|---|
| Vollständigkeit | Jede ein- und ausgehende Mail wird erfasst, bevor ein Nutzer sie löschen kann. Lücken müssen erkennbar sein. | § 146 Abs. 1 AO, GoBD 3.2.1 |
| Unveränderbarkeit | Elektronisch Eingegangenes darf vor Fristablauf nicht gelöscht werden und muss unveränderbar erhalten bleiben. Erreichbar durch Hardware, Software (Sperren, Festschreibung, Protokollierung) oder Organisation (Berechtigungen). Bloße Ablage im Dateisystem genügt regelmäßig nicht. | GoBD Rz. 119, 110 |
| Originalformat | Was elektronisch eingeht, ist auch in dieser Form aufzubewahren, nicht nur als Ausdruck. Wird eine Mail als PDF gespeichert, gehen die Header verloren, und der Zugang ist nicht mehr nachvollziehbar; das geht zu Lasten des Steuerpflichtigen. Deshalb die unveränderte `.eml` samt Headern und Anhängen. | GoBD Rz. 119, 133; Abschnitt 9.1 (Hinweis zum PDF, im Bereich Rz. 129) |
| Nachvollziehbarkeit | Eingang, Archivierung und weitere Verarbeitung elektronischer Unterlagen sind zu protokollieren. | GoBD Rz. 117 |
| Verfügbarkeit | Während der Frist jederzeit verfügbar, unverzüglich lesbar und maschinell auswertbar. | § 147 Abs. 2 Nr. 2 AO |
| Ordnung | Geordnete Aufbewahrung; ein bestimmtes Ordnungssystem ist nicht vorgeschrieben. Eindeutiger Index je Dokument. | § 147 Abs. 1 AO, GoBD Rz. 117, 122 |

## Aufbewahrungsfristen

| Dokumentart | Frist | Grundlage |
|---|---|---|
| Handels- und Geschäftsbriefe (empfangen und abgesandt) | 6 Jahre | § 147 Abs. 1 Nr. 2, 3, Abs. 3 AO; § 257 Abs. 1 Nr. 2, 3, Abs. 4 HGB |
| Buchungsbelege (z. B. Rechnungen) | 8 Jahre | § 147 Abs. 1 Nr. 4, Abs. 3 AO; § 257 Abs. 1 Nr. 4, Abs. 4 HGB |
| Bücher, Inventare, Jahresabschlüsse, Lageberichte | 10 Jahre | § 147 Abs. 1 Nr. 1, Abs. 3 AO; § 257 Abs. 1 Nr. 1, Abs. 4 HGB |

- **Fristbeginn:** mit dem Schluss des Kalenderjahres, in dem der Brief empfangen oder abgesandt wurde bzw. der Beleg entstanden ist (§ 147 Abs. 4 AO, § 257 Abs. 5 HGB). Eine Mail vom 03.02.2026 mit 6 Jahren Frist darf frühestens ab 01.01.2033 gelöscht werden.
- **Ablaufhemmung:** Die Frist läuft nicht ab, soweit und solange die Unterlagen für Steuern von Bedeutung sind, deren Festsetzungsfrist noch nicht abgelaufen ist (§ 147 Abs. 3 Satz 5 AO). Das Archiv braucht deshalb eine Löschpause.
- **GmbH:** Sie gilt als Handelsgesellschaft (§ 13 Abs. 3 GmbHG); § 257 HGB gilt damit unabhängig von der Größe.
- **Ausnahme Finanzbranche:** Kreditinstitute, Versicherungen und Wertpapierinstitute müssen Buchungsbelege handelsrechtlich 10 Jahre aufbewahren (§ 257 Abs. 4 Satz 2 HGB). Für solche Kunden passt die einheitliche Frist von 8 Jahren nicht.

Umsetzung: Im Fork gilt einheitlich die Frist für Buchungsbelege, 8 Jahre für alle Mails ([Architekturentscheidungen](architekturentscheidungen.md) E10), Schritt 4 der [Roadmap](roadmap.md).

## Prüferzugriff (§ 147 Abs. 6 AO)

| Art | Inhalt | Umsetzung |
|---|---|---|
| Z1 | Unmittelbarer Zugriff: Die Finanzbehörde nimmt selbst im Nur-Lesezugriff Einsicht und nutzt dafür Hard- und Software des Unternehmens. Eine Fernabfrage durch die Finanzbehörde ist ausgeschlossen (GoBD Rz. 165). | Prüfer-Rolle, Nutzung vor Ort am Gerät des Unternehmens |
| Z2 | Mittelbarer Zugriff: Das Unternehmen wertet nach Vorgabe des Prüfers maschinell aus (GoBD Rz. 168 ff.). | Suche und Export durch Admin |
| Z3 | Datenträgerüberlassung: Daten in maschinell auswertbarem Format (GoBD nach Rz. 170). | Prüfdaten-Export, siehe [Vorhanden](ist-stand-original.md#vorhanden) |

## Verfahrensdokumentation

Für jedes DV-System muss eine übersichtlich gegliederte Verfahrensdokumentation vorhanden sein (GoBD Rz. 151). Sie besteht in der Regel aus allgemeiner Beschreibung, Anwenderdokumentation, technischer Systemdokumentation und Betriebsdokumentation (Rz. 153). Änderungen müssen versioniert und nachvollziehbar sein; sie ist so lange aufzubewahren wie die Unterlagen, zu deren Verständnis sie nötig ist (Rz. 154).

Fehlt sie oder ist sie ungenügend, ist das allein kein Grund, die Buchführung zu verwerfen, solange Nachvollziehbarkeit und Nachprüfbarkeit nicht beeinträchtigt sind (Rz. 155). Bei einem Mail-Archiv ist die Nachprüfbarkeit ohne Beschreibung aber kaum gegeben. Muster: [Verfahrensdokumentation](verfahrensdokumentation-muster.md).

## Abgleich

Welche Punkte der Mail Archiver bereits erfüllt, steht in [Ist-Stand Mail Archiver](ist-stand-original.md).
