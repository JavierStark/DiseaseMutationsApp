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
    
   