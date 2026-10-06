# Mail-Archiver GoBD – Installationsvorlage

Docker-Compose-Vorlage für eine Neuinstallation mit gehärteter Datenbank. Am 06.10.2026 als Neuinstallation getestet (Schritte 2, 3 und 5 bestanden, 191 von 191 Mails mit Original). Datensicherung und Rücksicherung sind nicht getestet.

## Was die Vorlage anders macht als das Original-Projekt

- Die Anwendung meldet sich von Anfang an mit einer eigenen Rolle ohne Superuser-Rechte an.
- Die Datenbank ist von außen nicht erreichbar.
- Es gibt keine Schalter für Löschsperre oder Original-Speicherung: beides ist im Fork fest eingebaut.

## Inbetriebnahme

Alle Befehle im Verzeichnis `deploy/` dieses Repositorys ausführen. Das Image wird aus dem Repository gebaut (`build: context: ..`); wer ein fertiges Image nutzt, ersetzt den Block `build` in `docker-compose.yml`.

1. **Zugangsdaten:** `.env.beispiel` nach `.env` kopieren, drei verschiedene Passwörter setzen, Port wählen.
2. **Erster Start:**
   ```bash
   docker compose up -d --build
   docker logs --tail 30 mailarchiver-gobd-app
   ```
   Beim ersten Start legt die Datenbank die Anwendungsrolle an (`initdb/01-app-rolle.sh`), danach richtet die App ihre Tabellen selbst ein. Im Log darf keine Zeile mit `FATAL` stehen.
3. **Härten (einmalig, vor dem Einbinden des ersten Postfachs):**
   ```bash
   docker exec -i mailarchiver-gobd-db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < haerten.sql
   ```
   Erwartet: Besitzer `mailarchiver_worm_owner`, Rechte der App je Tabelle nur `INSERT, SELECT`, beide Rollen kein Superuser.
4. **Anmelden** auf `http://<server>:<WEB_PORT>` mit dem Administrator aus `.env`, Postfächer einbinden. Felder zur Löschung leer lassen.
5. **Kontrolle nach dem ersten Abruf:** Seite „Aufbewahrung“: „E-Mails ohne gespeichertes Original“ muss 0 sein. Seite „Protokoll“: „Protokoll prüfen“ muss grün melden.

## Updates

Ändert ein Update den geschützten Speicher, liegt unter `doc/sql/` ein Skript. Erst die App stoppen, das Skript als Datenbank-Verwalter ausführen, dann neu bauen und starten. Ohne das Skript beendet sich die App mit einer `FATAL`-Zeile und sagt, was fehlt.

## Datensicherung

Das Volume `pgdata` enthält alles: Mails, Originale, Protokoll. Sichern zum Beispiel mit
```bash
docker exec mailarchiver-gobd-db sh -c 'pg_dump -U "$POSTGRES_USER" -Fc "$POSTGRES_DB"' > sicherung.dump
```
Das Volume `keys` mitsichern (Anmelde-Schlüssel der Weboberfläche).

Für eine Rücksicherung in eine leere Datenbank müssen die Rollen `mailarchiver_app` und `mailarchiver_worm_owner` vorher existieren, sonst gehen Besitzer und Rechte verloren. Auf einem neuen Volume legt `initdb/01-app-rolle.sh` die Anwendungsrolle an, die Besitzerrolle fehlt dann noch:
```sql
CREATE ROLE mailarchiver_worm_owner NOLOGIN;
```
Danach `pg_restore` als Datenbank-Verwalter. Dieser Ablauf ist **nicht getestet**: Die Rücksicherung vor dem Ernstfall einmal auf einem Testsystem durchspielen.

## Entfernen

`docker compose down` hält die Daten. `docker compose down -v` löscht alle archivierten Mails unwiderruflich und ist während der Aufbewahrungsfrist nicht zulässig.

## Dazu gehört

Eine Verfahrensdokumentation (GoBD), die diese Installation beschreibt: wer betreibt sie, welche Postfächer werden archiviert, wie wird gesichert und geprüft. Hintergrund zum geschützten Speicher steht in [doc/WormStorage.md](../doc/WormStorage.md).
