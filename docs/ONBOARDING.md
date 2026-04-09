# GroupPoint_XY — Developer Onboarding Guide

## Project Overview

**GroupPoint_XY** is a C# Grasshopper plugin for Rhino 3D, built on **.NET Framework 4.8**. It ships a collection of standalone Grasshopper components covering five capability areas: point/geometry sorting & filtering, boundary and cross-section detection, BREP/surface analysis, coordinate system transforms, and Tekla Structures drawing automation.

| | |
|---|---|
| **Language** | C# (.NET 4.8) |
| **Plugin format** | `.gha` (Grasshopper Assembly) |
| **Primary frameworks** | Grasshopper SDK, RhinoCommon, Tekla Structures API |
| **Components** | 18 GH components + 1 assembly info class |

> **File naming convention:** Most files carry a date prefix (`YYMMDD_`) and a revision suffix (`_R1`, `_R2`, `_R3`). This encodes iteration history directly in the filename — e.g. `260214_Dimension_R2.cs` is the February 14 2026 second revision of the dimension component. When two revisions coexist (R2/R3), the higher revision is the current one.

---

## Architecture Layers

The plugin is architecturally flat — every file defines one independent `GH_Component` subclass. There are no cross-file imports. Layers are organised by functional purpose, not dependency.

### Plugin Infrastructure
*Assembly identity and the namesake component.*

Defines the plugin's GUID, author metadata, and the core point-grouping component the plugin is named after. Start here.

| File | Role |
|---|---|
| `GroupPoint_XYInfo.cs` | Plugin registration — GUID, name, author, version |
| `GroupPoint_XYComponent.cs` | Core component: filters point lists into min/max subsets by axis |

### Geometry Sorting & Filtering
*Sort or classify points, curves, and geometry objects by axis or vector direction.*

The largest layer (6 components). Ranges from the simplest line classifier to centroid-based sorting of mixed geometry trees.

| File | What it does |
|---|---|
| `SortLineByAxis.cs` | Classifies lines as X/Y/Z/diagonal using squared dot products |
| `ExtreamByAxis.cs` | Returns min/max curves or points along a named world axis |
| `ExplodeCurveandPoints.cs` | Extracts extreme curves and unique endpoints along a reference vector |
| `251225_SortedPointList_R2.cs` | Sorts point lists by dot-product projection onto a vector |
| `260131_Sort_XYZ.cs` | Sorts any geometry by centroid coordinate along X/Y/Z |
| `260214_SortCurves_XYZ_R1.cs` | Sorts curve trees by length or midpoint axis coordinate |

### Boundary & Slice Detection
*Slice 3D geometry and classify the resulting curves as outer boundaries or internal voids.*

| File | What it does |
|---|---|
| `251225_IdentifyBoundary_R3.cs` | Plane-slice at height; separates outer curves from voids |
| `Get Boundary Fast.cs` | Mesh-shadow projection; faster but less exact than slicing |
| `260103_ConcaveDefinition_R2.cs` | Adds concave corner detection via signed-area winding test |
| `260115_BREPBoundary.cs` | Multi-plane slicing; extracts boundaries, holes, and flat surfaces |

### BREP & Surface Analysis
*Decompose and classify Brep geometry at the face level.*

| File | What it does |
|---|---|
| `251222_IdentifySurfaces_R1.cs` | Classifies Brep faces as horizontal/vertical/sloped by normal |
| `ExplodeBrep.cs` | Explodes Brep into faces with local frames and bounding geometry |

### View & Coordinate Transforms
*Remap geometry between Tekla view space and Rhino world space.*

| File | What it does |
|---|---|
| `251221_ViewToWorldXY_R1.cs` | Tekla view CS → Rhino world XY plane transform |
| `251225_FrontViewToAnyView_R2.cs` | Any source plane → any target design plane |

### Tekla Structures Integration
*Automate linear dimension creation/modification in Tekla drawings.*

| File | What it does |
|---|---|
| `260214_Dimension_R2.cs` | Creates/modifies Tekla linear dimensions (paper-space offset) |
| `260214_Dimension_R3.cs` | Same, with projection-based distance calculation (current) |

### Utility

| File | What it does |
|---|---|
| `260125_Mid Station_R1.cs` | Pass-through relay — re-outputs input unchanged for wire routing |

---

## Key Concepts

### Every component follows the same structure

```
RegisterInputParams(GH_InputParamManager pManager)   → define inputs
RegisterOutputParams(GH_OutputParamManager pManager)  → define outputs
SolveInstance(IGH_DataAccess DA)                      → main compute
[private helper methods]                               → algorithm details
```

All business logic lives in `SolveInstance` and private helpers. To understand a component, read its `SolveInstance` first.

### The axis-projection idiom

The plugin's sorting and filtering components all share a common pattern:
1. Project geometry (or its centroid/endpoint) onto an axis or vector via dot product
2. Find min/max threshold values
3. Bucket or rank by that projected value

This pattern appears in `GroupPoint_XYComponent`, all six sorting components, and several boundary helpers.

### The explode-then-filter pattern

Boundary and curve-extreme components break geometry into line segments first (`ExplodeCurvesToLines`), then apply a filter (perpendicularity test, axis threshold, or containment check). Understanding this decompose-first strategy makes the boundary detection logic much easier to follow.

### Plane-to-plane transforms

The view-transform components construct a `Transform.PlaneToPlane(sourcePlane, targetPlane)` and apply it to all geometry. This is RhinoCommon's standard remapping mechanism — the same call appears in both transform components and is the key concept to know before reading them.

### Revision versioning (R1, R2, R3)

When you see multiple revisions of a file in the same directory, the highest revision is the current implementation. Lower revisions are retained as history. For the Dimension components, R2 uses fixed paper-space offsets while R3 replaces this with projection-based distance — R3 is preferred.

---

## Guided Tour

A recommended reading order that builds understanding incrementally:

| Step | Title | Files |
|---|---|---|
| 1 | Plugin Overview: Identity and Registration | `GroupPoint_XYInfo.cs`, `GroupPoint_XYComponent.cs` |
| 2 | The Core Grouping Algorithm | `GroupPoint_XYComponent.cs` (deep read) |
| 3 | Line Classification: The Simplest Sorter | `SortLineByAxis.cs` |
| 4 | Sorting Family: Extreme Values & Curve Decomposition | `ExtreamByAxis.cs`, `ExplodeCurveandPoints.cs` |
| 5 | Advanced Geometry Sorting | `251225_SortedPointList_R2.cs`, `260131_Sort_XYZ.cs`, `260214_SortCurves_XYZ_R1.cs` |
| 6 | Boundary Detection: Slicing and Classifying | `251225_IdentifyBoundary_R3.cs`, `Get Boundary Fast.cs` |
| 7 | Advanced Boundary: Concave & Multi-Layer | `260103_ConcaveDefinition_R2.cs`, `260115_BREPBoundary.cs` |
| 8 | BREP and Surface Analysis | `251222_IdentifySurfaces_R1.cs`, `ExplodeBrep.cs` |
| 9 | Coordinate System Transforms | `251221_ViewToWorldXY_R1.cs`, `251225_FrontViewToAnyView_R2.cs` |
| 10 | Tekla Integration & Utility | `260214_Dimension_R2.cs`, `260214_Dimension_R3.cs`, `260125_Mid Station_R1.cs` |

---

## Complexity Hotspots

These files have the highest internal complexity and warrant careful reading before modification:

| File | Lines | Why it's complex |
|---|---|---|
| `260115_BREPBoundary.cs` | ~1361 | Multi-plane slicing, mesh unification, surface hierarchy detection, hole deduplication — the most algorithmically dense file |
| `260214_Dimension_R3.cs` | ~954 | Full Tekla dimension lifecycle (create/modify/delete/validate) plus stateful canvas highlighting |
| `260214_Dimension_R2.cs` | ~946 | Same as R3 but with paper-space offset logic — read R3 instead unless debugging R2 specifically |
| `Get Boundary Fast.cs` | ~806 | Mesh meshing pipeline, shadow projection, boolean union, plus containment-based classification |
| `260103_ConcaveDefinition_R2.cs` | ~799 | Boundary slicing + concave vertex detection using shoelace signed-area + winding order correction |
| `260131_Sort_XYZ.cs` | ~774 | Centroid computation for 8+ geometry types (Brep, Mesh, Curve, Point, Surface...) |
| `251225_IdentifyBoundary_R3.cs` | ~741 | Slice pipeline, outer/void separation, custom preview override with `AfterSolveInstance` |

**Simple starting points** (under 50 lines, safe to read first):
- `GroupPoint_XYInfo.cs` — 28 lines, assembly metadata only
- `260125_Mid Station_R1.cs` — 49 lines, pass-through relay

---

*Generated by [Claude Code](https://claude.ai/claude-code) on 2026-03-26 from the project knowledge graph at `.understand-anything/knowledge-graph.json`.*
