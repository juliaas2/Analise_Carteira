module FrontierHtml

open System
open System.Globalization
open System.IO
open System.Text

/// Gráfico interativo (Plotly via CDN): não depende de pacotes NuGet com API mutável.
let write (path: string) (frontierVol: float list) (frontierRet: float list) (markers: (float * float * string) list) =
    let sb (xs: float seq) =
        let b = StringBuilder()
        let mutable first = true

        for x in xs do
            if first then
                first <- false
            else
                b.Append(',') |> ignore

            b.Append(x.ToString("G17", CultureInfo.InvariantCulture)) |> ignore

        b.ToString()

    let escapeJs (t: string) =
        t.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ")

    let markerXs = markers |> List.map (fun (v, _, _) -> v)
    let markerYs = markers |> List.map (fun (_, r, _) -> r)
    let markerText = markers |> List.map (fun (_, _, lab) -> sprintf "\"%s\"" (escapeJs lab))

    let markerTextArr = String.Join(",", markerText |> Array.ofList)

    let html =
        $"""<!DOCTYPE html>
<html lang="pt"><head><meta charset="utf-8"/><title>Fronteira eficiente</title>
<script src="https://cdn.plot.ly/plotly-2.27.0.min.js"></script></head>
<body style="font-family:system-ui;margin:16px;">
<h2>Fronteira eficiente de Markowitz (mensal)</h2>
<div id="chart" style="width:900px;height:600px;"></div>
<script>
const traceLine = {{
  x: [{sb frontierVol}],
  y: [{sb frontierRet}],
  mode: 'lines+markers',
  name: 'Fronteira (short sales)',
  line: {{ color: '#2563eb', width: 2 }},
  marker: {{ size: 7 }}
}};
const tracePts = {{
  x: [{sb markerXs}],
  y: [{sb markerYs}],
  mode: 'markers+text',
  name: 'Carteiras de referência',
  marker: {{ size: 12, color: '#dc2626' }},
  text: [{markerTextArr}],
  textposition: 'top center'
}};
Plotly.newPlot('chart', [traceLine, tracePts], {{
  title: 'σ × E[r] (amostra completa)',
  xaxis: {{ title: 'Volatilidade mensal σ' }},
  yaxis: {{ title: 'Retorno esperado mensal E[r]' }},
  showlegend: true
}});
</script></body></html>"""

    File.WriteAllText(path, html)
