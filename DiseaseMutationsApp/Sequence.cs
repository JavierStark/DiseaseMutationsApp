namespace DiseaseMutationsApp;

public class Sequence
{
    private const string NCBI_EFETCH_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi";
    public string Id { get; private set; }
    public string Data { get; private set; }
    //baseUrl = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi?db=nuccore&id=NG_016465.4&seq_start=70169&seq_stop=70190&rettype=fasta"


    public Sequence(string id, string data)
    {
        Id = id;
        Data = data;
    }

    public string From(int from)
    {
        return Data[(from - 1)..];
    }
    
    public string To(int to)
    {
        return Data[..to];
    }
    
    public string FromTo(int from, int to)
    {
        return Data[(from - 1)..to];
    }
}