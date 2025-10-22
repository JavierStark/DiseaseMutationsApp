namespace DiseaseMutationsApp;

public class SequenceRepository
{
    private const string BASE_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi?db=nuccore&id=";

    private static readonly Dictionary<string, Sequence> sequences = new();
    
    public static async Task<Sequence> GetSequence(string id)
    {
        if (sequences.TryGetValue(id, out var sequence))
        {
            return sequence;
        }

        sequence = new Sequence(id, await GetSequenceData(id));
        sequences[id] = sequence;
        return sequence;
    }

    private static async Task<string> GetSequenceData(string id)
    {
        var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{BASE_URL}{id}&rettype=fasta");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var lines = content.Split('\n');
        var data = string.Join("", lines.Skip(1)).Trim();
        return data;
    }
}