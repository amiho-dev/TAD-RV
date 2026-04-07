# TAD-RV

TAD-RV ist eine Windows-Plattform fuer Klassenraum- und Endpoint-Management.
Sie besteht aus drei Hauptteilen:

- TADAdmin: Live-Uebersicht, Fernsicht, Sperren, Nachrichten, Filter
- TADDomainController: Deployment, Richtlinien, Update-Steuerung, Betriebsprotokolle
- TADBridgeService: Dienst auf den Clients, der Befehle annimmt und Status liefert

## Was ist neu im aktuellen Stand

- Einheitliches Release-Schema: `v26.04.XX.XXX`
- In-Place-Updater fuer alle Editionen (Admin, DC, Client-Service)
- Kritische Patches koennen als Force-Update markiert werden (ohne Bestaetigungsdialog)
- Admin-Dashboard mit neuer Settings-Flaeche und Game-Filter-Schaltern
- Domain-Controller mit One-Click-Optionen fuer USB-Block, Update-Push und Log-Einblick
- Erweiterte Passwort-Redaction-Fallbacks im RV-Stream

## GUI-First Bedienung

Alltaegliche Aufgaben sind auf GUI ausgelegt:

- Deployment im Domain Controller ueber die Seite Deploy
- Richtlinien ueber Schalter statt Bitmasken-CLI
- Updates ueber integrierte Update-Hinweise und Auto-Install fuer kritische Releases
- Troubleshooting ueber sichtbare Statuskarten und Log-Ansichten

## Update-Kanaele

Wir trennen zwei Linien klar:

- Stable: regulare Produktion (`v26.04.XX.XXX`)
- Beta-LTS: laenger stabilisierte Linie fuer sensible Umgebungen, eigener Tag-Kanal

Empfehlung:

- Standard-Schulen: Stable
- Pruefungs-/Langzeitlabore: Beta-LTS nach internem Testfenster

## Kompatibilitaet (Kurzfassung)

- Voll unterstuetzt: Windows 10/11 x64, 2+ Kerne
- Eingeschraenkt: ARM64 ueber Emulation
- LTS-only: Legacy-Hardware und alte OS-Linien
- Nicht unterstuetzt: Single-Core und veraltete Plattformen

Details: siehe `docs/Deployment-Guide.md` und Wiki-Kompatibilitaetsseite.

## Dokumentation

- `docs/Architecture.md`: Architektur in Klartext
- `docs/Deployment-Guide.md`: Rollout und Betriebsablauf
- `docs/Teacher-Guide.md`: Admin-/Lehrer-Workflows
- `docs/Console-Guide.md`: Domain-Controller-Workflows
- `.github/wiki/`: Kurzanleitungen fuer Betrieb und Release-Politik

## Lizenz

Proprietaer. Alle Rechte vorbehalten. (C) 2026 TAD Europe
