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

    public (string mutated, string original) GetMutatedSubsequence(HGVS hgvs, int leftBorder = 0, int rightBorder = 0)
    {
        var start = hgvs.Position.start - 1;
        var limitLeft = start - leftBorder;
        
        if (start < 0) start = 0;
        if (limitLeft < 0) leftBorder = 0;
        
        var end = hgvs.Position.end;
        var limitRight = end + rightBorder;
        
        if (end > Data.Length) end = Data.Length;
        if (limitRight > Data.Length) rightBorder = Data.Length;
        
        var original = Data[limitLeft..limitRight];
        
        var mutated = hgvs.Mutation switch
        {
            HGVS.MutationType.Substitution => 
                Data[(start-leftBorder)..start] + hgvs.Alternate + Data[end..(end + rightBorder)],
            HGVS.MutationType.Deletion => 
                Data[..start] + Data[end..],
            HGVS.MutationType.Insertion => 
                Data[..(start + 1)] + hgvs.Alternate + Data[(start + 1)..],
            HGVS.MutationType.Duplication => 
                Data[..end] + Data[start..(end - start)] + Data[end..],
            HGVS.MutationType.NoChange => 
                original,
            _ => throw new NotImplementedException($"Mutation type {hgvs.Mutation} not implemented")
        };
        
        return (mutated, original); 
    }
}