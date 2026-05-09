namespace Carteira

open MathNet.Numerics.LinearAlgebra

/// Geração da fronteira (vários alvos de retorno) para benchmarks sequencial vs paralelo.
module FrontierSweep =

    let private tryPoint (cov: Matrix<float>) (mu: Vector<float>) (rfMonthly: float) (rp: float) =
        try
            let w = Optimization.frontierWeightsForTargetReturn cov mu rp
            let vol = Optimization.portfolioVol w cov
            Some(vol + rp * 1e-12 + Metrics.sharpeMonthly rp vol rfMonthly * 1e-15)
        with _ ->
            None

    /// Soma dos σ como “sink” para evitar que o JIT elimine o trabalho (volatilidade > 0).
    let volatileSumSequential (cov: Matrix<float>) (mu: Vector<float>) (rfMonthly: float) (rpLow: float) (rpHigh: float) (steps: int) =
        let mutable acc = 0.0

        for k in 0..steps do
            let t = float k / float steps
            let rp = rpLow + t * (rpHigh - rpLow)

            match tryPoint cov mu rfMonthly rp with
            | Some sink -> acc <- acc + sink
            | None -> ()

        acc

    let volatileSumParallel (cov: Matrix<float>) (mu: Vector<float>) (rfMonthly: float) (rpLow: float) (rpHigh: float) (steps: int) =
        let parts =
            Array.Parallel.init (steps + 1) (fun k ->
                let t = float k / float steps
                let rp = rpLow + t * (rpHigh - rpLow)

                match tryPoint cov mu rfMonthly rp with
                | Some s -> s
                | None -> 0.0)

        Array.sum parts
