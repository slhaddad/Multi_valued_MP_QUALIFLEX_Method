# Multi-valued_MP_QUALIFLEX_Method

**This program computes the multi-valued morphological profile using the QUALIFLEX vector ordering algorithm.**

## The method

QUALIFLEX evaluates every possible ranking (permutation) of the pixel-vectors of the neighborhood. The values of each band are first converted into ordinal ranks. For each permutation and each band, every pair of pixel-vectors receives a concordance/discordance index: +1 if the permutation agrees with the band ranking, 0 for a tie and -1 if it disagrees. These indices are summed per band and aggregated with the band weights into a global index.

The permutation with the highest global index is the optimal ranking: its **head** is the **supremum** (dilation) and its **tail** the **infimum** (erosion).

Since the number of permutations grows as n!, the disk of size i is obtained by i successive applications of the elementary disk of radius 1 (5 pixels, i.e. 120 permutations per neighborhood), by associativity of the Minkowski sum.

The supremum and infimum obtained in every neighborhood are used to compute, for each size of the structuring element: erosion, dilation, opening, closing, opening by reconstruction and closing by reconstruction (multivariate morphological profile).

## Implementation

- **Language:** C# (WPF desktop application with a graphical interface)
- **Framework:** .NET 10 (`net10.0-windows`)
- **Development environment:** Visual Studio Code with the *C# Dev Kit* extension
- **Operating system:** Windows 64-bit (WPF is Windows-only)
- **Structuring element:** disk; the disk of size i is obtained by i successive applications of the elementary disk of radius 1 (5 pixels). Border pixels whose neighborhood falls outside the image are kept unchanged.
- **Interface language:** French

## Repository content

```
EMP_QUALIFLEX_Q_Optimum_Local.sln              Visual Studio / VS Code solution
global.json          .NET SDK version (10.0.x)
.vscode/             VS Code build, run (F5) and publish tasks
EMP_QUALIFLEX_Q_Optimum_Local/
    MainWindow.xaml.cs   ordering algorithm and morphological operators
    MainWindow.xaml      graphical interface
    App.xaml(.cs)        application entry point
    EMP_QUALIFLEX_Q_Optimum_Local.csproj
```

## How to run

### Option 1 — Standalone executable (no installation)

Download `EMP_QUALIFLEX_Q_Optimum_Local.exe` from the **Releases** page of this repository, if available, and double-click it. It is self-contained: neither .NET nor VS Code is required. You can also build it yourself with Option 3.

### Option 2 — Visual Studio Code

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the **C# Dev Kit** extension for VS Code.
2. Clone or download this repository, then in VS Code: *File > Open Folder* and select the repository folder.
3. Press **F5** to build and run the program.

### Option 3 — Command line

```powershell
dotnet build "EMP_QUALIFLEX_Q_Optimum_Local\EMP_QUALIFLEX_Q_Optimum_Local.csproj"
dotnet run --project "EMP_QUALIFLEX_Q_Optimum_Local\EMP_QUALIFLEX_Q_Optimum_Local.csproj"

# Create the standalone executable in the Executable\ folder
dotnet publish "EMP_QUALIFLEX_Q_Optimum_Local\EMP_QUALIFLEX_Q_Optimum_Local.csproj" -c Release -o Executable
```

## Input parameters

| Field in the interface (French) | Meaning |
|---|---|
| *Donnez la taille maximale de l'ES : i =* | Maximum size of the disk-shaped structuring element. The program computes all sizes from 1 to i. |
| *Selection bande — Parcourir* | Select the image bands (one grayscale image per band, PNG/JPEG/BMP/TIFF, all of the same size). Multiple selection is allowed. |
| *Donnez le nombre d'itérations pour la stabilité* | Maximum number of iterations of the geodesic reconstruction (`0` = unlimited, run until stability). |
| *Donnez le pourcentage de ressemblance (0 - 100)* | Stability threshold: the reconstruction stops when two successive images are identical on at least this percentage of pixels (e.g. `35`). |
| *Donnez les poids des différentes bandes* | Band weights, separated by semicolons, no spaces (e.g. `0,45;0,35;0,20`). One weight per band. |

Then click **Executer**.

## Outputs

All results are written to the **`IMAGES-résultat`** folder, created next to the solution (`.sln`) file, or next to the executable if it is run alone:

- one TIFF image per band and per structuring-element size for each operator: `Erod`, `Dilat`, `OuvertureStandard`, `FermetureStandard`, `OuvertReconstruction`, `FermeReconstruction` (named `<operator> B_<band> ES_<size>.tiff`);
- three multiband images in **ENVI** format (`.img` + `.hdr`):
  - `OuvertReconstruction-multibande`: all openings by reconstruction;
  - `FermeReconstruction-multibande`: all closings by reconstruction;
  - `EXTENDED-PROFIL-MOR-multibande`: the extended morphological profile (openings and closings by reconstruction together).

## Multivariate Mathematical Morphology through Vector Ordering

The code published in this repository implements new algorithms that extend mathematical morphology operators to multivalued images, particularly multispectral satellite images. These approaches also apply to RGB color images.

This extension relies on vector-ordering strategies applied to the pixel-vectors within the neighborhood defined by a structuring element (SE). For each neighborhood, the ordering uniquely identifies the infimum and supremum pixel-vectors required by the fundamental erosion and dilation operations.

The proposed strategies are based on:

- The comparative structure of multi-criteria decision analysis methods: AHP, PROMETHEE (usual, U-shape, level and Gaussian preference functions), QUALIFLEX and TOPSIS (p = 1 and p = 2);
- Numeral-system-based methods;
- Outranking relations between pixel-vectors;
- Cumulative distances: SID (Spectral Information Divergence) and SAD (Spectral Angle Distance);
- The conventional lexicographic order.

Each program computes multivariate erosion, dilation, opening, closing, opening by reconstruction and closing by reconstruction, using a disk-shaped structuring element of increasing size. It also exports the extended morphological profile in ENVI format.

The methods are described in detail in the following works:

- Samir, L. H., Akila, K., & Aude Nuscia, T. (2025). New Vector Ordering Algorithms for Multivalued Mathematical Morphology Computing Based on Multicriteria Decision Making Systems: L'haddad et al. *Computational and Applied Mathematics*, 44(6), 320.
- L'haddad, S., Kemmouche, A., & Taïbi, A. N. (2024). Computing Multivalued Mathematical Morphology on Multiband Images Using Algorithms for Multicriteria Analysis. *Image Analysis and Stereology*, 43(1), 23-40.
- L'haddad, S., & Kemmouche, A. (2021, January). New Approach for Multi-valued Mathematical Morphology Computation. In *International Conference on Artificial Intelligence and its Applications* (pp. 514-523). Cham: Springer International Publishing.
- L'haddad, S., & Kemmouche, A. (2020, December). Vector Ordering Algorithms for Morphological Multi-Valued Operators Using Improved F-score Technique and Numeral Systems. In *2020 4th International Symposium on Informatics and its Applications (ISIA)* (pp. 1-6). IEEE.
- Plaza, A., Martínez, P., Plaza, J., & Pérez, R. (2005). Dimensionality reduction and classification of hyperspectral image data using sequences of extended morphological transformations. *IEEE Transactions on Geoscience and Remote Sensing*, 43(3), 466-479.

A more detailed description of these methods will be provided in my forthcoming PhD thesis (in French), to be published online and on my ResearchGate profile (Samir L'Haddad, USTHB, Algiers, Algeria).
