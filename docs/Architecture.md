# TAD-RV Architektur (Klartext)

Dieses Dokument erklaert, wie die Komponenten zusammenarbeiten, ohne tiefe Treiberdetails.

## Komponenten und Rollen

| Komponente | Aufgabe |
| --- | --- |
| TADAdmin | Unterrichtsoberflaeche mit Live-Ansicht, Sperren und Filtern |
| TADDomainController | Zentrale Verwaltung fuer Deployment, Richtlinien, Updates und Logs |
| TADBridgeService | Client-Dienst, der Befehle ausfuehrt und Status meldet |
| Shared-Bibliothek | Gemeinsames Protokoll, Update- und Lizenzlogik |

## Datenfluss in der Praxis

1. Der Domain Controller setzt Rollout- und Policy-Zustand.
2. Der Client-Dienst nimmt Richtlinien und Befehle an.
3. TADAdmin zeigt Live-Zustand und sendet Unterrichtsbefehle.
4. Kritische Betriebsereignisse landen in Logs und koennen zentral eingesehen werden.

## Update-Architektur

- Alle Editionen nutzen den gleichen Updater-Ansatz.
- Normale Updates zeigen Verfuegbarkeit und koennen reguler installiert werden.
- Force-Updates sind fuer kritische Patches gedacht und koennen ohne Benutzerabfrage starten.

## Datenschutz bei Remote View

- Vor dem Versand werden sensible Bereiche redigiert.
- Neben UIA-Erkennung gibt es einen Fallback fuer typische Login-/Passwortfenster.
- Ziel ist: keine rohen Zugangsdaten im Stream.

## Domain-Controller als Betriebszentrale

Die Deploy-Ansicht ist auf reale Betriebsaufgaben ausgerichtet:

- One-Click Deployment
- USB-Block policy setzen
- Client-Update-Push anstossen
- Betriebsprotokolle aktualisieren

## Emulation

Fuer Demo und Entwicklung kann der Service emuliert laufen. So lassen sich GUI-Flows testen, auch ohne komplette Produktionsumgebung.
- CI/CD integration testing

---

*See also: [Deployment-Guide.md](Deployment-Guide.md) · [Signing-Handbook.md](Signing-Handbook.md) · [Teacher-Guide.md](Teacher-Guide.md)*
