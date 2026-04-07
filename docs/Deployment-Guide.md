# Deployment Guide

Dieses Dokument beschreibt den produktiven Rollout mit GUI-Fokus.

## Voraussetzungen

- Windows 10/11 auf Zielgeraeten
- Admin-Rechte auf Deployment-Rechner
- Erreichbare Client-Rechner im Schulnetz

## Empfohlener Rollout im Domain Controller

1. Domain Controller starten.
2. Deploy-Seite oeffnen.
3. Service-Pfad und Zielordner eintragen.
4. Optional aktivieren:
   - USB-Block fuer Schueler
   - Force-Update-Push nach Deployment
5. Deploy Now klicken.
6. Fortschritt und Ergebnis im Deployment-Log kontrollieren.

## Nachkontrolle

- Dashboard meldet aktive Dienste.
- Admin-Ansicht zeigt verbundene Endpunkte.
- Testbefehl (Message oder Lock) wird auf Clients wirksam.

## Update-Verhalten

- Alle Editionen koennen In-Place aktualisieren.
- Kritische Releases koennen als Force-Update markiert werden.
- Fuer sensible Umgebungen Beta-LTS-Kanal nutzen.

## Troubleshooting

| Problem | Pruefung |
| --- | --- |
| Client nicht sichtbar | Netzwerksegment, Service-Status, Firewall |
| Deployment stoppt | Pfade, Rechte, Antivirus-Ausnahmen |
| Policy greift nicht | Richtlinienstatus aktualisieren, Log-Ansicht pruefen |
| Update kommt nicht an | Release-Metadaten, Repo-Quelle, Konnektivitaet |

## Betriebstipps

- Erst in einer Pilotklasse ausrollen, dann breit verteilen.
- Vor Ferien und Pruefungen nur stabile oder Beta-LTS Freigaben nutzen.
- Betriebslogs regelmaessig exportieren.
