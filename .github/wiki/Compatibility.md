# Compatibility Matrix

Diese Tabelle zeigt, welche Umgebungen fuer den produktiven Betrieb sinnvoll sind.

| Plattform | Status | Empfohlener Kanal | Hinweis |
| --- | --- | --- | --- |
| Windows 11 x64, moderne Intel/AMD CPU | Optimal | Stable | Beste Leistung fuer RV und Filter |
| Windows 10 x64, 2+ Kerne | Supported | Stable | Voll einsetzbar in Schulen |
| Windows 11 ARM64 (x64-Emulation) | Eingeschraenkt | Stable mit Testphase | Vorher Pilotklasse testen |
| Legacy-Hardware mit altem OS | LTS-only | Beta-LTS | Nur Basisfunktionen, keine neuen Features |
| Single-Core oder sehr alte Systeme | Nicht unterstuetzt | Kein Kanal | Setup sollte blockieren |

## Kanal-Regeln

- Stable: regulaere Updates und Bug-Fixes
- Beta-LTS: konservativer Kanal mit laengerem Testfenster und weniger Feature-Wechseln

## Entscheidungshilfe

- Wenn Betriebssicherheit wichtiger ist als neue Features: Beta-LTS
- Wenn neue Funktionen schnell gebraucht werden: Stable
