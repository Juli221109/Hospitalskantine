using System.Globalization;
using Hospitalskantine;

namespace Hospitalskantine.Tests;

public sealed class MaaltidsprisTests
{
    private static readonly DateOnly Onsdag = new(2026, 10, 21);

    [Fact]
    public void Patientens_maaltider_er_gratis()
    {
        using var storage = new TemporaryDirectory();
        var service = new KantineService(new FilService(storage.Path));

        service.GemValg("P001", OpretUgevalg(1, 1, 1), Onsdag);

        var mandag = service.HentOptaellinger(Onsdag)[0];
        Assert.Equal(0, mandag.Belob.Morgenmad);
        Assert.Equal(0, mandag.Belob.Frokost);
        Assert.Equal(0, mandag.Belob.Aftensmad);
    }

    [Fact]
    public void Paarorende_betaler_den_korrekte_pris_for_hvert_maaltid()
    {
        using var storage = new TemporaryDirectory();
        var service = new KantineService(new FilService(storage.Path));

        service.GemValg("P001", OpretUgevalg(2, 2, 2), Onsdag);

        var mandag = service.HentOptaellinger(Onsdag)[0];
        Assert.Equal(25, mandag.Belob.Morgenmad);
        Assert.Equal(50, mandag.Belob.Frokost);
        Assert.Equal(60, mandag.Belob.Aftensmad);
        Assert.Equal(1, mandag.Maaltider.Morgenmad.Paarorende);
        Assert.Equal(1, mandag.Maaltider.Frokost.Paarorende);
        Assert.Equal(1, mandag.Maaltider.Aftensmad.Paarorende);
    }

    private static IReadOnlyList<Valgdag> OpretUgevalg(int morgenmad, int frokost, int aftensmad)
    {
        var mandag = new DateOnly(2026, 10, 26);
        return Enumerable.Range(0, 7)
            .Select(offset => new Valgdag(
                mandag.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                morgenmad,
                frokost,
                aftensmad))
            .ToArray();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory().FullName;

        public string Path => _path;

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
