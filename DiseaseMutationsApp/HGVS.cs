namespace DiseaseMutationsApp;

public class HGVS
{
    private string Code { get; set; }
    public string Accession { get; private set; }
    public string Type { get; private set; }
    public int Position { get; private set; }
    public string Reference { get; private set; }
    public string Alternate { get; private set; }
    
    public HGVS(string code)
    {
        Code = code;
        Parse();
    }

    private void Parse()
    {
        // Example: NM_000546.6:c.215C>G or NG_016465.4:g.70185=
        var parts = Code.Split(':');
        Accession = parts[0]; // 'NM_000546.6'
        
        
        var typeAndRest = parts[1]; // 'c.215C>G'
        
        Type = typeAndRest[0].ToString(); // 'c' or 'g'
        var rest = typeAndRest[2..]; // '215C>G' or '70185='
        
        if (rest.Contains('>'))
        {
            var positionPart = new string(rest.TakeWhile(c => char.IsDigit(c)).ToArray());
            Position = int.Parse(positionPart);
            
            var refAltPart = rest[positionPart.Length..]; // 'C>G'
            var refAltSplit = refAltPart.Split('>');
            Reference = refAltSplit[0]; // 'C'
            Alternate = refAltSplit[1]; // 'G'
        }
        else if (rest.EndsWith('='))
        {
            var positionPart = new string(rest.TakeWhile(c => char.IsDigit(c)).ToArray());
            Position = int.Parse(positionPart);
            Reference = "";
            Alternate = "="; // No change
        }
        else
        {
            throw new FormatException("Unsupported HGVS format");
        }
        
    }
}