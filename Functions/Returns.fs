namespace Carteira

open MathNet.Numerics.LinearAlgebra

module Returns =

    let loadFromCsv (path: string) : AssetReturns =
        let lines =
            System.IO.File.ReadAllLines(path)
            |> Array.filter (System.String.IsNullOrWhiteSpace >> not)

        if lines.Length < 2 then
            invalidArg (nameof path) "O CSV precisa de cabeçalho e pelo menos uma linha de dados."

        let tickers = lines.[0].Split(',') |> Array.map (fun s -> s.Trim())
        let n = tickers.Length
        let rowCount = lines.Length - 1
        let m: Matrix<float> = Matrix.Build.Dense(rowCount, n)

        for t in 0 .. rowCount - 1 do
            let parts = lines.[t + 1].Split(',')

            if parts.Length <> n then
                invalidArg (nameof path) $"Linha {t + 2}: esperadas {n} colunas, obtidas {parts.Length}."

            for j in 0 .. n - 1 do
                m.[t, j] <-
                    System.Double.Parse(parts.[j], System.Globalization.CultureInfo.InvariantCulture)

        { Tickers = tickers
          Matrix = m }

    let columnMeans (data: AssetReturns) =
        let m = data.Matrix
        let rows = m.RowCount
        let cols = m.ColumnCount

        Vector.Build.Dense(cols, fun j ->
            let mutable s = 0.0

            for i in 0 .. rows - 1 do
                s <- s + m.[i, j]

            s / float rows)

    /// Covariância amostral (denominador n − 1).
    let sampleCovariance (data: AssetReturns) =
        let m = data.Matrix
        let t = m.RowCount
        let n = m.ColumnCount
        let mu = columnMeans data
        let dof = float (t - 1)
        let cov = Matrix.Build.Dense(n, n)

        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                let mutable s = 0.0

                for row in 0 .. t - 1 do
                    s <- s + (m.[row, i] - mu.[i]) * (m.[row, j] - mu.[j])

                cov.[i, j] <- s / dof

        cov
