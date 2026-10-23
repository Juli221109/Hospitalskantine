# Hospitalskantinen

En lille C# ASP.NET Core-hjemmeside til patienternes måltidsvalg.

## Gratis offentlig undervisningsdemo

Projektet er klargjort til Render Free med Docker og `render.yaml`. Blueprintfilen opretter kun én gratis webservice: ingen database, disk, betalt abonnement eller prøveperiode.

Render Free har begrænsninger:

- Webservicen går i dvale efter 15 minutters inaktivitet; første besøg bagefter kan tage omkring et minut.
- Filsystemet er midlertidigt. Testkonti og bestillinger kan forsvinde ved dvale, genstart eller deploy.
- Render tildeler 750 gratis instanstimer pr. workspace pr. kalendermåned. Når timer eller inkluderet forbrug er opbrugt, suspenderes tjenesten eller nye builds, hvis der ikke er tilføjet betalingsmetode.
- Dette er kun til undervisning og test, ikke til rigtige patienter eller personfølsomme oplysninger.

Når `KANTINE_DEMO_MODE=true`, starter C#-appen automatisk disse offentlige testkonti igen, hvis de mangler:

| Rolle | Brugernavn | Adgangskode |
|---|---|---|
| Patient | `demo` | `1234` |
| Kantine | `kantine` | `KantineDemo2026!` |

Brug ikke disse loginoplysninger til andre systemer. Alle besøgende på demoen kan foretage testbestillinger, og de kan blive slettet, når tjenesten genstarter.

I Render vælges **New → Blueprint**, og GitHub-repository `Juli221109/Hospitalskantine`. Vælg kun Free-ressourcer, og stop, hvis Render kræver betalingskort eller viser en betalt ressource. Render genererer et offentligt `onrender.com`-link og kan deploye automatisk fra `main`.

## Start hjemmesiden

Installer .NET 10 SDK, åbn en terminal i projektmappen `Hospitalskantine`, og kør:

```powershell
dotnet run --environment Development -- --urls http://127.0.0.1:3000
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

## Kør tests

Projektet har unit tests for, at patientens måltider koster 0 kr., og at pårørende betaler den korrekte pris pr. måltid:

```powershell
dotnet test .\tests\Hospitalskantine.Tests\Hospitalskantine.Tests.csproj
```

`/health` er et offentligt helbredstjek til hostingplatformens statuskontrol. I produktion sendes cookies kun over HTTPS. `KANTINE_DATA_DIR` er konfigureret til midlertidig fillagring på Render Free.
