namespace Carteira

open MathNet.Numerics.LinearAlgebra

module Metrics =

    let sharpeMonthly (meanMonthly: float) (volMonthly: float) (rfMonthly: float) =
        if volMonthly < 1e-14 then
            nan
        else
            (meanMonthly - rfMonthly) / volMonthly

    /// Covariância anual a partir da mensal (retornos simples i.i.d. aprox.).
    let annualizeCovarianceMonthly (covMonthly: Matrix<float>) : Matrix<float> =
        covMonthly * 12.0

    let annualizeMeanMonthly (muMonthly: Vector<float>) : Vector<float> =
        muMonthly * 12.0

    let monthlyFromAnnualRateSimple (annualRate: float) : float =
        annualRate / 12.0
