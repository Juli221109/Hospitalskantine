# Hospitalskantinen

En lille C# ASP.NET Core-hjemmeside til patienternes måltidsvalg.

## Start hjemmesiden

Installer .NET 10 SDK, åbn en terminal i projektmappen `Hospitalskantine`, og kør:

```powershell
dotnet run -- --urls http://127.0.0.1:3000
```

Åbn derefter <http://127.0.0.1:3000>.

## Opret login

Der er ingen selvregistrering. Opret en patient eller en kantinebruger fra projektmappen:

```powershell
dotnet run -- --create-user patient01 patient
dotnet run -- --create-user kantine01 canteen
```

Adgangskoden indtastes skjult i terminalen. Patientens adgangskode skal være fire tal; kantinebrugerens adgangskode skal være mindst 12 tegn. En patient får automatisk et ID som `P001`. Adgangskoder gemmes som PBKDF2-hashes.

## Brug

- Patienten kan vælge morgenmad, frokost og aftensmad for den kommende uge (mandag-søndag).
- Patientens mad er gratis. En pårørende kan vælges til hvert måltid og betaler 25 kr for morgenmad, 50 kr for frokost og 60 kr for aftensmad.
- Kantinebrugeren ser antal patienter og pårørende samt beløb pr. måltid. Patienternes måltider indgår ikke i beløbet.
- Konti og valg gemmes lokalt som JSON-filer under `%LOCALAPPDATA%\Hospitalskantine`. `KANTINE_DATA_DIR` kan sættes for at vælge en anden datamappe.

Dette er en lokal udviklingsversion. Før offentlig drift skal den hostes bag HTTPS, og datalagring, sikkerhedskopiering og adgangskoder skal administreres på en egnet server.
