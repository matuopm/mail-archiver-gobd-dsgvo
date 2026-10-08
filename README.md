# 📧 Mail-Archiver GoBD

**E-Mail-Archiv für Selbstständige und kleine Unternehmen, nach bestem Wissen ausgelegt auf GoBD und DSGVO**

> [!WARNING]
> **In Entwicklung.** Dieser Fork ist mit Unterstützung von KI erstellt und kann Fehler enthalten. Er ist nach bestem Wissen und Gewissen auf die Anforderungen von GoBD und DSGVO ausgelegt, aber rechtlich nicht geprüft. Eine Zusicherung der Konformität gibt es nicht. Vor dem Einsatz bitte mit Steuerberatung und gegebenenfalls Datenschutzbeauftragten abstimmen.

Dieser Fork von [Mail-Archiver](https://github.com/s1t5/mail-archiver) speichert jede E-Mail zusätzlich im Original mit SHA-256-Prüfsumme in einem unveränderbaren Speicher, bewahrt sie 8 Jahre auf und löscht sie danach automatisch. Manuelles Löschen gibt es nicht. Zugriffe werden in einem geschützten Protokoll festgehalten, und für Betriebsprüfungen gibt es eine eigene Rolle nur zum Lesen.

Der Fork ist bewusst ein reiner GoBD-Modus ohne Schalter. Wer diese Einschränkungen nicht braucht, ist mit dem [Original-Projekt](https://github.com/s1t5/mail-archiver) besser bedient.

![Mail-Archiver Dashboard](Screenshots/dashboard.jpg)

## 🔏 Was dieser Fork anders macht

### Originale unveränderbar gespeichert
- Jede E-Mail wird beim Abruf zusätzlich byte-genau als Original (`.eml`) mit SHA-256 gespeichert, in einem eigenen Datenbankschema `archive_worm`.
- Die Datenbank selbst verhindert jede Änderung: Ändern und Leeren werden abgelehnt, Löschen erst nach Ablauf der Frist erlaubt, die Prüfsumme wird beim Speichern nachgerechnet.
- Das gilt für den IMAP-Abruf, für Microsoft-365-Postfächer und für den Import von EML- und MBOX-Dateien (Weboberfläche und Kommandozeile). Bei Microsoft 365 ist das Original die E-Mail, wie Microsoft sie ausliefert; Exchange setzt sie dafür neu zusammen.
- Optional lässt sich die Datenbank härten: Die Anwendung arbeitet dann mit einer eigenen Rolle, die im geschützten Speicher nur lesen und hinzufügen darf.
- Details: [doc/WormStorage.md](doc/WormStorage.md)

### Kein manuelles Löschen
- Archivierte E-Mails lassen sich nicht von Hand löschen, weder einzeln noch gesammelt.
- Ein Postfach mit archivierten E-Mails kann nicht gelöscht, nur deaktiviert werden.

### 8 Jahre Aufbewahrung mit automatischem Löschlauf
- Einheitliche Frist für alle E-Mails: 8 Jahre ab Ende des Jahres, in dem die E-Mail archiviert wurde.
- Ein täglicher Löschlauf entfernt E-Mails nach Ablauf der Frist samt Original und protokolliert das.
- Auf der Seite **Aufbewahrung** können Administratoren das Löschen pausieren, etwa während einer Betriebsprüfung (§ 147 Abs. 3 AO). Beginn und Ende einer Pause werden protokolliert.

### Geschütztes Zugriffsprotokoll
- Das Protokoll kann nur ergänzt, nicht geändert oder gelöscht werden.
- Jeder Eintrag ist per SHA-256-Kette mit dem vorherigen verbunden. Der Knopf **Protokoll prüfen** zeigt, ob die Kette lückenlos ist; auch die Prüfung selbst wird protokolliert.
- Protokolltexte erscheinen in der gewählten Sprache (Deutsch und Englisch vollständig).
- Details: [doc/Logs.md](doc/Logs.md)

### Rolle „Prüfer (nur Lesen)“
- Für Betriebsprüfer oder externe Prüfer: sieht alle Postfächer und das gesamte Protokoll, kann suchen, öffnen und exportieren.
- Kann nichts ändern, löschen, wiederherstellen oder importieren. Der Server lehnt alles ab, was nicht ausdrücklich als lesend freigegeben ist.
- Nicht mit Administrator kombinierbar.
- Details: [doc/UserManagement.md](doc/UserManagement.md)

## 📚 Projektdokumentation

Anforderungen aus GoBD und DSGVO, Architekturentscheidungen, Roadmap, Testprotokolle und eine Muster-Verfahrensdokumentation zum Ausfüllen stehen in [`doc/gobd/`](doc/gobd/README.md). Am bequemsten liest man sie in [Obsidian](https://obsidian.md): den Ordner `doc/gobd` herunterladen und über „Ordner als Vault öffnen“ laden.

## ✨ Weitere Funktionen (aus dem Original)

- Automatischer Abruf mehrerer Postfächer per IMAP, außerdem Microsoft 365 und private Microsoft-Konten
- Suche mit Filtern, Vorschau mit Anhängen, Export als MBOX oder gezippte EML
- Mehrere Benutzer mit Zuordnung zu Postfächern, Anmeldung auch per OpenID Connect
- Datenträgerüberlassung (Z3) als Export mit `INDEX.XML` ([doc/AuditExport.md](doc/AuditExport.md))
- Lesende REST-API und MCP-Server für Skripte und KI-Agenten ([doc/API.md](doc/API.md), [doc/MCP.md](doc/MCP.md))
- Mehrsprachige Oberfläche mit Dunkelmodus
- Wiederherstellen von E-Mails in ein Postfach und Löschen auf dem Mailserver nach einer einstellbaren Anzahl von Tagen (das Archiv bleibt davon unberührt)

Die übrige Dokumentation stammt größtenteils aus dem Original-Projekt und ist auf Englisch: [doc/Index.md](doc/Index.md).

## 🚀 Installation

Für eine Neuinstallation gibt es eine Docker-Compose-Vorlage mit gehärteter Datenbank im Ordner [`deploy/`](deploy/LIESMICH.md). Die Anleitung dort führt durch Zugangsdaten, ersten Start, Härtung und Kontrolle. Das Image wird aus diesem Repository gebaut; ein fertiges Image dieses Forks gibt es nicht.

Wichtig:
- Die Anwendung selbst bietet kein HTTPS. Davor gehört ein Reverse Proxy ([doc/ReverseProxy.md](doc/ReverseProxy.md)).
- Härtung, Updates mit Skripten unter `doc/sql/` und alle Hintergründe zum geschützten Speicher: [doc/WormStorage.md](doc/WormStorage.md)
- Alle Einstellungen: [doc/Setup.md](doc/Setup.md)

## ⚠️ Grenzen

- **Microsoft 365:** Das Speichern der Originale aus Microsoft-365-Postfächern ist nur mit automatischen Tests geprüft, noch nicht mit einem echten Postfach.
- **Ältere E-Mails:** E-Mails, die vor dieser Version archiviert wurden, haben kein Original. Sie werden auch nicht automatisch gelöscht.
- **Datensicherung:** Sicherung und Rücksicherung der gehärteten Datenbank sind nicht getestet. Vor dem Ernstfall einmal auf einem Testsystem durchspielen.
- **Datenbank-Superuser:** Wer Superuser-Rechte auf der Datenbank hat, kann den Schutz umgehen. Die Protokollkette macht Änderungen am Protokoll sichtbar, verhindert sie aber nicht.
- **Keine Rechtsberatung:** Die Software unterstützt eine GoBD-konforme Archivierung, ersetzt aber weder die eigene Verfahrensdokumentation noch die Abstimmung mit Steuerberatung oder Datenschutzbeauftragten.

## 🙏 Herkunft und Dank

Dieser Fork baut auf [Mail-Archiver](https://github.com/s1t5/mail-archiver) von **s1t5** auf. Fast alles, was die Anwendung kann, stammt von dort: Abruf, Suche, Oberfläche, Import, Export, API. Herzlichen Dank dafür!

Wer das Original-Projekt unterstützen möchte:

<a href="https://www.buymeacoffee.com/s1t5" target="_blank"><img src="https://img.shields.io/badge/Buy%20Me%20a%20Coffee-s1t5-FFDD00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black" alt="Buy Me a Coffee"></a>
<a href="https://ko-fi.com/s1t5dev" target="_blank"><img src="https://img.shields.io/badge/Ko--Fi-s1t5dev-FF5E5B?style=for-the-badge&logo=ko-fi&logoColor=white" alt="Ko-fi"></a>
<a href="https://github.com/sponsors/s1t5" target="_blank"><img src="https://img.shields.io/badge/GitHub%20Sponsors-s1t5-FF9A00?style=for-the-badge&logo=github-sponsors&logoColor=white" alt="GitHub Sponsors"></a>

Fehler und Wünsche zu den GoBD-Funktionen bitte als Issue in diesem Repository melden, nicht im Original-Projekt.

## 📄 Lizenz

GNU General Public License Version 3 (GPLv3), wie das Original-Projekt. Den vollständigen Text enthält die Datei [LICENSE](LICENSE).
