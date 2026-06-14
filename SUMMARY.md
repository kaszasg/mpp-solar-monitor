# Solar Inverter Monitor - .NET 8

MPP Solar PIP 11kW Max inverter monitorozó alkalmazás .NET Core 8 platformra.
Az inverter adatait soros porton, Ethernet/soros átalakítón keresztül olvassa ki (TCP socket),
majd az értékeket OpenHAB REST API-n keresztül továbbítja.

## Architektúra

| Fájl | Szerep |
|---|---|
| `Program.cs` | Host konfiguráció, DI, HTTP resilience, logolás beállítása |
| `SolarOptions.cs` | Konfigurációs osztály (`appsettings.json` -> `Solar` szekció) |
| `InverterClient.cs` | TCP kommunikáció az inverterrel |
| `OpenHabClient.cs` | OpenHAB REST API kliens |
| `SolarWorker.cs` | `BackgroundService` - fő lekérdezési ciklus |
| `solar.service` | Systemd service unit fájl |

## Inverter kommunikáció (`InverterClient`)

- TCP kapcsolat az Ethernet/soros átalakítón keresztül (alapértelmezett: `192.168.17.22:4196`)
- Minden parancs önálló TCP kapcsolatot nyit (connect, send, receive, close)
- Parancsok `\r` terminátorral, válasz `\r\n`-ig olvasva
- Checksum: az ASCII karakterkódok összege mod 256, 3 jegyűre nullázva
- **Throttling**: `SemaphoreSlim` + minimum 1 másodperc várakozás parancsok között
  (az inverter nem kezel egyidejű kéréseket)

## OpenHAB integráció (`OpenHabClient`)

- HTTP POST `text/plain` tartalommal az egyes item-ekre
- `Microsoft.Extensions.Http.Resilience` (`AddStandardResilienceHandler`):
  - Max 3 újrapróbálkozás, 2 másodperces késleltetéssel
  - Circuit breaker: 30 másodperces mintavételi ablak
  - Kérésenként 8 másodperc, összesen 30 másodperc timeout

## Lekérdezési ciklus (`SolarWorker`)

```
Indulás -> QPI (protokoll azonosítás)
  |
  v
Végtelen ciklus:
  [5s várakozás] -> QPIGS 1 + QPIGS2 1 (státusz lekérdezés)
  [5s várakozás] -> QET, QEY, QEM, QED (energia lekérdezés)
  [5s várakozás] -> ismétlés
```

### Lekérdezett értékek

**Státusz (QPIGS 1)**:
- Grid: feszültség, teljesítmény, frekvencia
- Kimenet: feszültség, teljesítmény, frekvencia, terhelés %
- Akkumulátor: feszültség, kapacitás %
- PV1: áram, feszültség, teljesítmény
- Rendszer hőmérséklet

**Státusz (QPIGS2 1)**:
- PV2: áram, feszültség, teljesítmény

**Energia (QET/QEY/QEM/QED)**:
- Összes, éves, havi, napi energia termelés

## Logolás

- Formátum: `yyyy-MM-dd HH:mm:ss` időbélyeg, egysoros, forrás osztály nélkül
- `Information` szint: STATUS, ENERGY értékek, ACCESS parancsok
- `Warning` szint: sikertelen lekérdezések, REST hibák
- `Error` szint: TCP kapcsolat/küldés/olvasás hibák
- `Microsoft.*`, `System.*`, `Polly.*` kategóriák `Warning` szintre szűrve

## Konfiguráció (`appsettings.json`)

```json
{
  "Solar": {
    "InverterHost": "192.168.17.22",
    "InverterPort": 4196,
    "OpenHabBaseUrl": "http://admin:JELSZO@192.168.17.220:8888/rest/items",
    "PollIntervalSeconds": 5
  }
}
```

## Systemd integráció

- `Microsoft.Extensions.Hosting.Systemd` csomag + `AddSystemd()` hívás
- `Type=notify`: a .NET host automatikusan küld `sd_notify` jelzéseket (ready, stopping)
- `Restart=always`, `RestartSec=10`: hiba esetén 10 másodperc után újraindul
- Telepítési könyvtár: `/opt/bas`

### Telepítés

```bash
dotnet publish -c Release -r linux-x64 --self-contained -o /opt/bas
cp solar.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable solar
systemctl start solar
```

### Szolgáltatás kezelése

```bash
systemctl status solar
systemctl stop solar
journalctl -u solar -f
```

## Build és futtatás (fejlesztés)

```bash
dotnet build
dotnet run
```

## NuGet csomagok

- `Microsoft.Extensions.Hosting` 8.0.1
- `Microsoft.Extensions.Hosting.Systemd` 8.0.1
- `Microsoft.Extensions.Http` 8.0.1
- `Microsoft.Extensions.Http.Resilience` 9.0.0
