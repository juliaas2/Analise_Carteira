namespace Carteira

open System
open MathNet.Numerics.LinearAlgebra

type AssetReturns =
    { Tickers: string[]
      Matrix: Matrix<float> }

/// Séries alinhadas com uma data por linha (ex.: mensal, primeiro dia do mês).
type DatedAssetReturns =
    { Tickers: string[]
      Dates: DateTime[]
      Matrix: Matrix<float> }
