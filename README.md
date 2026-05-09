# Analise_Carteira

Projeto em **F# / .NET 9**: biblioteca de funções (`Functions`) e aplicação de consola (`carteiraSimulada`) para **carteira ótima no sentido de Markowitz**, métricas de risco–retorno e extensões que costumam distinguir trabalhos **nível A / A+** (restrições realistas, validação fora da amostra e visualização).

---

## O que o código faz (resumo)

| Ideia | Onde está | Descrição breve |
|--------|-----------|------------------|
| Dados | `Returns.fs` | Lê CSV (tickers na 1.ª linha; cada linha seguinte = retornos num período). |
| **Yahoo Finance** | `YahooFinance.fs` | Modo `--yahoo`: descarrega **mensalmente** preços **ajustados** (`adjclose`) dos **mesmos 30 tickers** via API não oficial `v8/finance/chart`, alinha meses comuns e calcula retornos simples. |
| Momentos | `Returns.fs` | Vetor de médias amostrais \(\hat{\boldsymbol{\mu}}\) e matriz de covariância **amostral** \(\hat{\boldsymbol{\Sigma}}\) com divisor \(T-1\). |
| **Short sales** | `Optimization.fs` | Carteira de variância global mínima (**GMVP**), carteira **tangente** (máximo Sharpe analítico com \(\mathbf{w}^\top \mathbf{1}=1\)) e pontos da **fronteira eficiente** com dois constrangimentos de igualdade (solução fechada via sistema \(2\times 2\)). |
| **Long-only** | `Simplex.fs`, `LongOnly.fs` | Projecção no **simplex** \(\{\mathbf{w}\ge \mathbf{0},\ \mathbf{1}^\top\mathbf{w}=1\}\); **GMVP long-only** por **Frank–Wolfe**; **aproximação** ao máximo Sharpe long-only por **subida de gradiente projetada** (estacionário local — não garante ótimo global). |
| Métricas | `Metrics.fs` | Sharpe mensal; anualização **simples** dos momentos (\(\times 12\) para média; \(\times 12\) na covariância; vol carteira com \(\sqrt{\mathbf{w}^\top \boldsymbol{\Sigma}_{\mathrm{anual}}\mathbf{w}}\) quando derivado a partir da Σ mensal). |
| Validação | `Backtest.fs` | Corta a série em **treino** / **teste**, estima \(\hat{\boldsymbol{\mu}},\hat{\boldsymbol{\Sigma}}\) só no treino, **congela** os pesos e avalia retorno realizado no teste. |
| **Calendário** | `Types.fs` (`DatedAssetReturns`), `DateSeries.fs` | **CSV:** datas **sintéticas** (última linha forçada a março 2025). **`--yahoo`:** datas **reais** (primeiro dia de cada mês da série Yahoo). Permite isolar **Q1 2025** no índice calendário. |
| **Escolha da “melhor” carteira** | `PortfolioSelection.fs` | Entre várias candidatas, escolhe a de maior Sharpe na **amostra de estimação**. |
| **Q1 2025** | `Program.fs` + funções acima | Estima pesos **excluindo** observações de Q1 2025; aplica ao trimestre e imprime retornos mensais, retorno acumulado e Sharpe na janela (nota: só **3** meses). |
| **Paralelismo** | `FrontierSweep.fs`, `Benchmark.fs` | Mesmo modelo da fronteira: laço **sequencial** vs `Array.Parallel` (**≥ 8 corridas** cada, **2** aquecimentos); médias e desvios em ms e speed-up. |
| Relatórios | `Reporting.fs`, `FrontierHtml.fs` | Exporta **`fronteira_pontos.csv`** (`;`, cultura invariante) e **`fronteira_eficiente.html`** (gráfico Plotly.js por CDN). |

---

## Matemática (Markowitz, formulário curto)

**Retornos mensais** por ativo em colunas; \(\hat{\boldsymbol{\mu}}\in\mathbb{R}^N\), \(\hat{\boldsymbol{\Sigma}}\in\mathbb{R}^{N\times N}\) positiva definida (idealmente).

- **Retorno e vol da carteira** (mensal):  
  \(E[r_p]=\mathbf{w}^\top\hat{\boldsymbol{\mu}}\), \(\sigma_p^2=\mathbf{w}^\top\hat{\boldsymbol{\Sigma}}\mathbf{w}\).

- **Sharpe mensal** (taxa \(r_f\) por período):  
  \(\displaystyle \frac{\mathbf{w}^\top\hat{\boldsymbol{\mu}}-r_f}{\sigma_p}\).

- **GMVP (short permitido, \(\mathbf{1}^\top\mathbf{w}=1\))**:  
  \(\mathbf{w}_{\mathrm{GMVP}} \propto \hat{\boldsymbol{\Sigma}}^{-1}\mathbf{1}\), normalizado para somar 1.

- **Carteira tangente** (mesmo normalização):  
  \(\mathbf{w}_{\tan} \propto \hat{\boldsymbol{\Sigma}}^{-1}(\hat{\boldsymbol{\mu}}-r_f\mathbf{1})\).

- **Fronteira com alvo** \(E[r_p]=m\) (short permitido): minimizar \(\mathbf{w}^\top\hat{\boldsymbol{\Sigma}}\mathbf{w}\) com \(\mathbf{1}^\top\mathbf{w}=1\) e \(\hat{\boldsymbol{\mu}}^\top\mathbf{w}=m\) — implementação por **KKT** e sistema \(2\times 2\) nos multiplicadores (ver código em `Optimization.fs`).

**Pressupostos típicos (limitações honestas):** estabilidade no tempo, ausência de custos de transação, \(\hat{\boldsymbol{\Sigma}}\) bem condicionada; estimação **dentro da amostra** tende a **superestimar** Sharpe real; treino/teste ajuda mas um único corte temporal não substitui validação mais robusta.

---

## Algoritmos “extra” (long-only)

1. **Frank–Wolfe** na GMVP com domínio simplex: em cada iteração lineariza-se o gradiente \(2\hat{\boldsymbol{\Sigma}}\mathbf{w}\), escolhe-se vértice \(\mathbf{e}_j\) minimizador ao longo do gradiente e faz-se **pesquisa linear exata** em \(\gamma\in[0,1]\) ao longo do segmento (função quadrática).

2. **Gradiente projetado** para Sharpe long-only: usa-se um gradiente analítico de \(\displaystyle S(\mathbf{w})=\frac{\mathbf{w}^\top\hat{\boldsymbol{\mu}}-r_f}{\sqrt{\mathbf{w}^\top\hat{\boldsymbol{\Sigma}}\mathbf{w}}}\) e projecção Euclidiana no simplex após cada passo; passo adapta-se heuristicamente. O problema **não é convexo** em \(\mathbf{w}\): interpretar como **heurística forte**, não como solver global certificado.

---

## Estrutura de pastas

```
Functions/          # Biblioteca (tipos, dados, optimização, backtest, CSV)
carteiraSimulada/   # Consola + gerador HTML da fronteira
data/               # dow30_returns.csv (exemplo)
AnaliseCarteira.sln
```

---

## Como executar

Na raiz do repositório:

```bash
dotnet build AnaliseCarteira.sln
dotnet run --project carteiraSimulada/carteiraSimulada.fsproj
```

**Dados em tempo real (Yahoo Finance, exige rede):**

```bash
dotnet run --project carteiraSimulada/carteiraSimulada.fsproj -- --yahoo
```

Outro ficheiro CSV local:

```bash
dotnet run --project carteiraSimulada/carteiraSimulada.fsproj -- /caminho/dados.csv
```

Sem **`--yahoo`**, usa-se o CSV local (predefinido `data/dow30_returns.csv` ou caminho que passes como argumento). Com **`--yahoo`**, o programa ignora o ficheiro e só usa dados obtidos na rede.

### Saídas geradas (pasta do executável, ex.: `carteiraSimulada/bin/Debug/net9.0/`)

- **`fronteira_pontos.csv`** — colunas `sigma_mensal;retorno_mensal;rotulo` (fronteira + pontos de referência).
- **`fronteira_eficiente.html`** — abrir no browser (requer rede na **primeira** vez para carregar o CDN do Plotly).

---

## Parâmetros fixos no código

- Taxa livre de risco anual **2%** convertida para mensal por \(r_{f,\mathrm{m}}=r_{f,\mathrm{a}}/12\) (`Metrics.monthlyFromAnnualRateSimple`).
- Fração de treino na validação temporal **≈72%** (`Program.fs`); só corre teste se houver **≥6** observações de teste.
- **CSV — âncora temporal para Q1 2025:** `DateTime(2025, 3, 1)` — última linha do CSV tratada como **março de 2025** (`Program.fs`). **`--yahoo`:** sem âncora artificial; Q1 2025 usa **datas reais** dos meses jan–mar de 2025 devolvidos pela API.
- **Início histórico Yahoo:** ano **2015** em `downloadDow30Monthly 2015` (`Program.fs`); altera se precisares de mais histórico.
- **Benchmark:** **500** alvos na fronteira, **8** corridas cronometradas por modo (sequencial / paralelo), **2** corridas de aquecimento (`Program.fs` + `Benchmark.fs`).

Altere estes valores diretamente em `Program.fs` se o enunciado pedir outros números.

---

## Referências úteis (para relatório)

- Markowitz, H. (1952). *Portfolio Selection*. Journal of Finance.  
- Frank, M. & Wolfe, P. (1956). *An algorithm for quadratic programming*. Naval Research Logistics Quarterly.  
- Wang, Y. et al. *Projection onto the probability simplex* (algoritmos standard para projecção Euclidiana).

---

## Rúbrica do enunciado (o que está feito / o que falta)

Tabela de correspondência com os critérios que descreveste. **Quem classifica é sempre o docente**; isto serve para entrega e discussão oral.

### Critérios base

| Critério | Significado (resumido) | Este projeto |
|----------|-------------------------|--------------|
| **I** | Projeto incompleto | **Não aplicável como “incompleto”**: há solução compilável, biblioteca + consola, dados, otimização Markowitz, extensões long-only, treino/teste, CSV e HTML. Se o PDF da UC pedir **obrigatoriamente** integrações abaixo (API, Q1 2025, paralelismo), o docente pode ainda considerar lacunas face ao **seu** enunciado — não face a esta lista genérica. |
| **D** | Não paralelizado **ou** não apresenta elementos funcionais | **Elementos funcionais: sim** (F#). **Paralelismo demonstrável:** geração de pontos da fronteira com `FrontierSweep.volatileSumParallel` (**`Array.Parallel.init`** por detrás). **Benchmark sequencial vs paralelo** na consola (8 corridas por modo). A interpretação final do critério **D** continua a cargo do docente. |
| **C+** | Linguagem multi-paradigma (Python, Java, PHP, …) | **Não é este caso**: o trabalho não está nessas linguagens. |
| **B+** | Linguagem funcional (Haskell, OCaml, **F#**, Scala, …) | **Cumprido**: implementação em **F#** (.NET 9), com organização em biblioteca e programa. |

**Síntese para oral:** **B+** (F#) + **elementos funcionais** + **paralelismo com medição de tempo** + **API Yahoo (`--yahoo`)** [endpoint não oficial — pode mudar ou bloquear].

### Opcionais (+½ conceito cada, segundo disseste)

| # | Requisito | Estado neste repositório |
|---|-----------|---------------------------|
| 1 | Obter dados **sob demanda** via **API** | **Feito** com **`--yahoo`**: pedidos HTTP ao Yahoo chart (`query1.finance.yahoo.com/v8/finance/chart/...`). **Aviso:** API não documentada oficialmente; pode exigir rede estável e ocasionalmente bloquear pedidos. Sem `--yahoo`, mantém-se o CSV em `data/dow30_returns.csv`. |
| 2 | Testar a **melhor carteira** no **1.º trimestre de 2025** e **apresentar resultados** | **Feito.** Pesos escolhidos por maior Sharpe na **estimação sem Q1**; avaliação nos **3 meses** de 2025 (jan–mar) conforme datas da série. **CSV:** última linha forçada como março 2025 (convénio). **`--yahoo`:** datas reais (calendário Yahoo). Sharpe no trimestre continua **frágil** (só 3 observações). |
| 3 | Comparação de **tempo de execução** **com vs sem paralelismo**, **≥ 5 execuções** cada | **Feito.** `FrontierSweep` + `Benchmark.timedRuns`: **8 corridas** por modo (**> 5**), **2** corridas de aquecimento; imprime média (ms), desvio amostral, speed-up e checksum da mesma função objetivo (soma estável dos pontos da fronteira). |

### Mapa rápido “feito / não feito”

- Base **B+** (linguagem funcional F#): **feito**  
- Projeto **funcional** no sentido de paradigma: **feito**  
- Paralelismo + **comparação de tempos** (≥ 5 corridas cada modo): **feito** (8 corridas)  
- Dados via **API** (Yahoo, modo `--yahoo`): **feito**  
- Validação específica **Q1 2025** com impressão de resultados: **feito** (datas sintéticas no CSV **ou** reais com Yahoo)  
- Opcionais (3× +½): **3 de 3** no código-base deste repo (sujeito sempre ao critério do docente)  

---

## Relação com “nota A+”

Critérios de avaliação variam; em termos de **substância técnica**, este repositório cobre além do núcleo Markowitz analítico: **long-only**, **validação treino/teste**, **Q1 2025**, **benchmark sequencial vs paralelo**, **dados Yahoo sob demanda (`--yahoo`)**, **exportação reprodutível** e **visualização**. O **relatório escrito** continua essencial (interpretação económica, figuras, limitações da API Yahoo não oficial e do Sharpe com só 3 meses no trimestre).
