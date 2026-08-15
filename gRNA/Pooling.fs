/// Combinatorial pooling of guide RNAs for High-Throughput Screening.
///
/// Each guide targets one pathogenic variant, so screening a disease with hundreds of
/// known variants one-guide-per-well is infeasible. Instead guides are produced in tandem
/// (at most K per transcript, a biological limit) and pooled into wells following a
/// combinatorial layout, so that a positive readout still identifies the causative guide.
///
/// Guides are handled purely as 1-based indices; names and sequences stay in the UI layer.
module gRNA.Pooling

/// The pooling layouts under consideration. The binary model (N = ceil(log2 V)) is
/// deliberately absent: it strictly requires K = V/2, which violates the K <= 5 tandem limit.
type PoolingModel =
    /// Guides are cut into blocks of K*K laid out as a K x K grid; each block contributes
    /// its K rows then its K columns. N = 2K * ceil(V / K^2).
    | TwoDFragmented
    /// A single ceil(sqrt V) square grid; every row and column is split into chunks of K.
    /// N = 2s * ceil(s / K) where s = ceil(sqrt V).
    | TwoDMatrix
    /// A ceil(cbrt V) cube sliced along all three axes; every slice is split into chunks of K.
    /// N = 3c * ceil(c^2 / K) where c = ceil(cbrt V).
    | ThreeD

/// Physical microplate geometry. Rows are labelled A, B, C..., columns are numbered from 1.
type PlateFormat = { Rows: int; Columns: int }

/// Where a pool physically sits once the pools are laid out row-major across plates.
type WellAddress =
    { Plate: int
      Row: char
      Column: int
      Label: string }

/// The cost of one model for a given (V, K), used to pick the cheapest.
type ModelEstimate =
    { Model: PoolingModel
      /// N: total wells the model needs.
      Wells: int
      /// R: how many wells each guide appears in. More repetitions means less risk of a
      /// positive being camouflaged, at the cost of more wells.
      Repetitions: int
      Formula: string }

/// One tube: the guides mixed in it and the well it ends up in. An empty GuideIndices
/// means the layout reserves the slot but there is nothing to prepare.
type Pool =
    { Id: int
      Name: string
      GuideIndices: int list
      Well: WellAddress }

type PoolingPlan =
    { Model: PoolingModel
      GuideCount: int
      WellCapacity: int
      Pools: Pool list
      TotalWells: int
      NonEmptyWells: int
      MaxPoolSize: int }

[<Literal>]
let MaxGuideCount = 100_000

[<Literal>]
let MaxWellCapacity = 10_000

let plate96 = { Rows = 8; Columns = 12 }
let plate384 = { Rows = 16; Columns = 24 }

let allModels = [ TwoDFragmented; TwoDMatrix; ThreeD ]

let modelName model =
    match model with
    | TwoDFragmented -> "2D Fragmented"
    | TwoDMatrix -> "2D Matrix"
    | ThreeD -> "3D"

let formulaText model =
    match model with
    | TwoDFragmented -> "N = 2K * ceil(V / K^2)"
    | TwoDMatrix -> "N = 2s * ceil(s / K), s = ceil(sqrt V)"
    | ThreeD -> "N = 3c * ceil(c^2 / K), c = ceil(cbrt V)"

/// Number of wells each guide appears in under this model.
let repetitions model =
    match model with
    | TwoDFragmented -> 2
    | TwoDMatrix -> 2
    | ThreeD -> 3

/// Tie-break order when two models need the same number of wells: prefer the simpler layout.
let private modelPriority model =
    match model with
    | TwoDFragmented -> 0
    | TwoDMatrix -> 1
    | ThreeD -> 2

/// Integer ceiling division. Both arguments must be positive.
let ceilDiv (a: int) (b: int) =
    if b <= 0 then
        invalidArg "b" "Divisor must be greater than 0."
    elif a <= 0 then
        0
    else
        (a + b - 1) / b

/// Exact ceil(sqrt v). Computed by integer correction rather than trusting the float root:
/// Math.Sqrt/Math.Pow can land a hair either side of a perfect root, and rounding up then
/// overshoots by one, inflating every well count derived from it.
let ceilSqrt (v: int) =
    if v <= 0 then
        0
    else
        let mutable r = max 1 (int (sqrt (float v)))
        while r * r < v do
            r <- r + 1

        while r > 1 && (r - 1) * (r - 1) >= v do
            r <- r - 1

        r

/// Exact ceil(cbrt v). See ceilSqrt for why this is not a plain rounded float root.
let ceilCbrt (v: int) =
    if v <= 0 then
        0
    else
        let mutable r = max 1 (int (System.Math.Cbrt(float v)))
        while r * r * r < v do
            r <- r + 1

        while r > 1 && (r - 1) * (r - 1) * (r - 1) >= v do
            r <- r - 1

        r

/// Rejects inputs that are meaningless or would blow up the pool list.
let validate (guideCount: int) (wellCapacity: int) =
    if guideCount < 1 then
        invalidArg "guideCount" "The number of guides must be at least 1."

    if guideCount > MaxGuideCount then
        invalidArg "guideCount" (sprintf "The number of guides must not exceed %d." MaxGuideCount)

    if wellCapacity < 1 then
        invalidArg "wellCapacity" "The number of guides per well must be at least 1."

    if wellCapacity > MaxWellCapacity then
        invalidArg "wellCapacity" (sprintf "The number of guides per well must not exceed %d." MaxWellCapacity)

/// Total wells the model needs for V guides at K guides per well.
let wellsForModel model (guideCount: int) (wellCapacity: int) =
    validate guideCount wellCapacity
    let v = guideCount
    let k = wellCapacity

    match model with
    | TwoDFragmented -> 2 * k * ceilDiv v (k * k)
    | TwoDMatrix ->
        let s = ceilSqrt v
        2 * s * ceilDiv s k
    | ThreeD ->
        let c = ceilCbrt v
        3 * c * ceilDiv (c * c) k

/// All models costed for this (V, K), cheapest first.
let compareModels (guideCount: int) (wellCapacity: int) =
    validate guideCount wellCapacity

    allModels
    |> List.map (fun m ->
        { Model = m
          Wells = wellsForModel m guideCount wellCapacity
          Repetitions = repetitions m
          Formula = formulaText m })
    |> List.sortBy (fun e -> e.Wells, modelPriority e.Model)

/// The model that needs the fewest wells for this (V, K).
let bestModel (guideCount: int) (wellCapacity: int) =
    (compareModels guideCount wellCapacity |> List.head).Model

/// Where the n-th pool sits once pools are laid out row-major across plates.
let wellAddress (format: PlateFormat) (poolId: int) =
    if format.Rows < 1 || format.Columns < 1 then
        invalidArg "format" "A plate must have at least one row and one column."

    if format.Rows > 26 then
        invalidArg "format" "A plate must not have more than 26 rows (rows are labelled A-Z)."

    if poolId < 1 then
        invalidArg "poolId" "Pool ids are 1-based."

    let perPlate = format.Rows * format.Columns
    let plate = (poolId - 1) / perPlate + 1
    let within = (poolId - 1) % perPlate
    let row = char (int 'A' + within / format.Columns)
    let column = within % format.Columns + 1

    { Plate = plate
      Row = row
      Column = column
      Label = sprintf "Plate %d - %c%d" plate row column }

/// Names a pool, appending a part suffix only when the group actually had to be split.
let private chunkName (baseName: string) (partIndex: int) (partCount: int) =
    if partCount <= 1 then
        baseName
    else
        sprintf "%s (part %d/%d)" baseName partIndex partCount

/// Splits slots 1..groupSize into consecutive chunks of at most k, as (part, first, last).
/// Chunking by slot rather than by surviving member keeps chunk boundaries stable when the
/// last group is ragged.
let private slotChunks (groupSize: int) (k: int) =
    [ for p in 1 .. ceilDiv groupSize k -> p, (p - 1) * k + 1, min (p * k) groupSize ]

/// Blocks of K*K guides, each read out as K rows then K columns.
let private twoDFragmentedPools (v: int) (k: int) =
    let blocks = ceilDiv v (k * k)

    [ for b in 1..blocks do
          let blockBase = (b - 1) * k * k

          for i in 1..k do
              yield
                  (sprintf "Block %d - Row %d" b i,
                   [ for j in 1..k do
                         let g = blockBase + (i - 1) * k + j
                         if g <= v then yield g ])

          for i in 1..k do
              yield
                  (sprintf "Block %d - Column %d" b i,
                   [ for j in 1..k do
                         let g = blockBase + i + (j - 1) * k
                         if g <= v then yield g ]) ]

/// One ceil(sqrt V) square grid, read out as rows then columns, each split into chunks of K.
let private twoDMatrixPools (v: int) (k: int) =
    let s = ceilSqrt v
    let chunks = slotChunks s k
    let parts = List.length chunks

    [ for r in 1..s do
          for (p, lo, hi) in chunks do
              yield
                  (chunkName (sprintf "Row %d" r) p parts,
                   [ for c in lo..hi do
                         let g = (r - 1) * s + c
                         if g <= v then yield g ])

      for c in 1..s do
          for (p, lo, hi) in chunks do
              yield
                  (chunkName (sprintf "Column %d" c) p parts,
                   [ for r in lo..hi do
                         let g = (r - 1) * s + c
                         if g <= v then yield g ]) ]

/// A ceil(cbrt V) cube sliced along X, Y and Z, each slice split into chunks of K.
let private threeDPools (v: int) (k: int) =
    let c = ceilCbrt v
    let chunks = slotChunks (c * c) k
    let parts = List.length chunks
    let axisNames = [| "X"; "Y"; "Z" |]
    let guideAt x y z = (x - 1) * c * c + (y - 1) * c + z

    // The c*c cells of one slice, in a fixed order so the chunk split is deterministic.
    let sliceCells axis slice =
        [| for a in 1..c do
               for b in 1..c do
                   yield
                       match axis with
                       | 0 -> guideAt slice a b
                       | 1 -> guideAt a slice b
                       | _ -> guideAt a b slice |]

    [ for axis in 0..2 do
          for slice in 1..c do
              let cells = sliceCells axis slice

              for (p, lo, hi) in chunks do
                  yield
                      (chunkName (sprintf "%s-slice %d" axisNames.[axis] slice) p parts,
                       [ for slot in lo..hi do
                             let g = cells.[slot - 1]
                             if g <= v then yield g ]) ]

/// Builds the full well-by-well distribution for one model.
let buildPlan (model: PoolingModel) (format: PlateFormat) (guideCount: int) (wellCapacity: int) =
    validate guideCount wellCapacity

    let raw =
        match model with
        | TwoDFragmented -> twoDFragmentedPools guideCount wellCapacity
        | TwoDMatrix -> twoDMatrixPools guideCount wellCapacity
        | ThreeD -> threeDPools guideCount wellCapacity

    let pools =
        raw
        |> List.mapi (fun i (name, guides) ->
            let id = i + 1

            { Id = id
              Name = name
              GuideIndices = guides
              Well = wellAddress format id })

    { Model = model
      GuideCount = guideCount
      WellCapacity = wellCapacity
      Pools = pools
      TotalWells = List.length pools
      NonEmptyWells = pools |> List.filter (fun p -> not (List.isEmpty p.GuideIndices)) |> List.length
      MaxPoolSize = pools |> List.fold (fun acc p -> max acc (List.length p.GuideIndices)) 0 }
