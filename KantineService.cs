using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hospitalskantine;

public sealed record Brugerkonto(string Brugernavn, string Rolle, string? PatientId, string AdgangskodeHash);
public sealed record LoginBruger(string Brugernavn, string Rolle, string? PatientId);
public sealed record Maaltidspriser(int Morgenmad, int Frokost, int Aftensmad);
public sealed record Maaltidstaelling(int Patienter, int Paarorende);
public sealed record Maaltidsoptaelling(Maaltidstaelling Morgenmad, Maaltidstaelling Frokost, Maaltidstaelling Aftensmad);
public sealed record Maaltidsbelob(int Morgenmad, int Frokost, int Aftensmad);
public sealed record Menudag(string Morgenmad, string Frokost, string Aftensmad);
public sealed record Patientdag(
    string Dato,
    int? MorgenmadValg,
    int? FrokostValg,
    int? AftensmadValg,
    Menudag Menu,
    Maaltidspriser Priser,
    Maaltidsbelob Belob);
public sealed record Kantinedag(
    string Dato,
    Maaltidsoptaelling Maaltider,
    Menudag Menu,
    Maaltidspriser Priser,
    Maaltidsbelob Belob);
public sealed record Valgdag(string Dato, int MorgenmadValg, int FrokostValg, int AftensmadValg);

public sealed class KantineService(FilService filer)
{
    public static readonly Maaltidspriser Priser = new(25, 50, 60);
    public const string DemoPatientBrugernavn = "demo";
    public const string DemoPatientAdgangskode = "1234";
    public const string DemoKantineBrugernavn = "kantine";
    public const string DemoKantineAdgangskode = "KantineDemo2026!";
    private readonly object _writeGate = new();

    private static readonly Menudag[] Menu =
    [
        new("Havregrød med frugt", "Kylling med ris og grøntsager", "Fiskefrikadeller med kartofler"),
        new("Rugbrød, ost og æg", "Frikadeller med kartofler", "Pasta med kødsovs"),
        new("Yoghurt med müsli og bær", "Lasagne med salat", "Kylling med ris"),
        new("Havregrød med æble og kanel", "Svinemørbrad med kartofler", "Grøntsagssuppe med brød"),
        new("Brød med ost og grønt", "Chili con carne med ris", "Ovnbagt fisk med kartofler"),
        new("Yoghurt med havregryn", "Burger med ovnkartofler", "Kyllingefrikadeller med salat"),
        new("Rundstykker med ost og æg", "Langtidsstegt svinekød med kartofler", "Grøntsagslasagne med salat")
    ];

    public LoginBruger? LogInd(string brugernavn, string adgangskode)
    {
        var konto = filer.IndlaesBrugere().FirstOrDefault(
            user => string.Equals(user.Brugernavn, brugernavn, StringComparison.OrdinalIgnoreCase));
        if (konto is null || !KontrollerAdgangskode(adgangskode, konto.AdgangskodeHash))
        {
            return null;
        }

        return new LoginBruger(konto.Brugernavn, konto.Rolle, konto.PatientId);
    }

    public string OpretBruger(string brugernavn, string adgangskode, string rolle)
    {
        brugernavn = brugernavn.Trim();
        if (!Regex.IsMatch(brugernavn, "^[a-zA-Z0-9._-]{2,30}$"))
        {
            throw new ArgumentException("Brugernavnet skal være 2-30 tegn og må kun indeholde bogstaver, tal, punktum, bindestreg og underscore.");
        }

        if (rolle is not ("patient" or "canteen"))
        {
            throw new ArgumentException("Rollen skal være patient eller canteen.");
        }

        if (rolle == "patient" && !Regex.IsMatch(adgangskode, "^[0-9]{4}$"))
        {
            throw new ArgumentException("Patientens adgangskode skal være præcis fire tal.");
        }

        if (rolle == "canteen" && adgangskode.Length < 12)
        {
            throw new ArgumentException("Kantinebrugerens adgangskode skal være mindst 12 tegn.");
        }

        lock (_writeGate)
        {
            var brugere = filer.IndlaesBrugere();
            if (brugere.Any(user => string.Equals(user.Brugernavn, brugernavn, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Brugernavnet findes allerede.");
            }

            var patientId = rolle == "patient" ? NaestePatientId(brugere) : null;
            brugere.Add(new Brugerkonto(brugernavn, rolle, patientId, LavAdgangskodeHash(adgangskode)));
            filer.GemBrugere(brugere);
            return patientId ?? brugernavn;
        }
    }

    public void SikreDemoKonti()
    {
        lock (_writeGate)
        {
            SikreDemoKonto(DemoPatientBrugernavn, DemoPatientAdgangskode, "patient");
            SikreDemoKonto(DemoKantineBrugernavn, DemoKantineAdgangskode, "canteen");
        }
    }

    private void SikreDemoKonto(string brugernavn, string adgangskode, string rolle)
    {
        var eksisterende = filer.IndlaesBrugere().FirstOrDefault(user =>
            string.Equals(user.Brugernavn, brugernavn, StringComparison.OrdinalIgnoreCase));
        if (eksisterende is not null)
        {
            if (eksisterende.Rolle != rolle)
            {
                throw new InvalidOperationException(
                    $"Demokontoen '{brugernavn}' har den forkerte rolle i brugerdataene.");
            }

            return;
        }

        OpretBruger(brugernavn, adgangskode, rolle);
    }

    public IReadOnlyList<Patientdag> HentValg(string patientId, DateOnly dato)
    {
        var eksisterende = filer.IndlaesBestillinger()
            .Where(choice => choice.PatientId == patientId)
            .ToDictionary(choice => choice.Date, StringComparer.Ordinal);
        return KommendeUge(dato).Select(day =>
        {
            eksisterende.TryGetValue(day, out var choice);
            var meal = MenuForDato(day);
            var breakfast = choice?.BreakfastChoice;
            var lunch = choice?.LunchChoice;
            var dinner = choice?.DinnerChoice;
            return new Patientdag(
                day,
                breakfast,
                lunch,
                dinner,
                meal,
                Priser,
                new Maaltidsbelob(Belob(breakfast, Priser.Morgenmad), Belob(lunch, Priser.Frokost),
                    Belob(dinner, Priser.Aftensmad)));
        }).ToArray();
    }

    public void GemValg(string patientId, IReadOnlyList<Valgdag> valg, DateOnly dato)
    {
        var dage = KommendeUge(dato);
        if (valg.Count != dage.Count || valg.Select(day => day.Dato).Distinct(StringComparer.Ordinal).Count() != dage.Count ||
            valg.Any(day => !dage.Contains(day.Dato, StringComparer.Ordinal) ||
                            !GyldigtValg(day.MorgenmadValg) ||
                            !GyldigtValg(day.FrokostValg) ||
                            !GyldigtValg(day.AftensmadValg)))
        {
            throw new ArgumentException("Vælg en mulighed for hvert måltid på alle syv dage.");
        }

        lock (_writeGate)
        {
            var bestillinger = filer.IndlaesBestillinger();
            bestillinger.RemoveAll(choice => choice.PatientId == patientId && dage.Contains(choice.Date, StringComparer.Ordinal));
            bestillinger.AddRange(valg.Select(day =>
                new Bestilling(patientId, day.Dato, day.MorgenmadValg, day.FrokostValg, day.AftensmadValg)));
            filer.GemBestillinger(bestillinger);
        }
    }

    public IReadOnlyList<Kantinedag> HentOptaellinger(DateOnly dato)
    {
        var bestillinger = filer.IndlaesBestillinger();
        return KommendeUge(dato).Select(day =>
        {
            var dagens = bestillinger.Where(choice => choice.Date == day).ToArray();
            var breakfast = Tael(dagens.Select(choice => choice.BreakfastChoice));
            var lunch = Tael(dagens.Select(choice => choice.LunchChoice));
            var dinner = Tael(dagens.Select(choice => choice.DinnerChoice));
            return new Kantinedag(
                day,
                new Maaltidsoptaelling(breakfast, lunch, dinner),
                MenuForDato(day),
                Priser,
                new Maaltidsbelob(breakfast.Paarorende * Priser.Morgenmad,
                    lunch.Paarorende * Priser.Frokost, dinner.Paarorende * Priser.Aftensmad));
        }).ToArray();
    }

    public static bool GyldigtValg(int valg) => valg is >= 0 and <= 2;

    private static Maaltidstaelling Tael(IEnumerable<int?> valg)
    {
        var choices = valg.ToArray();
        return new Maaltidstaelling(choices.Count(value => value is 1 or 2), choices.Count(value => value == 2));
    }

    private static int Belob(int? valg, int pris) => valg == 2 ? pris : 0;

    private static Menudag MenuForDato(string dato)
    {
        var parsed = DateOnly.ParseExact(dato, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Menu[((int)parsed.DayOfWeek + 6) % 7];
    }

    private static IReadOnlyList<string> KommendeUge(DateOnly dato)
    {
        var mondayOffset = ((int)dato.DayOfWeek + 6) % 7;
        var thisMonday = dato.AddDays(-mondayOffset);
        var nextMonday = dato == thisMonday ? thisMonday : thisMonday.AddDays(7);
        return Enumerable.Range(0, 7)
            .Select(offset => nextMonday.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static string NaestePatientId(IReadOnlyList<Brugerkonto> brugere)
    {
        var nextNumber = brugere
            .Where(user => user.PatientId is not null && user.PatientId.StartsWith('P'))
            .Select(user => int.TryParse(user.PatientId.AsSpan(1), out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"P{nextNumber:D3}";
    }

    private static string LavAdgangskodeHash(string adgangskode)
    {
        const int iterations = 150_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(adgangskode, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool KontrollerAdgangskode(string adgangskode, string hash)
    {
        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" ||
            !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(adgangskode, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
