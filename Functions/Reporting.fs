namespace Carteira

open System.Globalization
open System.IO

module Reporting =

    /// CSV com separador ';' e cultura invariante (compatível com Excel em PT).
    let writeFrontierCsv (path: string) (rows: seq<float * float * string>) =
        use w = new StreamWriter(path)

        w.WriteLine("sigma_mensal;retorno_mensal;rotulo")

        for sigM, retM, lab in rows do
            w.WriteLine(
                sprintf
                    "%s;%s;%s"
                    (sigM.ToString("G17", CultureInfo.InvariantCulture))
                    (retM.ToString("G17", CultureInfo.InvariantCulture))
                    (lab.Replace(';', ',').Replace('\n', ' '))
            )
