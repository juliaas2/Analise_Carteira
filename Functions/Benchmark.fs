namespace Carteira

open System
open System.Diagnostics

module Benchmark =

    /// Uma corrida de aquecimento + várias cronometragens de `work` (deve devolver um valor usado para evitar dead-code).
    let timedRuns (warmupRuns: int) (timedRunsCount: int) (work: unit -> float) =
        for _ in 1..warmupRuns do
            work () |> ignore

        [| for _ in 1..timedRunsCount ->
            let sw = Stopwatch.StartNew()
            let sink = work ()
            sw.Stop()
            ignore sink
            sw.Elapsed.TotalMilliseconds |]

    let mean (xs: float[]) =
        Array.sum xs / float xs.Length

    let sampleStd (xs: float[]) =
        let m = mean xs

        if xs.Length < 2 then
            0.0
        else
            let var =
                (xs
                 |> Array.sumBy (fun x ->
                     let d = x - m
                     d * d))
                / float (xs.Length - 1)

            sqrt var
