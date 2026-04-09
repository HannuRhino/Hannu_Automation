# GitNexus Knowledge Graph — GroupPoint_XY

> Generated: 2026-03-16
> Re-index command: `npx gitnexus analyze`

---

## Summary

| Metric | Value |
|--------|-------|
| Indexed files | 45 |
| Symbols (nodes) | 46 |
| Relationships (edges) | 3 |
| Functional clusters | 0 |
| Execution flows (processes) | 0 |

The graph currently resolves at **file/folder level**. No class or function symbols were extracted — run `npx gitnexus analyze` to deepen the index.

---

## Nodes (46)

### Folder

| Name | Path |
|------|------|
| Properties | `Properties/` |

### Files — C# Source

| File | Description |
|------|-------------|
| `GroupPoint_XYComponent.cs` | Main Grasshopper component logic |
| `GroupPoint_XYInfo.cs` | Plugin assembly info |
| `251221_ViewToWorldXY_R1.cs` | View-to-world XY transform, revision 1 |
| `251222_IdentifySurfaces_R1.cs` | Surface identification, revision 1 |
| `251225_FrontViewToAnyView_R2.cs` | Front-view to arbitrary view, revision 2 |
| `251225_IdentifyBoundary_R3.cs` | Boundary identification, revision 3 |
| `251225_SortedPointList_R2.cs` | Sorted point list utility, revision 2 |
| `260103_ConcaveDefinition_R2.cs` | Concave hull/definition, revision 2 |
| `260115_BREPBoundary.cs` | BREP boundary extraction |
| `260125_Mid Station_R1.cs` | Mid-station calculation, revision 1 |
| `260131_Sort_XYZ.cs` | Sort points by XYZ axes |
| `260214_Dimension_R2.cs` | Dimension annotation, revision 2 |
| `260214_Dimension_R3.cs` | Dimension annotation, revision 3 |
| `260214_SortCurves_XYZ_R1.cs` | Sort curves by XYZ, revision 1 |
| `ExplodeBrep.cs` | BREP explode utility |
| `ExplodeCurveandPoints.cs` | Curve and point explode utility |
| `ExtreamByAxis.cs` | Extreme points by axis |
| `Get Boundary Fast.cs` | Fast boundary extraction |
| `SortLineByAxis.cs` | Sort lines by axis |
| `Properties/Resources.Designer.cs` | Auto-generated resource designer |

### Files — Project & Config

| File | Description |
|------|-------------|
| `GroupPoint_XY.csproj` | C# project file |
| `GroupPoint_XY.csproj.user` | User-specific project settings |
| `GroupPoint_XY.sln` | Visual Studio solution file |
| `Properties/launchSettings.json` | Launch configuration |
| `Properties/Resources.resx` | Resource definitions |
| `Don_Automation.gha` | Compiled Grasshopper plugin binary |

### Files — Documentation (EN)

| File |
|------|
| `GroupPoint_XY_EN.md` |
| `251221_ViewToWorldXY_R1.md` |
| `260131_Sort_XYZ.md` |
| `ExplodeCurveAndPoints_EN.md` |
| `ExtremeCurve_EN.md` |
| `IdentifySurfaces_EN.md` |
| `Relay_EN.md` |
| `SortCurvesByXYZ_EN.md` |
| `SortLineByAxis_EN.md` |
| `TeklaDimension_EN.md` |

### Files — Documentation (VI)

| File |
|------|
| `GroupPoint_XY_VI.md` |
| `251221_ViewToWorldXY_R1_VI.md` |
| `260131_Sort_XYZ_VI.md` |
| `ExtremeCurve_VI.md` |
| `IdentifySurfaces_VI.md` |
| `Relay_VI.md` |
| `SortCurvesByXYZ_VI.md` |
| `SortLineByAxis_VI.md` |
| `TeklaDimension_VI.md` |

---

## Relationships (3)

All detected relationships are `CONTAINS` edges from the `Properties` folder to its children.

| From | Relation | To | Confidence |
|------|----------|----|-----------|
| `Properties` | CONTAINS | `Resources.resx` | 1.0 |
| `Properties` | CONTAINS | `Resources.Designer.cs` | 1.0 |
| `Properties` | CONTAINS | `launchSettings.json` | 1.0 |

---

## Graph Diagram

```
GroupPoint_XY (repo root)
├── GroupPoint_XYComponent.cs
├── GroupPoint_XYInfo.cs
├── [C# source files × 18]
├── GroupPoint_XY.csproj / .sln
├── Don_Automation.gha
├── [Documentation EN × 10]
├── [Documentation VI × 9]
└── Properties/
    ├── Resources.resx        ←─ CONTAINS
    ├── Resources.Designer.cs ←─ CONTAINS
    └── launchSettings.json   ←─ CONTAINS
```

---

## Notes

- **No execution flows (processes)** were detected — the index is file-level only. Re-run `npx gitnexus analyze` to extract class/method-level symbols and call-graph relationships.
- **No functional clusters** were auto-detected by the Leiden community algorithm, likely because cross-file CALLS edges have not yet been resolved.
- Filename prefixes follow the pattern `YYMMDD_ComponentName_RN` (date + component + revision).
- Each C# component has a matching bilingual documentation pair (`_EN.md` / `_VI.md`).
