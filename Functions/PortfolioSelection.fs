namespace Carteira

open System
open MathNet.Numerics.LinearAlgebra

module PortfolioSelection =

    /// Escolhe a carteira com maior Sharpe **mensal** estimado (amostra dada por μ, Σ).
    let bestBySharpe (mu: Vector<float>) (cov: Matrix<float>) (rfMonthly: float) (candidates: (string * Vector<float>) list) =
        let scored =
            candidates
            |> List.choose (fun (label, w) ->
                let m = Optimization.portfolioMean w mu
                let v = Optimization.portfolioVol w cov
                let s = Metrics.sharpeMonthly m v rfMonthly

                if Double.IsFinite s then
                    Some(label, w, s)
                else
                    None)

        match scored with
        | [] ->
            match candidates with
            | (lb, w) :: _ -> lb, w
            | [] -> invalidArg (nameof candidates) "Lista de carteiras vazia."
        | xs ->
            xs |> List.maxBy (fun (_, _, s) -> s) |> fun (lb, w, _) -> lb, w
