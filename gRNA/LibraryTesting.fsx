
#r "nuget: Plotly.NET, 5.0.0"
#r "nuget: Plotly.NET.Interactive, 5.0.0"

open Plotly.NET

let data = [1..7]

let square =
    data
    |> List.map (fun x -> x * x)

// Basic column chart
square
|> Chart.Column
|> Chart.withTitle "Square Numbers"
|> Chart.show

// Line chart with styling
square
|> Chart.Line(data)
|> Chart.withTitle "Squares Line Chart"
|> Chart.withXAxisStyle(TitleText = "X")
|> Chart.withYAxisStyle(TitleText = "X²")
|> Chart.show

// Scatter plot
let randomData = List.init 50 (fun _ -> System.Random().NextDouble() * 10.0)
let randomData2 = List.init 50 (fun _ -> System.Random().NextDouble() * 10.0)

Chart.Scatter(randomData, randomData2, mode = StyleParam.Mode.Markers)
|> Chart.withTitle "Random Scatter"
|> Chart.show

// Multiple series combined
[
    Chart.Line(data, square, Name = "Squares")
    Chart.Line(data, List.map (fun x -> x * x * x) data, Name = "Cubes")
]
|> Chart.combine
|> Chart.withTitle "Squares vs Cubes"
|> Chart.show

// Pie chart
let labels = ["A"; "B"; "C"; "D"]
let values = [30.0; 20.0; 25.0; 25.0]

Chart.Pie(values, labels)
|> Chart.withTitle "Distribution"
|> Chart.show

// Bar chart
Chart.Bar(labels, values)
|> Chart.withTitle "Categories"
|> Chart.show
