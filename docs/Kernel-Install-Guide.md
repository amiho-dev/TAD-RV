# Kernel Install Guide

Dieses Dokument ist eine Kurzreferenz fuer den Kernel-Treiberbetrieb.

## Wann benoetigt

- Nur wenn Ihre Umgebung den Kernel-Teil aktiv nutzt.
- Fuer reine GUI-/Service-Demos kann Emulation ausreichen.

## Kernschritte

1. Treiber in einer WDK-faehigen Windows-Umgebung bauen.
2. Treiber signieren.
3. Vertrauenskette auf Zielgeraeten sicherstellen.
4. Installation auf Pilotgeraeten testen.
5. Erst danach breit ausrollen.

## Troubleshooting (Kurz)

- Treiber startet nicht: Signatur und Zertifikatsvertrauen pruefen.
- Dienst findet Treiber nicht: Installationspfad und Rechte pruefen.
- Instabilitaet: auf letzte stabile oder Beta-LTS-Linie zurueckgehen.
