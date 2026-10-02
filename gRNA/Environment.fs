namespace gRNA

open System
open System.IO

/// Bad user input (malformed HGVS, unsupported mutation, ...).
type GrnaInputException(message: string, ?inner: exn) =
    inherit Exception(message, defaultArg inner null)

/// A native dependency (Bowtie binary/index, Python/ViennaRNA) is missing or unusable.
type GrnaDependencyException(message: string, ?inner: exn) =
    inherit Exception(message, defaultArg inner null)

/// An upstream service (NCBI) or child process failed or returned something unparseable.
type GrnaUpstreamException(message: string, ?inner: exn) =
    inherit Exception(message, defaultArg inner null)

/// Where the native tools live and how the library talks to the outside world.
/// Relative paths are resolved against AppContext.BaseDirectory first, then the current directory.
type GrnaEnvironment =
    { BowtieBinary: string
      /// Explicit index base path (without the .1.bt2 / .1.ebwt suffix). Wins over globbing.
      BowtieIndexBase: string option
      BowtieIndexDirectory: string
      PythonExecutable: string
      ProcessTimeout: TimeSpan
      NcbiApiKey: string option
      NcbiContact: string option
      /// Maximum number of distinct accessions kept in the in-memory sequence cache.
      SequenceCacheEntries: int }

module GrnaEnvironment =
    let defaults =
        { BowtieBinary = Path.Combine("bowtie", "bowtie-align-s")
          BowtieIndexBase = None
          BowtieIndexDirectory = Path.Combine("bowtie", "indexes")
          PythonExecutable = "python3"
          ProcessTimeout = TimeSpan.FromMinutes 10.0
          NcbiApiKey = None
          NcbiContact = None
          SequenceCacheEntries = 8 }

    /// Applies GRNA_* environment variable overrides on top of `defaults`.
    let fromEnvironmentVariables () =
        let get (name: string) =
            match Environment.GetEnvironmentVariable name with
            | null | "" -> None
            | v -> Some v

        { defaults with
            BowtieBinary = get "GRNA_BOWTIE_BINARY" |> Option.defaultValue defaults.BowtieBinary
            BowtieIndexBase = get "GRNA_BOWTIE_INDEX"
            PythonExecutable = get "GRNA_PYTHON" |> Option.defaultValue defaults.PythonExecutable
            NcbiApiKey = get "GRNA_NCBI_API_KEY"
            NcbiContact = get "GRNA_NCBI_CONTACT" }

    /// Resolves a possibly relative path: absolute paths are returned as-is, otherwise
    /// AppContext.BaseDirectory wins when the file/directory exists there, then the CWD.
    let resolvePath (path: string) =
        if Path.IsPathRooted path then
            path
        else
            let fromBase = Path.Combine(AppContext.BaseDirectory, path)
            if File.Exists fromBase || Directory.Exists fromBase then fromBase
            else Path.GetFullPath path

    let mutable private current = fromEnvironmentVariables ()

    /// The process-wide environment used by the wrappers unless one is passed explicitly.
    let getCurrent () = current
    let setCurrent (env: GrnaEnvironment) = current <- env

/// Injectable log sink. Silent by default; hosts redirect it to their logger.
module GrnaLog =
    let mutable sink: string -> unit = ignore
    let log (message: string) = sink message
