namespace DiseaseMutationsApp;

public class Sequence
{
    public string Id { get; private set; }
    public string Data { get; private set; }


    public Sequence(string id, string data)
    {
        Id = id;
        Data = data;
    }

    public (string mutated, string original) GetMutatedSubsequence(HGVS hgvs, int leftPadding = 0, int rightPadding = 0)
    {
        var start = hgvs.Position.start - 1;
        var limitLeft = start - leftPadding;
        
        if (start < 0) start = 0;
        if (limitLeft < 0) leftPadding = 0;
        
        var end = hgvs.Position.end;
        var limitRight = end + rightPadding;
        
        if (end > Data.Length) end = Data.Length;
        if (limitRight > Data.Length) rightPadding = Data.Length;
        
        var original = Data[limitLeft..limitRight];
        
        var mutated = hgvs.Mutation switch
        {
            HGVS.MutationType.Substitution => 
                Data[limitLeft..start] + hgvs.Alternate + Data[end..limitRight],
            HGVS.MutationType.Deletion => 
                Data[limitLeft..start] + Data[end..limitRight],
            HGVS.MutationType.Insertion => 
                Data[limitLeft..(start + 1)] + hgvs.Alternate + Data[(start + 1)..limitRight],
            HGVS.MutationType.Duplication => 
                Data[limitLeft..start] + Data[start..end] + Data[start..end] + Data[end..limitRight],
            HGVS.MutationType.NoChange => 
                original,
            HGVS.MutationType.DeletionInsertion => 
                Data[limitLeft..start] + hgvs.Alternate + Data[end..limitRight],
            HGVS.MutationType.Inversion => 
                Data[limitLeft..start] + 
                new string(Data[start..end].Reverse().ToArray()) + 
                Data[end..limitRight],
            _ => throw new NotImplementedException($"Mutation type {hgvs.Mutation} not implemented")
        };
        
        return (mutated, original); 
    }
}