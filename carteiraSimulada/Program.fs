open System
open System.IO
open Carteira
open Carteira.Returns
open Carteira.Optimization
open Carteira.Metrics
open Carteira.LongOnly
open Carteira.Backtest
open Carteira.Reporting
open Carteira.DateSeries
open Carteira.PortfolioSelection
open Carteira.YahooFinance
open MathNet.Numerics.LinearAlgebra

let resolveCsvPath (argv: string[]) =
    if argv.Length > 0 && File.Exists(argv.[0]) then
        argv.[0]
    else
        Path.Combine(AppContext.BaseDirectory, "dow30_returns.csv")

let useYahoo (argv: string[]) = argv |> Array.exists ((=) "--yahoo")

let argvWithoutFlags (argv: string[]) =
    argv |> Array.filter (fun s -> s <> "--yahoo" && not (String.IsNullOrWhiteSpace s))

let printWeights (tickers: string[]) (w: Vector<float>) (title: string) =
    printfn ""
    printfn "%s" title

    Array.zip tickers (w |> Seq.toArray)
    |> Array.sortByDescending snd
    |> Array.iter (fun (t, x) -> printfn "  %s\t%.4f" t x)

    printfn "  --- soma pesos: %.6f" (w.Sum())

[<EntryPoint>]
let main argv =
    try
        let yahoo = useYahoo argv
        let pathArgv = argvWithoutFlags argv
        let csvPath = resolveCsvPath pathArgv

        let outDir = AppContext.BaseDirectory

        let datedFull, data, sourceDescr =
            if yahoo then
                printfn "Fonte: Yahoo Finance (API chart v8, histórico mensal, preço ajustado)."
                let dr = downloadDow30Monthly 2015
                dr, toAssetReturns dr, "Yahoo Finance API"
            else
                if not (File.Exists csvPath) then
                    eprintfn "Arquivo não encontrado: %s" csvPath
                    eprintfn "Uso: dotnet run --project carteiraSimulada -- [--yahoo] [caminho/dados.csv]"
                    exit 1

                let d = loadFromCsv csvPath
                let syn = attachSyntheticMonthly d (DateTime(2025, 3, 1))
                syn, toAssetReturns syn, csvPath

        let mu = columnMeans data
        let cov = sampleCovariance data
        let muAnn = annualizeMeanMonthly mu
        let n = mu.Count
        let rows = data.Matrix.RowCount

        let tickersWithMeans =
            Array.zip data.Tickers (muAnn |> Seq.toArray)
            |> Array.sortByDescending snd

        let rfAnnual = 0.02
        let rfMonthly = monthlyFromAnnualRateSimple rfAnnual

        printfn "=== Carteira simulada — Markowitz & extensões ==="
        printfn "Períodos: %d | Ativos: %d | Dados: %s" rows n sourceDescr
        printfn "Taxa livre de risco (anual, simples): %.2f%%" (rfAnnual * 100.0)
        printfn "Nota: carteiras \"short\" permitem pesos negativos; \"long-only\" restringem w_i ≥ 0."

        let wEq = equalWeights n

        let wGmvp = globalMinimumVarianceWeights cov
        let wTan = tangencyWeights cov mu rfMonthly

        let wGmvpLo = frankWolfeMinimumVariance cov 6000 1e-9
        let wSharpeLo = projectedGradientMaxSharpe cov mu rfMonthly 3000

        let muEq = portfolioMean wEq mu
        let volEq = portfolioVol wEq cov

        let muGmvp = portfolioMean wGmvp mu
        let volGmvp = portfolioVol wGmvp cov

        let muTan = portfolioMean wTan mu
        let volTan = portfolioVol wTan cov

        let muGmvpLo = portfolioMean wGmvpLo mu
        let volGmvpLo = portfolioVol wGmvpLo cov

        let muSharpeLo = portfolioMean wSharpeLo mu
        let volSharpeLo = portfolioVol wSharpeLo cov

        printfn ""
        printfn "--- Retorno e risco em amostra COMPLETA (mensal) ---"
        printfn "%-30s %12s %12s %12s" "Carteira" "E[r]" "σ" "Sharpe"

        let row label m v =
            printfn "%-30s %12.6f %12.6f %12.4f" label m v (sharpeMonthly m v rfMonthly)

        row "1/N" muEq volEq
        row "GMVP (short)" muGmvp volGmvp
        row "Tangente / máx. Sharpe (short)" muTan volTan
        row "GMVP long-only (Frank–Wolfe)" muGmvpLo volGmvpLo
        row "Máx. Sharpe long-only (grad. proj.)" muSharpeLo volSharpeLo

        printfn ""
        printfn "--- Equivalentes anuais (E[r]×12 ; σ×√12) ---"

        let rowAnn label m v =
            printfn "%-30s %12.4f %% %12.4f %% %12.4f" label (m * 1200.0) (v * sqrt 12.0 * 100.0) (sharpeMonthly m v rfMonthly)

        rowAnn "1/N" muEq volEq
        rowAnn "GMVP (short)" muGmvp volGmvp
        rowAnn "Tangente (short)" muTan volTan
        rowAnn "GMVP long-only" muGmvpLo volGmvpLo
        rowAnn "Sharpe long-only" muSharpeLo volSharpeLo

        printWeights data.Tickers wGmvp "Pesos — GMVP (short)"
        printWeights data.Tickers wTan "Pesos — tangente (short)"
        printWeights data.Tickers wGmvpLo "Pesos — GMVP long-only"
        printWeights data.Tickers wSharpeLo "Pesos — máximo Sharpe long-only (aprox.)"

        let rpLow = portfolioMean wGmvp mu
        let rpHigh = mu |> Seq.max

        printfn ""
        printfn "--- Fronteira eficiente analítica (short) entre GMVP e max μ_i ---"
        printfn "%12s %12s %12s" "E[r] alvo" "σ" "Sharpe"

        let frontierVolRet = ResizeArray<float * float>()
        let csvRows = ResizeArray<float * float * string>()
        let steps = 40

        for k in 0..steps do
            let t = float k / float steps
            let rp = rpLow + t * (rpHigh - rpLow)

            try
                let w = frontierWeightsForTargetReturn cov mu rp
                let vol = portfolioVol w cov
                printfn "%12.6f %12.6f %12.4f" rp vol (sharpeMonthly rp vol rfMonthly)
                frontierVolRet.Add(vol, rp)
                csvRows.Add(vol, rp, "fronteira_short")
            with _ ->
                ()

        let markerSpecs =
            [ volEq, muEq, "1/N"
              volGmvp, muGmvp, "GMVP_short"
              volTan, muTan, "Tangente_short"
              volGmvpLo, muGmvpLo, "GMVP_long_only"
              volSharpeLo, muSharpeLo, "Sharpe_long_only" ]

        for (v, r, lab) in markerSpecs do
            csvRows.Add(v, r, lab)

        let csvPathOut = Path.Combine(outDir, "fronteira_pontos.csv")
        writeFrontierCsv csvPathOut csvRows

        let htmlPathOut = Path.Combine(outDir, "fronteira_eficiente.html")

        FrontierHtml.write
            htmlPathOut
            (frontierVolRet |> Seq.map fst |> List.ofSeq)
            (frontierVolRet |> Seq.map snd |> List.ofSeq)
            markerSpecs

        printfn ""
        printfn "Artefactos escritos em:"
        printfn "  %s" csvPathOut
        printfn "  %s" htmlPathOut

        // --- Q1 2025: estimação sem jan–mar, melhor carteira por Sharpe, avaliação realizada no trimestre ---
        let dated = datedFull
        let lastObs = dated.Dates.[dated.Dates.Length - 1]
        let q1Ix = indicesInQuarter dated 2025 1
        let q1Set = Set.ofArray q1Ix

        if q1Ix.Length = 0 then
            printfn ""
            printfn "(!) Sem observações em Q1 2025 nas datas sintéticas."
        elif rows - q1Ix.Length < n + 2 then
            printfn ""
            printfn "(!) Poucas observações fora do Q1 2025 para estimar Σ."
        else
            let estIx = [| for i in 0 .. rows - 1 do if not (Set.contains i q1Set) then yield i |]

            let estSlice = sliceByRowIndices dated estIx |> toAssetReturns
            let muEst = columnMeans estSlice
            let covEst = sampleCovariance estSlice

            let wTanEst = tangencyWeights covEst muEst rfMonthly
            let wGmvpEst = globalMinimumVarianceWeights covEst
            let wGmvpLoEst = frankWolfeMinimumVariance covEst 6000 1e-9
            let wSharpeLoEst = projectedGradientMaxSharpe covEst muEst rfMonthly 3000
            let wEqEst = equalWeights n

            let bestLabel, wBest =
                PortfolioSelection.bestBySharpe muEst covEst rfMonthly [
                    "Tangente_short", wTanEst
                    "GMVP_short", wGmvpEst
                    "GMVP_long_only", wGmvpLoEst
                    "Sharpe_long_only", wSharpeLoEst
                    "1_N", wEqEst
                ]

            let q1Dr = sliceByRowIndices dated q1Ix
            let q1Slice = toAssetReturns q1Dr
            let q1Dates = q1Dr.Dates
            let qSeries = portfolioReturnSeries q1Slice wBest

            printfn ""
            printfn "--- Teste no 1.º trimestre de 2025 ---"

            printfn
                "Conv.: cada linha = retorno mensal; último mês na série = %04d-%02d. Q1 2025 = %d mês(es) com datas jan–mar de 2025."
                lastObs.Year
                lastObs.Month
                q1Ix.Length

            printfn "Carteira com maior Sharpe **na amostra de estimação** (exclui Q1): %s" bestLabel

            for i in 0 .. qSeries.Count - 1 do
                printfn "  %04d-%02d  retorno carteira: %12.6f" q1Dates.[i].Year q1Dates.[i].Month qSeries.[i]

            let cumSimple =
                Seq.fold (fun acc r -> acc * (1.0 + r)) 1.0 qSeries - 1.0

            let mQ, vQ, sQ = summaryMonthly qSeries rfMonthly

            printfn "Retorno acumulado simples no trimestre: %.6f  (~ %.4f %%)" cumSimple (cumSimple * 100.0)
            printfn "Sobre %d meses: média mensal=%.6f  σ=%.6f  Sharpe mensal=%.4f  (poucos períodos → Sharpe pouco informativo)"
                qSeries.Count mQ vQ sQ

        let benchSteps = 500

        printfn ""
        printfn "--- Benchmark: fronteira (%d alvos), sequencial vs paralelo — 8 corridas cada (2 aquecimento) ---" benchSteps

        let seqMs =
            Benchmark.timedRuns 2 8 (fun () ->
                FrontierSweep.volatileSumSequential cov mu rfMonthly rpLow rpHigh benchSteps)

        let parMs =
            Benchmark.timedRuns 2 8 (fun () ->
                FrontierSweep.volatileSumParallel cov mu rfMonthly rpLow rpHigh benchSteps)

        let chkSeq = FrontierSweep.volatileSumSequential cov mu rfMonthly rpLow rpHigh benchSteps
        let chkPar = FrontierSweep.volatileSumParallel cov mu rfMonthly rpLow rpHigh benchSteps

        printfn "Sequencial: média %8.3f ms | desvio amostral %7.3f ms" (Benchmark.mean seqMs) (Benchmark.sampleStd seqMs)

        printfn "Paralelo:   média %8.3f ms | desvio amostral %7.3f ms" (Benchmark.mean parMs) (Benchmark.sampleStd parMs)

        printfn "Speed-up médio (seq/par): %.2f×" ((Benchmark.mean seqMs) / max (Benchmark.mean parMs) 1e-9)
        printfn "Checksum soma volatilidades (diag. consistência): seq=%.10g  par=%.10g" chkSeq chkPar

        // Validação fora da amostra (treino / teste)
        let trainFrac = 0.72
        let trainRows = max (n + 3) (int (floor (float rows * trainFrac)))
        let testRows = rows - trainRows

        if testRows >= 6 then
            printfn ""
            printfn "--- Pesos estimados no TREINO (%d meses), desempenho realizado no TESTE (%d meses) ---" trainRows testRows

            let trainD = sliceRows data 0 trainRows
            let testD = sliceRows data trainRows testRows

            let muTr = columnMeans trainD
            let covTr = sampleCovariance trainD

            let wEqT = equalWeights n
            let wGmvpT = globalMinimumVarianceWeights covTr
            let wTanT = tangencyWeights covTr muTr rfMonthly
            let wGmvpLoT = frankWolfeMinimumVariance covTr 6000 1e-9
            let wSharpeLoT = projectedGradientMaxSharpe covTr muTr rfMonthly 3000

            let eval label w =
                let series = portfolioReturnSeries testD w
                let m, v, s = summaryMonthly series rfMonthly
                printfn "%-32s E[r]=%.6f σ=%.6f Sharpe=%.4f" label m v s

            eval "1/N" wEqT
            eval "GMVP (short)" wGmvpT
            eval "Tangente (short)" wTanT
            eval "GMVP long-only" wGmvpLoT
            eval "Sharpe long-only" wSharpeLoT
        else
            printfn ""
            printfn "(Treino/teste omitido: série temporal curta demais para subdivisão útil.)"

        printfn ""
        printfn "Retorno médio anualizado por ativo (ordenado):"

        tickersWithMeans
        |> Array.iter (fun (t, m) -> printfn "  %s\t%.4f %%" t (m * 100.0))

        0

    with ex ->
        eprintfn "Erro: %s" ex.Message
        exit 2
