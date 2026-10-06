\pset pager off
\echo
\echo '=== Geschuetzten Speicher abschliessen: eigene Besitzer-Rolle, App darf nur lesen und anfuegen ==='
SELECT archive_worm.harden('mailarchiver_app');

\echo
\echo '=== Kontrolle (erwartet: Besitzer mailarchiver_worm_owner; Rechte der App je Tabelle INSERT, SELECT) ==='
SELECT tablename, tableowner FROM pg_tables WHERE schemaname = 'archive_worm' ORDER BY 1;
SELECT table_name, string_agg(privilege_type, ', ' ORDER BY privilege_type) AS rechte_der_app
FROM information_schema.role_table_grants
WHERE table_schema = 'archive_worm' AND grantee = 'mailarchiver_app' GROUP BY 1 ORDER BY 1;
SELECT rolname, rolsuper AS superuser FROM pg_roles WHERE rolname IN ('mailarchiver_app', 'mailarchiver_worm_owner') ORDER BY 1;
