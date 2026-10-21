using System.Text.Json;

namespace Hospitalskantine;

public sealed class FilService(string dataDirectory)
{
    private readonly object _gate = new();
    private readonly string _usersPath = Path.Combine(dataDirectory, "users.json");
    private readonly string _choicesPath = Path.Combine(dataDirectory, "bestillinger.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<Brugerkonto> IndlaesBrugere() => ReadList<Brugerkonto>(_usersPath);

    public List<Bestilling> IndlaesBestillinger() => ReadList<Bestilling>(_choicesPath);

    public void GemBrugere(List<Brugerkonto> brugere) => WriteList(_usersPath, brugere);

    public void GemBestillinger(List<Bestilling> bestillinger) => WriteList(_choicesPath, bestillinger);

    private List<T> ReadList<T>(string path)
    {
        lock (_gate)
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<T>>(json)
                ?? throw new InvalidDataException($"Datafilen {Path.GetFileName(path)} indeholder ikke en liste.");
        }
    }

    private void WriteList<T>(string path, List<T> value)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
