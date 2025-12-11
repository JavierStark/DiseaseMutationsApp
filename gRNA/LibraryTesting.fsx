// Basic Option-style computation expression
type OptionBuilder() =
    member _.Bind(m, f) = Option.bind f m
    member _.Return(x) = Some x
    member _.ReturnFrom(m) = m
    member _.Zero() = None

let option = OptionBuilder()

// Usage examples
let addSome a b =
    option {
        let! x = a
        let! y = b
        return x + y
    }

let r1 = addSome (Some 2) (Some 3)    // Some 5
let r2 = addSome None (Some 3)       // None

// Print results (for script execution)
printfn "r1 = %A" r1
printfn "r2 = %A" r2