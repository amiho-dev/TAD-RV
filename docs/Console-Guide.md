# Domain Controller Guide

Der Domain Controller ist die Betriebszentrale fuer Rollout und Richtlinien.

## Dashboard

Hier sehen Sie auf einen Blick:

- Dienststatus
- Update-Status
- Grundlegende System- und Betriebsdaten

Bei kritischem Update wird ein klarer Hinweis angezeigt.

## Deploy-Seite

Wichtige Felder und Schalter:

- Service Publish Path
- Target Install Path
- Domain Controller Host
- Block USB storage for all student clients
- Queue force-update after deployment

Aktionen:

- Deploy Now
- Toggle USB Policy
- Push Updates
- Refresh Operational Logs

## Warum diese Seite relevant ist

Mit einer Maske koennen Sie zentrale Alltagsaufgaben erledigen:

- Rollout starten
- USB-Speicher zentral sperren/entsperren
- Update-Welle auf Clients ausloesen
- Login/Logoff/print-nahe Eintraege einsehen

## Troubleshooting

| Symptom | Vorgehen |
| --- | --- |
| Deployment bleibt haengen | Pfade, Rechte und AV-Ausnahmen pruefen |
| Clients uebernehmen Policy nicht | USB-/Update-Flags neu setzen und Logs aktualisieren |
| Keine aktuellen Ereignisse | Security/Application-Lesezugriff pruefen |
