# Installation & Requirements

## Mindestanforderungen

- OS: Windows 10 oder Windows 11
- CPU: mindestens 2 Kerne
- Speicher: mindestens 100 MB frei
- Updates: keine offenen kritischen Windows-Updates

## Empfohlener Ablauf (GUI)

1. Domain Controller starten.
2. Auf die Seite Deploy wechseln.
3. Service-Pfad und Zielpfad eintragen.
4. Optional aktivieren:
	- USB-Block fuer Schueler
	- Update-Push nach Deployment
5. Deploy Now klicken.
6. Deployment-Log pruefen.

## Nach der Installation pruefen

- Im Dashboard ist der Service online.
- Client-Endpunkte erscheinen im Admin-Grid.
- Ein Testbefehl (z. B. Nachricht) kommt am Client an.

## Wenn etwas nicht funktioniert

- Netzwerksegment pruefen (Admin und Clients im gleichen VLAN/LAN)
- Firewall-Regeln fuer Discovery/Control pruefen
- Im Domain Controller die Betriebsprotokolle aktualisieren
