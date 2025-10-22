namespace DiseaseMutationsApp;

public class Sequence
{
    private const string NCBI_EFETCH_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi";
    public string Id { get; private set; }
    private string url;
    public int FromIndex { get; private set; } = -1;

    public int ToIndex { get; private set; } = -1;
    //baseUrl = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi?db=nuccore&id=NG_016465.4&seq_start=70169&seq_stop=70190&rettype=fasta"


    private Sequence(string id)
    {
        Id = id;
        url = NCBI_EFETCH_URL + "?db=nuccore&id="+id;
    }
    
    private Sequence(string id, int from, int to)
    {
        Id = id;
        FromIndex = from;
        ToIndex = to;
        url = NCBI_EFETCH_URL + "?db=nuccore&id=" + id; 
        
        if (from > 0)
        {
            url += "&seq_start=" + from;
        }
        if (to > 0)
        {
            url += "&seq_stop=" + to;
        }
        
        url += "&rettype=fasta";
    }
    
    public static Sequence UsingId(string id)
    {
        return new Sequence(id);
    }

    public Sequence From(int from)
    {
        return new Sequence(Id, from, ToIndex);
    }
    
    public Sequence To(int to)
    {
        return new Sequence(Id, FromIndex, to);
    }
    
    public Sequence FromTo(int from, int to)
    {
        return new Sequence(Id, from, to);
    }

    public async Task<string> Get()
    {
        Console.WriteLine(url);
        var httpClient = new HttpClient();

        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var lines = content.Split('\n');
        
        return string.Join("", lines.Skip(1)).Trim();
    }
    
}