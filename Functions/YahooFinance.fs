namespace Carteira

open System
open System.Net.Http
open System.Text.Json
open MathNet.Numerics.LinearAlgebra

/// Descarga histórico mensal (preço ajustado) via endpoint público **chart** do Yahoo (não oficial; pode mudar ou limitar pedidos).
module YahooFinance =

    let dow30Tickers =
        [| "AAPL"
           "AMGN"
           "AMZN"
           "AXP"
           "BA"
           "CAT"
           "CRM"
           "CSCO"
           "CVX"
           "DIS"
           "GS"
           "HD"
           "HON"
           "IBM"
           "JNJ"
           "JPM"
           "KO"
           "MCD"
           "MMM"
           "MRK"
           "MSFT"
           "NKE"
           "NVDA"
           "PG"
           "SHW"
           "TRV"
           "UNH"
           "V"
           "VZ"
           "WMT" |]

    let private http =
        lazy
            let c = new HttpClient()
            c.Timeout <- TimeSpan.FromSeconds 45.

            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36"
            )

            c.DefaultRequestHeaders.Accept.ParseAdd("application/json")
            c

    let private tryProp (name: string) (e: JsonElement) =
        match e.TryGetProperty(name) with
        | true, x -> Some x
        | _ -> None

    let private monthKey (d: DateTime) = d.Year * 100 + d.Month

    let private monthFromUnix (sec: int64) =
        let dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime
        DateTime(dt.Year, dt.Month, 1)

    /// Devolve mapa mês → preço (adj. close preferencial).
    let fetchMonthlyAdjMap (symbol: string) (period1: int64) (period2: int64) : Map<DateTime, float> =
        let enc = Uri.EscapeDataString symbol

        let url =
            sprintf
                "https://query1.finance.yahoo.com/v8/finance/chart/%s?period1=%d&period2=%d&interval=1mo&events=div%%2Csplits"
                enc
                period1
                period2

        let json =
            try
                http.Value.GetStringAsync(url).GetAwaiter().GetResult()
            with ex ->
                raise (InvalidOperationException(sprintf "Falha HTTP ao pedir '%s'." symbol, ex))

        let doc =
            try
                JsonDocument.Parse(json, JsonDocumentOptions())
            with :? JsonException ->
                let clip = min json.Length 200
                invalidOp (sprintf "Resposta não-JSON ao pedir '%s': %s" symbol json.[0 .. clip - 1])

        use doc = doc

        match tryProp "chart" doc.RootElement with
        | None -> invalidOp "Resposta Yahoo sem 'chart'."
        | Some chart ->
            let results = chart.GetProperty("result")

            if results.GetArrayLength() = 0 then
                let hint =
                    match tryProp "error" chart with
                    | Some e -> e.GetRawText()
                    | None -> ""

                invalidOp (
                    sprintf
                        "Yahoo devolveu série vazia para '%s'. %s"
                        symbol
                        (if hint.Length > 0 then hint else "(sem detalhe error)")
                )
            else
                let r0 = results.[0]

                let ts =
                    [| for x in r0.GetProperty("timestamp").EnumerateArray() -> x.GetInt64() |]

                let prices: float[] =
                    let ind = r0.GetProperty("indicators")

                    let fromIndAdj =
                        match tryProp "adjclose" ind with
                        | Some outer when outer.GetArrayLength() > 0 ->
                            let a0 = outer.[0].GetProperty("adjclose")

                            [| for x in a0.EnumerateArray() ->
                                if x.ValueKind = JsonValueKind.Null then
                                    nan
                                else
                                    x.GetDouble() |]
                        | _ -> [||]

                    let quote0 = ind.GetProperty("quote").[0]

                    let fromQuote =
                        if fromIndAdj.Length = ts.Length then
                            fromIndAdj
                        else
                            let fromQuoteAdj =
                                match tryProp "adjclose" quote0 with
                                | Some arr ->
                                    [| for x in arr.EnumerateArray() ->
                                        if x.ValueKind = JsonValueKind.Null then
                                            nan
                                        else
                                            x.GetDouble() |]
                                | None -> [||]

                            if fromQuoteAdj.Length = ts.Length then
                                fromQuoteAdj
                            else
                                match tryProp "close" quote0 with
                                | Some arr ->
                                    [| for x in arr.EnumerateArray() ->
                                        if x.ValueKind = JsonValueKind.Null then
                                            nan
                                        else
                                            x.GetDouble() |]
                                | None -> [||]

                    if fromQuote.Length <> ts.Length then
                        invalidOp (sprintf "Série de preços incompatível com timestamps para '%s'." symbol)

                    fromQuote

                (ts, prices)
                ||> Array.zip
                |> Array.map (fun (t, p) -> monthFromUnix t, p)
                |> Array.filter (fun (_, p) -> p |> Double.IsFinite)
                |> Array.fold
                    (fun (m: Map<DateTime, float>) (d, p) ->
                        // último valor ganha se repetir mês
                        Map.add d p m)
                    Map.empty

    /// Alinha todos os tickers, calcula retornos mensais simples e datas do **mês de fim** do período.
    let downloadDow30Monthly (startYear: int) : DatedAssetReturns =
        let period1 = DateTimeOffset(DateTime(startYear, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds()
        let period2 = DateTimeOffset.UtcNow.AddDays(2.).ToUnixTimeSeconds()

        printfn "  [Yahoo] A pedir %d símbolos (mensal)..." dow30Tickers.Length

        let maps =
            dow30Tickers
            |> Array.mapi (fun i sym ->
                if i > 0 && i % 5 = 0 then
                    System.Threading.Thread.Sleep 120

                sym, fetchMonthlyAdjMap sym period1 period2)

        let commonMonths =
            maps
            |> Array.map (fun (_, mp) -> mp.Keys |> Seq.map monthKey |> Set.ofSeq)
            |> fun sets ->
                match sets |> Array.toList with
                | [] -> Set.empty
                | h :: t -> List.fold Set.intersect h t

        if commonMonths.Count < 40 then
            invalidOp
                $"Poucos meses comuns entre tickers ({commonMonths.Count}). Verifique rede ou bloqueio Yahoo."

        let sortedMonths =
            commonMonths |> Seq.sort |> Seq.toArray

        let sortedDates =
            sortedMonths |> Array.map (fun k -> DateTime(k / 100, k % 100, 1))

        let priceRows =
            sortedDates.Length

        let priceMat = Matrix.Build.Dense(priceRows, dow30Tickers.Length)

        for ti in 0 .. dow30Tickers.Length - 1 do
            let sym = dow30Tickers.[ti]
            let _, mp = maps.[ti]

            for row in 0 .. priceRows - 1 do
                let d = sortedDates.[row]

                match Map.tryFind d mp with
                | Some px -> priceMat.[row, ti] <- px
                | None -> invalidOp (sprintf "Falta preço %s em %04d-%02d" sym d.Year d.Month)

        let retRows = priceRows - 1

        if retRows < 10 then
            invalidOp "Série demasiado curta após alinhamento."

        let retMat = Matrix.Build.Dense(retRows, dow30Tickers.Length)
        let datesRet = Array.zeroCreate retRows

        for t in 1 .. priceRows - 1 do
            datesRet.[t - 1] <- sortedDates.[t]

            for j in 0 .. dow30Tickers.Length - 1 do
                let p0 = priceMat.[t - 1, j]
                let p1 = priceMat.[t, j]

                if p0 < 1e-12 then
                    invalidOp "Preço inicial ~0 ao calcular retorno."

                retMat.[t - 1, j] <- p1 / p0 - 1.0

        { Tickers = dow30Tickers
          Dates = datesRet
          Matrix = retMat }
