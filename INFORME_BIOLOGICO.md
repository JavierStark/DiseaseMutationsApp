## Informe de Lógica de Negocio para Biólogos: Proyecto gRNA

Este documento describe la implementación de varios conceptos biológicos y bioinformáticos en el proyecto `gRNA`. El objetivo es proporcionar una visión clara de cómo se traduce la ciencia en la lógica de la aplicación, sirviendo como puente entre la biología y el desarrollo de software.

### 1. Notación HGVS: El Lenguaje de las Variantes Genéticas

**Concepto Científico:**
La notación HGVS (Human Genome Variation Society) es el estándar de oro para describir variaciones en secuencias de ADN, ARN y proteínas. Permite una comunicación precisa y sin ambigüedades sobre mutaciones, como sustituciones, deleciones o inserciones. Por ejemplo, `NG_012345.1:c.76A>T` describe una sustitución en la posición 76 de la secuencia codificante de un gen, donde una Adenina (A) ha sido reemplazada por una Timina (T).

**Implementación en el Código:**
En `gRNA/HGVS.fs`, hemos implementado un parser que interpreta esta notación. El código descompone una cadena HGVS en sus componentes fundamentales: el número de acceso de la secuencia, el tipo de mutación, las posiciones afectadas y los nucleótidos implicados.

```fsharp
// En gRNA/HGVS.fs

type MutationType =
    | Substitution
    | Deletion
    | Insertion
    | Duplication
    // ... otros tipos

type HGVS(hgvs: string) =
    member this.Parse() =
        let parts = hgvs.Split(':')
        let accession = parts.[0]
        let mutation = parts.[1]
        // Lógica para extraer tipo, posición y cambio
        // ...
```

Este módulo es crucial para que la aplicación entienda la solicitud inicial del usuario, que es una variante genética específica.

### 2. SNP (Single Nucleotide Polymorphism): Variaciones Comunes

**Concepto Científico:**
Un SNP es una variación en una única base de nucleótido en el genoma. Son el tipo más común de variación genética entre las personas. A menudo se identifican con un número `rs` (Reference SNP cluster ID). La aplicación utiliza estos `rs` para encontrar las notaciones HGVS asociadas, conectando una variación común con su descripción genética formal.

**Implementación en el Código:**
El fichero `gRNA/SNP.fs` se comunica con la base de datos dbSNP del NCBI para traducir un `rs` ID a sus correspondientes notaciones HGVS. Esto permite al sistema trabajar con un formato estandarizado a partir de un identificador de SNP.

```fsharp
// En gRNA/SNP.fs

module SNP =
    let getHgvsNotationsAsync (rsNumber: string) =
        async {
            let url = $"https://api.ncbi.nlm.nih.gov/variation/v0/beta/refsnp/{rsNumber.Replace("rs", "")}"
            // ... lógica para hacer la petición web y parsear la respuesta JSON
            // Se extraen las notaciones HGVS del resultado.
        }
```

### 3. Manipulación de Secuencias de ADN/ARN

**Concepto Científico:**
El núcleo de la genómica es la manipulación de secuencias de ADN. Una vez que entendemos una mutación a través de HGVS, necesitamos aplicarla a una secuencia de referencia para obtener la secuencia mutada. Este proceso simula el efecto de la variante genética en el ADN.

**Implementación en el Código:**
`gRNA/Sequence.fs` contiene la lógica para aplicar estas mutaciones. La función `GetMutatedSubsequence` toma una secuencia de referencia y un objeto HGVS, y devuelve la secuencia alterada según la mutación descrita (sustitución, deleción, etc.), junto con un contexto de nucleótidos flanqueantes.

```fsharp
// En gRNA/Sequence.fs

module Sequence =
    let GetMutatedSubsequence (sequence: string) (hgvs: HGVS) (leftPadding: int) (rightPadding: int) =
        // ... lógica para aplicar diferentes tipos de mutaciones
        match hgvs.MutationType with
        | MutationType.Substitution ->
            // Reemplaza el nucleótido de referencia por el alternativo
        | MutationType.Deletion ->
            // Elimina los nucleótidos en las posiciones indicadas
        // ... etc.
```

### 4. Búsqueda y Optimización de Spacers de gRNA: El Corazón de CRISPR

**Concepto Científico:**
El diseño de un gRNA eficaz es el paso más crítico en un experimento CRISPR. El "spacer", esa secuencia de ~20 nucleótidos que guía a la proteína Cas13, debe cumplir varios criterios para asegurar una alta eficiencia de corte en el sitio deseado y minimizar los efectos fuera de objetivo (off-targets). Un diseño subóptimo puede llevar a una baja eficiencia de edición o a mutaciones no deseadas en otras partes del genoma.

Nuestra estrategia se centra en un análisis multifactorial para cada posible spacer, evaluando sus propiedades intrínsecas y su comportamiento previsto en el genoma.

**Implementación en el Código (`gRNA/SpacerFinder.fs`):**

El módulo `SpacerFinder` es el motor principal de la aplicación. Orquesta un proceso de varios pasos para identificar los mejores candidatos a gRNA a partir de una secuencia de ADN que contiene una mutación de interés.

#### 4.1. Generación de Candidatos: La Ventana Deslizante

El primer paso es generar todos los posibles spacers. Esto se logra mediante una técnica de "ventana deslizante" (`slidingWindow`) que recorre la secuencia de ADN mutada, extrayendo subsecuencias de la longitud definida para el gRNA (normalmente 20 pares de bases).

> **IMPORTANTE: Naturaleza de la Secuencia del Spacer Generado**
>
> Es fundamental entender la transformación que sufre la secuencia de ADN original para convertirse en un spacer de gRNA:
>
> 1.  **Hebra Original (ADN Sentido):** Se parte de una subsecuencia de la hebra de ADN que contiene la mutación (ej: `5'-AGCT...-3'`).
> 2.  **Hebra Complementaria (ADN Antisentido):** El código calcula la hebra complementaria a esta subsecuencia (ej: `3'-TCGA...-5'`).
> 3.  **Transcripción a ARN:** Finalmente, la hebra de ADN complementaria se convierte en ARN reemplazando la Timina (T) por Uracilo (U).
>
> **La secuencia final devuelta por la aplicación es una secuencia de ARN (con 'U') que es complementaria a la hebra de ADN diana original.** Esto es así para que el gRNA pueda hibridar (unirse) correctamente a la secuencia de ADN que se desea cortar.

```fsharp
// En gRNA/SpacerFinder.fs

let slidingWindow (input: string) (windowSize: int) =
    [ for i in 0 .. input.Length - windowSize -> input.Substring(i, windowSize) ]
```
Por cada posición en la secuencia, se genera un candidato a spacer. Este conjunto exhaustivo de candidatos es la materia prima para el proceso de filtrado y puntuación.

#### 4.2. Puntuación y Filtrado: Un Enfoque Multifactorial

Cada spacer candidato se somete a una serie de evaluaciones bioinformáticas. Los resultados de estas evaluaciones se almacenan en un registro `gRNAResult`, que encapsula toda la información relevante para un candidato.

```fsharp
// En gRNA/SpacerFinder.fs

type gRNAResult =
    { Sequence: string              // La secuencia del spacer (en formato ARN, con U en lugar de T)
      GCScore: float               // Puntuación basada en el contenido de GC
      HomopolymerCount: int        // Número de secuencias de homopolímeros (ej. AAAA)
      Allignments: int             // Número de alineamientos en el genoma (off-targets)
      RnaFoldResult: RNAFoldResult // Resultado del plegamiento del ARN (estructura y energía)
      Score: float                 // Puntuación final combinada (de 0 a 1)
      // ... otros campos para visualización
    }
```

Los criterios de evaluación clave son:

**a) Contenido de Guanina-Citosina (GC):**
*   **Concepto:** La estabilidad del dúplex gRNA-ADN diana está influenciada por el contenido de GC. Un contenido demasiado bajo puede resultar en una unión inestable, mientras que uno demasiado alto puede dificultar la disociación de Cas13 después del corte. El rango óptimo generalmente aceptado es **40-60%**.
*   **Implementación:** La función `calculateGCScore` asigna una puntuación de 1.0 si el contenido de GC está dentro del rango ideal. Fuera de este rango, la puntuación se reduce proporcionalmente a la distancia del ideal.

```fsharp
// En gRNA/SpacerFinder.fs

let calculateGCScore (gcContent: float) (lowerThreshold: float, upperThreshold: float) =
    if gcContent < upperThreshold && gcContent > lowerThreshold then
        1.0
    else if gcContent < lowerThreshold then
        gcContent / lowerThreshold
    else
        (100.0 - gcContent) / (100.0 - upperThreshold)
```

**b) Presencia de Homopolímeros:**
*   **Concepto:** Tramos de cuatro o más nucleótidos idénticos (ej., `AAAA` o `GGGG`) pueden causar la terminación prematura de la transcripción del gRNA por la polimerasa III y se ha demostrado que reducen la eficiencia de CRISPR.
*   **Implementación:** La función `countHomopolymers` utiliza una expresión regular para contar la aparición de estos tramos problemáticos. Un número más bajo es mejor.

**c) Análisis de Off-Targets con Bowtie:**
*   **Concepto:** Este es, posiblemente, el criterio más importante para la seguridad. Se utiliza la herramienta de alineamiento `Bowtie` para buscar en todo el genoma humano secuencias idénticas o muy similares al spacer candidato. Cada coincidencia adicional (aparte del objetivo deseado) es un "off-target" potencial.
*   **Implementación:** Se invoca a `Bowtie` permitiendo hasta 2 "mismatches" (desajustes). Un número de alineamientos (`Allignments`) de 1 es ideal, lo que indica que el spacer es único. Un número mayor penaliza severamente al candidato.

**d) Estructura Secundaria del gRNA:**
*   **Concepto:** El gRNA completo (spacer + andamio de ARN) debe adoptar una estructura funcional para unirse a Cas13. Si el propio spacer se pliega en una estructura secundaria estable (horquillas, bucles), puede interferir con su función. Se utiliza `RNAFold` para predecir la estructura y su energía libre mínima (MFE).
*   **Implementación:** Un valor de energía (`RnaFoldResult.Energy`) más cercano a cero (menos negativo) es preferible, ya que indica una estructura menos estable y, por lo tanto, más accesible.

#### 4.3. Ordenación y Ranking Final

Una vez que todos los candidatos han sido evaluados, necesitan ser ordenados para presentar al usuario los más prometedores. La ordenación es un proceso lexicográfico que prioriza los factores más importantes.

*   **Implementación:** La función `sortByResult` define el criterio de ordenación. Los gRNA se ordenan de mejor a peor según la siguiente tupla de valores:

```fsharp
// En gRNA/SpacerFinder.fs

let sortByResult (result: gRNAResult) =
    (result.Allignments, -result.RnaFoldResult.Energy, -result.GCScore, result.HomopolymerCount)
```

Esto se traduce en el siguiente orden de prioridad:
1.  **Menor número de `Allignments` (Off-targets):** La especificidad es lo primero. Un gRNA con 1 alineamiento siempre será mejor que uno con 2, sin importar los otros factores.
2.  **Mayor `Energy` (Menos negativo):** Entre gRNAs con el mismo número de off-targets, se prefiere el que tenga la estructura secundaria menos estable (energía más cercana a 0). Se usa el negativo de la energía para que una ordenación ascendente coloque los valores menos negativos primero.
3.  **Mayor `GCScore`:** A igualdad de los criterios anteriores, se prefiere el que tenga un contenido de GC más cercano al ideal.
4.  **Menor `HomopolymerCount`:** Finalmente, se penaliza la presencia de homopolímeros.

Después de esta ordenación, se asigna una puntuación final (`Score`) normalizada de 0 a 1 a cada gRNA, donde el mejor candidato recibe un 1.0. Esto proporciona una métrica cuantitativa simple para que el usuario compare rápidamente la calidad relativa de los diferentes gRNA.

Este riguroso proceso de selección asegura que los gRNA recomendados por la aplicación no solo sean capaces de dirigirse a la mutación de interés, sino que lo hagan con la máxima eficiencia y seguridad posible.

#### 4.4. Regla Especial para SNPs de Sustitución

**Concepto:**
La eficiencia de la edición por CRISPR puede ser mejorada si la propia mutación que se quiere introducir (o corregir) ayuda a evitar que la maquinaria CRISPR vuelva a cortar el ADN una vez que ha sido reparado. Esto es especialmente relevante para los SNPs de sustitución. Si el gRNA se diseña de tal manera que uno de sus nucleótidos clave se solapa con la posición de la mutación, se puede crear un "desajuste" (mismatch) con la secuencia original (wild-type) y una coincidencia perfecta con la secuencia reparada (mutada).

Una estrategia conocida es forzar un desajuste en la "seed region" del gRNA. Sin embargo, una técnica más sutil y a veces preferida es introducir un desajuste en una posición menos crítica, como la cuarta base desde el extremo 3' del spacer. Si la mutación SNP de sustitución cae precisamente en la posición `N-4` (donde N es la longitud del spacer), podemos alterar deliberadamente el nucleótido del gRNA en esa posición.

**Implementación:**
El sistema implementa una regla especial para capitalizar esta estrategia.

1.  **Detección:** El sistema primero identifica si existe un candidato a spacer donde la mutación de sustitución de un solo nucleótido se encuentra exactamente en la posición 17 (para un spacer de 20 nucleótidos), que corresponde a la cuarta posición desde el extremo 3' (`longitud - 3`, en base 0).
2.  **Modificación:** Si se encuentra dicho spacer, se crea una versión modificada del mismo. La función `adjustFourthFromEndToAorU` altera el nucleótido en esa posición, cambiándolo a 'A' o 'U' (lo que no fuera originalmente).
3.  **Priorización:** Este spacer modificado se considera un candidato de alta prioridad. Se le asigna el `Rank` más alto (1) y una puntuación (`Score`) de 1.0, colocándolo al principio de la lista de resultados. La lógica asume que la ventaja de introducir este desajuste supera los otros criterios de puntuación (GC, homopolímeros, etc.) para este candidato específico.

```fsharp
// En gRNA/SpacerFinder.fs

let adjustFourthFromEndToAorU (sequence: string) =
    // ...
    let targetIndex = sequence.Length - 4
    let replacement = if sequence.[targetIndex] = 'A' then 'U' else 'A'
    // ...

let applySubstitutionSpecialRule (windowSize: int) (results: gRNAResult list) =
    let targetMutationIndex = windowSize - 3 // Posición 17 para un spacer de 20
    results
    |> List.tryFind (fun result -> result.MutationHighlightStart = targetMutationIndex)
    |> Option.map (fun selected ->
        let adjustedSequence = adjustFourthFromEndToAorU selected.Sequence
        // ... se crea un nuevo gRNAResult con Rank = 1 y Score = 1.0
    )
```

Esta regla automática ofrece al investigador un gRNA potencialmente más eficaz para la edición de SNPs de sustitución, aprovechando la propia mutación para mejorar la especificidad del proceso.

### 5. Alineamiento con Bowtie: Análisis de Off-Targets

**Concepto Científico:**
Para que la terapia CRISPR sea segura, el gRNA debe ser altamente específico para la secuencia diana. El análisis de "off-targets" consiste en buscar en todo el genoma secuencias similares al spacer que podrían ser cortadas por error. Bowtie es una herramienta bioinformática ultrarrápida para alinear secuencias cortas contra un genoma de referencia.

**Implementación en el Código:**
`gRNA/BowtieWrapper.fs` es una interfaz para ejecutar Bowtie. Envía cada spacer candidato a Bowtie y cuenta el número de alineamientos encontrados en el genoma humano. Un número bajo de alineamientos (idealmente 1) indica alta especificidad.

```fsharp
// En gRNA/BowtieWrapper.fs

module BowtieWrapper =
    let runBowtie (index: string) (sequences: string list) (mismatches: int) =
        // Construye y ejecuta el comando de Bowtie en la línea de comandos
        // Ejemplo: bowtie2 -x <genoma_index> -c <secuencia> -v <mismatches>
        // Parsea la salida para contar los alineamientos por secuencia
```

### 6. Predicción de Estructura Secundaria con RNAFold

**Concepto Científico:**
La molécula de gRNA puede plegarse sobre sí misma, formando estructuras secundarias. Una estructura muy estable puede impedir que el gRNA se una correctamente a la proteína Cas13 o a la secuencia de ADN diana, reduciendo su eficacia. RNAFold (del paquete ViennaRNA) es un programa que predice la estructura secundaria más probable de una molécula de ARN y su energía libre mínima (MFE). Un MFE muy bajo (muy negativo) indica una estructura muy estable y, por tanto, un gRNA potencialmente menos eficaz.

**Implementación en el Código:**
`gRNA/RNAFoldWrapper.fs` interactúa con RNAFold. Para cada spacer, obtiene la estructura predicha en formato de puntos y paréntesis y su valor de energía. Este valor se utiliza como uno de los factores para puntuar y clasificar los gRNA candidatos.

```fsharp
// En gRNA/RNAFoldWrapper.fs

module RNAFoldWrapper =
    type RNAFoldResult = { Structure: string; Energy: float }

    let fold (sequence: string) =
        // Llama a un script de Python que usa la librería ViennaRNA
        // para calcular la estructura y energía de la secuencia de ARN.
        // Parsea el resultado para obtener la estructura y la energía.
```

### 7. Distancia de Levenshtein: Medición de Similitud

**Concepto Científico:**
La distancia de Levenshtein es una métrica que mide la diferencia entre dos cadenas de texto. Se define como el número mínimo de ediciones de un solo carácter (inserciones, eliminaciones o sustituciones) necesarias para cambiar una cadena por la otra. En nuestro contexto, se utiliza para comparar nombres de fenotipos de enfermedades y agrupar variantes alélicas relevantes.

**Implementación en el Código:**
El módulo `gRNA/LevenshteinDistance.fs` contiene una implementación de este algoritmo. Ayuda a determinar si una variante alélica encontrada en la base de datos OMIM está relacionada con el fenotipo de la enfermedad que se está investigando.

```fsharp
// En gRNA/LevenshteinDistance.fs

module Levenshtein =
    let levenshteinDistance (s1: string) (s2: string) =
        // Implementación del algoritmo de programación dinámica
        // para calcular la distancia de edición.
```

### 8. Mapeo de Enfermedades con OMIM

**Concepto Científico:**
OMIM (Online Mendelian Inheritance in Man) es una base de datos exhaustiva de genes humanos y fenotipos genéticos. La aplicación la utiliza para encontrar variantes genéticas (SNPs con `rs` ID) que están asociadas a una enfermedad específica, identificada por su número MIM.

**Implementación en el Código:**
`gRNA/Omim.fs` realiza web scraping en el sitio de OMIM. A partir de un número MIM, navega a través de las tablas de "Phenotype-Gene Relationships" y "Allelic Variants" para extraer los `rs` ID relevantes, utilizando la distancia de Levenshtein para filtrar por fenotipos similares.

```fsharp
// En gRNA/Omim.fs

module Omim =
    let rsFromOmim (mimNumber: string) (diseaseName: string) =
        async {
            // 1. Obtiene la página de OMIM para el número MIM.
            // 2. Extrae los genes asociados al fenotipo.
            // 3. Para cada gen, visita la tabla de variantes alélicas.
            // 4. Filtra las variantes por similitud de fenotipo usando Levenshtein.
            // 5. Extrae y devuelve los rs IDs.
        }
```

### Flujo de Trabajo Principal

El fichero `gRNA/Main.fs` orquesta todo el proceso, conectando los módulos anteriores en un flujo de trabajo cohesivo:

1.  **Entrada:** Recibe una notación HGVS.
2.  **Parseo:** Usa `HGVS.fs` para interpretar la mutación.
3.  **Secuencia:** Usa `SequenceRepository.fs` para obtener la secuencia de referencia del NCBI.
4.  **Mutación:** Usa `Sequence.fs` para crear la secuencia mutada.
5.  **Búsqueda de gRNA:** Usa `SpacerFinder.fs` para generar y puntuar todos los gRNA candidatos, coordinando las llamadas a `BowtieWrapper.fs` y `RNAFoldWrapper.fs`.
6.  **Salida:** Devuelve una lista ordenada de los mejores gRNA, junto con toda la información relevante (secuencias, puntuaciones, etc.).

Este enfoque modular y basado en principios científicos sólidos permite a la aplicación diseñar gRNAs eficientes y específicos para la edición de genes asociados a enfermedades.
