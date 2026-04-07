# Internationalization Guide

TAD-RV verwendet eine gemeinsame i18n-Basis fuer Domain Controller und Admin UI.

## Grundprinzip

- Texte werden ueber Sprachkeys gepflegt.
- Sprache kann direkt im GUI gewechselt werden.
- Sprachwahl bleibt zwischen Sitzungen erhalten.

## Beste Praxis fuer neue Texte

- Immer einen klaren Key verwenden.
- Keine harten Texte in Logikfunktionen lassen.
- Kurze, alltagsnahe Formulierungen verwenden.

## Neue Sprache hinzufuegen

1. Sprachdatei auf Basis einer bestehenden Sprache anlegen.
2. Key-Set vollstaendig uebersetzen.
3. Ressource in den betroffenen Projekten einbinden.
4. Im GUI testen (Navigation, Buttons, Hinweise, Fehlermeldungen).
