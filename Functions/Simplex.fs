namespace Carteira

open MathNet.Numerics.LinearAlgebra

/// Projecção Euclidiana no simplex de probabilidade \{ w : w_i \geq 0,\ \sum_i w_i = 1 \}.
module Simplex =

    let projectUnit (v: Vector<float>) : Vector<float> =
        let n = v.Count

        if n = 0 then
            invalidArg (nameof v) "Vector vazio."

        let u = v |> Seq.toArray |> Array.sortDescending
        let mutable rho = 0

        for j in 0 .. n - 1 do
            let partial = Array.sum u.[0..j]

            if u.[j] > (partial - 1.0) / float (j + 1) then
                rho <- j

        let theta = (Array.sum u.[0..rho] - 1.0) / float (rho + 1)
        Vector.Build.Dense(n, fun i -> max 0.0 (v.[i] - theta))
