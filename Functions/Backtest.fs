namespace Carteira

open MathNet.Numerics.LinearAlgebra

module Backtest =

    let sliceRows (data: AssetReturns) (startRow: int) (rowCount: int) : AssetReturns =
        let m = data.Matrix
        let cols = m.ColumnCount

        if startRow < 0 || rowCount < 1 || startRow + rowCount > m.RowCount then
            invalidArg "slice" "Intervalo inválido para subdividir a amostra."

        let block = Matrix.Build.Dense(rowCount, cols)

        for i in 0 .. rowCount - 1 do
            for j in 0 .. cols - 1 do
                block.[i, j] <- m.[startRow + i, j]

        { Tickers = data.Tickers
          Matrix = block }

    /// Série temporal da carteira com pesos fixos: r_p,t = \sum_j w_j r_j,t.
    let portfolioReturnSeries (data: AssetReturns) (w: Vector<float>) : Vector<float> =
        let m = data.Matrix
        let rows = m.RowCount
        let cols = m.ColumnCount

        if w.Count <> cols then
            invalidOp "Dimensão dos pesos não coincide com o número de colunas."

        Vector.Build.Dense(rows, fun t ->
            let mutable acc = 0.0

            for j in 0 .. cols - 1 do
                acc <- acc + w.[j] * m.[t, j]

            acc)

    /// Média amostral, volatilidade amostral e Sharpe mensal da série (rf constante por período).
    let summaryMonthly (series: Vector<float>) (rfMonthly: float) =
        let n = float series.Count

        if series.Count < 2 then
            nan, nan, nan
        else
            let mean = series.Sum() / n

            let var =
                (series
                 |> Seq.sumBy (fun x ->
                     let d = x - mean
                     d * d))
                / (n - 1.0)

            let vol = sqrt var
            mean, vol, Metrics.sharpeMonthly mean vol rfMonthly
