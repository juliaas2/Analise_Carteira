namespace Carteira

open System
open MathNet.Numerics.LinearAlgebra

module DateSeries =

    /// Atribui datas mensais consecutivas: a **última** linha do CSV corresponde a `lastMonthFirstDay`.
    let attachSyntheticMonthly (data: AssetReturns) (lastMonthFirstDay: DateTime) : DatedAssetReturns =
        let rows = data.Matrix.RowCount

        if rows < 1 then
            invalidArg (nameof data) "Matriz vazia."

        let dates =
            Array.init rows (fun i -> lastMonthFirstDay.AddMonths(-(rows - 1 - i)))

        { Tickers = data.Tickers
          Dates = dates
          Matrix = data.Matrix }

    /// Índices de linhas cujo mês cai no trimestre calendário (1–4).
    let indicesInQuarter (dr: DatedAssetReturns) (year: int) (quarter: int) : int[] =
        let okMonth m =
            match quarter with
            | 1 -> m >= 1 && m <= 3
            | 2 -> m >= 4 && m <= 6
            | 3 -> m >= 7 && m <= 9
            | 4 -> m >= 10 && m <= 12
            | _ -> invalidArg (nameof quarter) "Trimestre deve ser 1..4."

        dr.Dates
        |> Array.mapi (fun i d -> i, d)
        |> Array.filter (fun (_, d) -> d.Year = year && okMonth d.Month)
        |> Array.map fst

    let sliceByRowIndices (dr: DatedAssetReturns) (rowIndices: int[]) : DatedAssetReturns =
        let cols = dr.Matrix.ColumnCount
        let k = rowIndices.Length
        let m = Matrix.Build.Dense(k, cols)

        let dates = Array.zeroCreate k

        for ni in 0 .. k - 1 do
            let ri = rowIndices.[ni]

            if ri < 0 || ri >= dr.Matrix.RowCount then
                invalidArg (nameof rowIndices) $"Índice de linha inválido: {ri}."

            dates.[ni] <- dr.Dates.[ri]

            for j in 0 .. cols - 1 do
                m.[ni, j] <- dr.Matrix.[ri, j]

        { Tickers = dr.Tickers
          Dates = dates
          Matrix = m }

    /// Apenas a matriz de retornos (sem datas), útil para \(\hat\mu\) e \(\hat\Sigma\).
    let toAssetReturns (dr: DatedAssetReturns) : AssetReturns =
        { Tickers = dr.Tickers
          Matrix = dr.Matrix }
