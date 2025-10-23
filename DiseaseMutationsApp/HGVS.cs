using System.Text.RegularExpressions;

namespace DiseaseMutationsApp;

public partial class HGVS
{
    private string Code { get; set; }
    public string Accession { get; private set; }
    public string Type { get; private set; }
    public (int start, int end) Position { get; private set; }
    
    public enum MutationType
    {
        Substitution,
        Deletion,
        Insertion,
        Duplication,
        Inversion,
        DeletionInsertion,
        Repeat,
        NoChange,
        Conversion,
        Translocation,
        LargeUnknown
    }
    public MutationType 
        Mutation { get; private set; }
    
    public string Reference { get; private set; }
    public string Alternate { get; private set; }
    public int Count { get; private set; }
    
    
    public HGVS(string code)
    {
        Code = code;
        Parse();
    }

    private void Parse()
    {
        var parts = Code.Split(':');
        Accession = parts[0];
        
        
        var typeAndRest = parts[1];
        
        Type = typeAndRest[0].ToString();
        var positionAndMethod = typeAndRest[2..];
        
        //use regex to extract number or 2 numbers with underscore
        var positionMatch = PositionRange().Match(positionAndMethod);
        if (!positionMatch.Success) throw new Exception("Invalid HGVS format: Position not found");
        var positions = positionMatch.Value.Split("_");
        Position = 
            positions.Length == 2 ? 
                (int.Parse(positions[0]), int.Parse(positions[1])) :
                (int.Parse(positions[0]), int.Parse(positions[0]));
        
        var method = positionAndMethod[positionMatch.Length..];
        ParseMethod(method);
    }

    private void ParseMethod(string method)
    {
        if(string.IsNullOrEmpty(method) || method == "=")
        {
            Mutation = MutationType.NoChange;
        }
        else if (method.Contains('>'))
        {
            Mutation = MutationType.Substitution;
            var refs = method.Split('>');
            Reference = refs[0];
            Alternate = refs[1];
        }
        else if (method.StartsWith("delins"))
        {
            Mutation = MutationType.DeletionInsertion;
            Alternate = method[6..];
        }
        else if (method.StartsWith("del"))
        {
            Mutation = MutationType.Deletion;
        }
        else if (method.StartsWith("ins"))
        {
            Mutation = MutationType.Insertion;
            Alternate = method[3..];
        }
        else if (method.StartsWith("dup"))
        {
            Mutation = MutationType.Duplication;
        }
        else if (method.StartsWith("inv"))
        {
            Mutation = MutationType.Inversion;
        }
        else if (method.Contains('[') && method.Contains(']'))
        {
            Mutation = MutationType.Repeat;
            var sequence = method[..method.IndexOf('[')];
            var repeatCountStr = method[(method.IndexOf('[') + 1)..method.IndexOf(']')];
            var repeatCount = int.Parse(repeatCountStr);
            Alternate = sequence;
            Count = repeatCount;
        }
        else
        {
            throw new Exception("Invalid HGVS format: Unknown mutation method");
        }
    }

    [GeneratedRegex(@"^(\d+)(_(\d+))?")]
    private static partial Regex PositionRange();
}