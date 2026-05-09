namespace Carteira

open MathNet.Numerics.LinearAlgebra

/// Otimização no simplex (sem vendido): algoritmos convexos estáveis.
module LongOnly =

    let frankWolfeMinimumVariance (sigma: Matrix<float>) (maxIter: int) (tol: float) : Vector<float> =
        let n = sigma.RowCount
        let mutable w = Vector.Build.Dense(n, fun _ -> 1.0 / float n)

        for k in 1..maxIter do
            let gw = sigma.Multiply(w)
            let grad = gw.Multiply(2.0)
            let jStar = [| 0 .. n - 1 |] |> Array.minBy (fun i -> grad.[i])

            let s =
                Vector.Build.Dense(n, fun i ->
                    if i = jStar then 1.0 else 0.0)

            let d = s - w
            let sd = sigma.Multiply(d)
            let denom = d.DotProduct(sd)

            let gamma =
                if abs denom < 1e-18 then
                    2.0 / float (k + 2)
                else
                    let num = -w.DotProduct(sigma.Multiply(d))
                    max 0.0 (min 1.0 (num / denom))

            let wNew = w + gamma * d
            w <- wNew

        Simplex.projectUnit w

    /// Gradiente projetado (subida) para maximizar Sharpe mensal no simplex (ponto estacionário local).
    let projectedGradientMaxSharpe
        (sigma: Matrix<float>)
        (mu: Vector<float>)
        (riskFreeMonthly: float)
        (maxIter: int)
        : Vector<float> =
        let n = sigma.RowCount

        let gradSharpe (w: Vector<float>) =
            let sw = sigma.Multiply(w)
            let wSw = sw.DotProduct(w)
            let vol = sqrt (max 1e-18 wSw)
            let excess = mu.DotProduct(w) - riskFreeMonthly
            mu.Multiply(1.0 / vol) - sw.Multiply(excess / (vol * vol * vol))

        let sharpe (w: Vector<float>) =
            Metrics.sharpeMonthly (Optimization.portfolioMean w mu) (Optimization.portfolioVol w sigma) riskFreeMonthly

        let mutable w = Vector.Build.Dense(n, fun _ -> 1.0 / float n)
        let mutable eta = 0.25

        for k in 1..maxIter do
            let g = gradSharpe w
            let cand = Simplex.projectUnit (w + eta * g)
            let sCand = sharpe cand
            let sCur = sharpe w

            if System.Double.IsFinite(sCand) && sCand >= sCur then
                w <- cand
                eta <- min 1.0 (eta * 1.05)
            else
                eta <- max 1e-6 (eta * 0.5)

            if eta < 1e-5 then
                ()

        w
