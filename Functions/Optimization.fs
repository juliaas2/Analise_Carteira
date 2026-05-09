namespace Carteira

open MathNet.Numerics.LinearAlgebra

module Optimization =

    let portfolioVariance (w: Vector<float>) (sigma: Matrix<float>) : float =
        (sigma * w).DotProduct(w)

    let portfolioVol (w: Vector<float>) (sigma: Matrix<float>) : float =
        sqrt (portfolioVariance w sigma)

    let portfolioMean (w: Vector<float>) (mu: Vector<float>) : float =
        w.DotProduct(mu)

    /// Carteira de variância global mínima (investimento total = 1, shorts permitidos).
    let globalMinimumVarianceWeights (sigma: Matrix<float>) : Vector<float> =
        let invSigma = sigma.Inverse()
        let n = sigma.RowCount
        let one = Vector.Build.Dense(n, fun _ -> 1.0)
        let z = invSigma * one
        let s = z.Sum()

        if abs s < 1e-14 then
            invalidOp "Soma dos pesos auxiliares ~ 0; matriz de covariância pode ser singular."

        z / s

    /// Carteira tangente (máximo Sharpe), Σ w = 1; pesos proporcionais a Σ⁻¹(μ − r_f).
    let tangencyWeights (sigma: Matrix<float>) (mu: Vector<float>) (riskFreeMonthly: float) : Vector<float> =
        let invSigma = sigma.Inverse()
        let n = sigma.RowCount
        let rf = Vector.Build.Dense(n, fun _ -> riskFreeMonthly)
        let excess = mu - rf
        let z = invSigma * excess
        let s = z.Sum()

        if abs s < 1e-14 then
            invalidOp "Soma dos pesos tangentes ~ 0."

        z / s

    /// Fronteira eficiente: mínima variância com E[r]=rp e Σ w = 1 (shorts permitidos).
    let frontierWeightsForTargetReturn
        (sigma: Matrix<float>)
        (mu: Vector<float>)
        (targetMeanMonthly: float)
        : Vector<float> =
        let invSigma = sigma.Inverse()
        let n = sigma.RowCount
        let iota = Vector.Build.Dense(n, fun _ -> 1.0)
        let v1 = invSigma.Multiply(iota)
        let v2 = invSigma.Multiply(mu)
        let a11 = iota.DotProduct(v1)
        let a12 = iota.DotProduct(v2)
        let a21 = mu.DotProduct(v1)
        let a22 = mu.DotProduct(v2)
        let det = a11 * a22 - a12 * a21

        if abs det < 1e-18 then
            invalidOp "Sistema degenerado ao resolver fronteira eficiente."

        let rp = targetMeanMonthly
        let lambda1 = (a22 - rp * a12) / det
        let lambda2 = (rp * a11 - a21) / det
        let linComb = lambda1 * iota + lambda2 * mu
        invSigma.Multiply(linComb)

    let equalWeights (n: int) : Vector<float> =
        Vector.Build.Dense(n, fun _ -> 1.0 / float n)
