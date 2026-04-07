# Build Guide

Dieses Projekt laesst sich auf Linux oder Windows bauen. Die Zielplattform ist Windows x64.

## Was gebaut wird

- TADAdmin
- TADDomainController
- TADBridgeService
- TADBootstrap und Setup/Updater-Tools

## Standardablauf

1. Abhaengigkeiten wiederherstellen.
2. Loesung in Release bauen.
3. Mit Build-Skript die Release-Artefakte erzeugen.

## Versionierung

Die Versionsstaende werden ueber die props-Dateien je Edition gepflegt:

- version-admin.props
- version-client.props
- version-dc.props

## CI-Hinweis

Build und Packaging koennen in einer Linux-CI laufen. Ausfuehrung der Binaries erfolgt auf Windows-Zielsystemen.
