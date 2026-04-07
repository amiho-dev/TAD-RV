# Signing Handbook

Dieses Dokument beschreibt den empfohlenen Signaturprozess in einfacher Form.

## Ziel

- Treiber und relevante Artefakte so signieren, dass Rollouts stabil und nachvollziehbar bleiben.

## Empfohlener Ablauf

1. Signaturzertifikat in sicherer Umgebung verwalten.
2. Treiber und Katalog signieren.
3. Signatur vor Rollout auf Referenzgeraet pruefen.
4. Zertifikatsvertrauen per Unternehmensrichtlinie verteilen.
5. Rollout in Pilotgruppe starten und Ereignisse beobachten.

## Betriebshinweise

- Private Schluessel nie im Repository ablegen.
- Fuer Schulen mit AD empfiehlt sich zentral verwaltetes Vertrauensmanagement.
- Bei Signaturproblemen zuerst Zertifikatskette und Ablaufdatum pruefen.
