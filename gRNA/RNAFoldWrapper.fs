module gRNA.RNAFoldWrapper

open System
open System.Diagnostics
open System.Threading.Tasks

type RNAFoldResult = {
    Structure: string
    Energy: float
}

let private parseOutput (output: string) : RNAFoldResult =
    try
        // Expected output format: ['(((...))).........', -2.0999999046325684]
        let trimmed = output.Trim()
        
        // Remove square brackets
        let cleaned = 
            if trimmed.StartsWith("[") && trimmed.EndsWith("]") then
                trimmed.Substring(1, trimmed.Length - 2)
            else
                trimmed
        
        // Split by comma, but we need to be careful as the structure might contain commas
        let commaIndex = cleaned.LastIndexOf(',')
        if commaIndex = -1 then
            raise (Exception(sprintf "No comma found in output: %s" output))
        
        let structurePart = cleaned.Substring(0, commaIndex).Trim()
        let energyPart = cleaned.Substring(commaIndex + 1).Trim()
        
        // Remove quotes from structure
        let structure = 
            if structurePart.StartsWith("'") && structurePart.EndsWith("'") then
                structurePart.Substring(1, structurePart.Length - 2)
            elif structurePart.StartsWith("\"") && structurePart.EndsWith("\"") then
                structurePart.Substring(1, structurePart.Length - 2)
            else
                structurePart
        
        let energy = float energyPart
        
        { Structure = structure; Energy = energy }
    with
    | ex ->
        raise (Exception(sprintf "Failed to parse ViennaRNA output '%s': %s" output ex.Message, ex))

/// Folds an RNA sequence using ViennaRNA Python library via subprocess
/// Returns the secondary structure in dot-bracket notation and the minimum free energy
let fold (sequence: string) : Task<RNAFoldResult> = task {
    let startInfo = ProcessStartInfo()
    
    startInfo.FileName <- "python3"
    startInfo.Arguments <- sprintf "-c \"import RNA; print(RNA.fold('%s'))\"" sequence
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true
    
    try
        use proc = new Process()
        proc.StartInfo <- startInfo
        
        let started = proc.Start()
        if not started then
            // Try with 'python' command instead
            let startInfo2 = ProcessStartInfo()
            startInfo2.FileName <- "python"
            startInfo2.Arguments <- sprintf "-c \"import RNA; print(RNA.fold('%s'))\"" sequence
            startInfo2.RedirectStandardOutput <- true
            startInfo2.RedirectStandardError <- true
            startInfo2.UseShellExecute <- false
            startInfo2.CreateNoWindow <- true
            
            use proc2 = new Process()
            proc2.StartInfo <- startInfo2
            proc2.Start() |> ignore
            
            let! stdout = proc2.StandardOutput.ReadToEndAsync()
            let! stderr = proc2.StandardError.ReadToEndAsync()
            do! proc2.WaitForExitAsync()
            
            if proc2.ExitCode <> 0 then
                raise (Exception(sprintf "Python process failed with exit code %d: %s" proc2.ExitCode stderr))
            
            return parseOutput stdout
        else
            let! stdout = proc.StandardOutput.ReadToEndAsync()
            let! stderr = proc.StandardError.ReadToEndAsync()
            do! proc.WaitForExitAsync()
            
            if proc.ExitCode <> 0 then
                raise (Exception(sprintf "Python process failed with exit code %d: %s" proc.ExitCode stderr))
            
            return parseOutput stdout
    with
    | ex -> 
        return! Task.FromException<RNAFoldResult>(Exception(sprintf "Failed to execute ViennaRNA fold: %s" ex.Message, ex))
}
let private parseOutputBatch (output: string) : RNAFoldResult list =
    try
        // Expected output: one result per line in format ['structure', energy]
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map parseOutput
        |> List.ofArray
    with
    | ex ->
        raise (Exception(sprintf "Failed to parse batch ViennaRNA output: %s" ex.Message, ex))

/// Folds multiple RNA sequences efficiently in a single Python call
let foldMany (sequences: string list) : Task<RNAFoldResult list> = task {
    if sequences.IsEmpty then
        return []
    else
        // Build Python script that processes all sequences
        let pythonScript = 
            let seqArray = 
                sequences 
                |> List.map (fun s -> sprintf "'%s'" s)
                |> String.concat ", "
            
            sprintf "import RNA; sequences = [%s]; [print(RNA.fold(seq)) for seq in sequences]" seqArray
        
        let startInfo = ProcessStartInfo()
        startInfo.FileName <- "python3"
        startInfo.Arguments <- sprintf "-c \"%s\"" pythonScript
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        startInfo.CreateNoWindow <- true
        
        try
            use proc = new Process()
            proc.StartInfo <- startInfo
            
            let started = proc.Start()
            if not started then
                // Try with 'python' command instead
                let startInfo2 = ProcessStartInfo()
                startInfo2.FileName <- "python"
                startInfo2.Arguments <- sprintf "-c \"%s\"" pythonScript
                startInfo2.RedirectStandardOutput <- true
                startInfo2.RedirectStandardError <- true
                startInfo2.UseShellExecute <- false
                startInfo2.CreateNoWindow <- true
                
                use proc2 = new Process()
                proc2.StartInfo <- startInfo2
                proc2.Start() |> ignore
                
                let! stdout = proc2.StandardOutput.ReadToEndAsync()
                let! stderr = proc2.StandardError.ReadToEndAsync()
                do! proc2.WaitForExitAsync()
                
                if proc2.ExitCode <> 0 then
                    raise (Exception(sprintf "Python batch process failed with exit code %d: %s" proc2.ExitCode stderr))
                
                return parseOutputBatch stdout
            else
                let! stdout = proc.StandardOutput.ReadToEndAsync()
                let! stderr = proc.StandardError.ReadToEndAsync()
                do! proc.WaitForExitAsync()
                
                if proc.ExitCode <> 0 then
                    raise (Exception(sprintf "Python batch process failed with exit code %d: %s" proc.ExitCode stderr))
                
                return parseOutputBatch stdout
        with
        | ex -> 
            return! Task.FromException<RNAFoldResult list>(Exception(sprintf "Failed to execute batch ViennaRNA fold: %s" ex.Message, ex))
}