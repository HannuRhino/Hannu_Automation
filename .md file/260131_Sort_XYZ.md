# Sort XYZ — `260131_Sort_XYZ.cs`

---

## 1. Overview

| Field | Value |
|-------|-------|
| Component Name | Sort XYZ |
| Nickname | `SrtXYZ` |
| Category | Mäkeläinen automation → Others |
| GUID | `217E719C-5BDA-4076-AEDB-559917278A13` |
| Class | `SortByCentroidComponent` |
| Base Class | `GH_Component` |

**What it does:** Receives geometry objects (Keys) and optional associated data (Values), computes the centroid of each geometry, and outputs three independently sorted lists — one per axis (X, Y, Z). Each axis output contains the sorted centroid coordinate (as a number) and the corresponding sorted values.

---

## 2. Flowchart

```
┌─────────────────────────────────────────────────────────┐
│                      SolveInstance                      │
└─────────────────────────────────────────────────────────┘
                           │
              ┌────────────▼────────────┐
              │   DA.GetDataTree(Keys)  │
              └────────────┬────────────┘
                           │ FAIL?
                    Yes ───┘  └─── No
                    │               │
             [Error + return]       │
                               ┌────▼────────────────────┐
                               │  DA.GetDataTree(Values) │
                               │  (optional, null if     │
                               │   not connected)        │
                               └────┬────────────────────┘
                                    │
                          ┌─────────▼─────────┐
                          │  keysTree empty?  │
                          └─────────┬─────────┘
                              Yes ──┘  └── No
                              │              │
                       [Warning + return]    │
                                      ┌─────▼──────────────────────┐
                                      │  Init 6 output DataTrees   │
                                      │  keysX/Y/Z, valuesX/Y/Z    │
                                      └─────┬──────────────────────┘
                                            │
                              ┌─────────────▼─────────────────┐
                              │  FOR each branch in keysTree  │
                              └─────────────┬─────────────────┘
                                            │
                                 ┌──────────▼──────────┐
                                 │  GetValuesBranch()  │
                                 │  (match or fallback │
                                 │   to keys)          │
                                 └──────────┬──────────┘
                                            │
                                 ┌──────────▼──────────┐
                                 │  Size mismatch?     │
                                 │  → trim to min,     │
                                 │    add Remark msg   │
                                 └──────────┬──────────┘
                                            │
                                 ┌──────────▼──────────┐
                                 │  ExtractCentroids() │
                                 │  For each geometry: │
                                 │  ┌────────────────┐ │
                                 │  │ Type check 1-5 │ │
                                 │  │ (GH_Point,     │ │
                                 │  │  GH_Line, etc) │ │
                                 │  └───────┬────────┘ │
                                 │          │ not found│
                                 │  ┌───────▼────────┐ │
                                 │  │ CastTo 6-9     │ │
                                 │  │ (fallback cast)│ │
                                 │  └───────┬────────┘ │
                                 │          │ not found│
                                 │  ┌───────▼────────┐ │
                                 │  │ ScriptVariable │ │
                                 │  │ method 10      │ │
                                 │  └───────┬────────┘ │
                                 │          │          │
                                 │  ┌───────▼────────┐ │
                                 │  │ Round to 2 dp  │ │
                                 │  │ AwayFromZero   │ │
                                 │  └───────┬────────┘ │
                                 │          │          │
                                 │  ┌───────▼────────┐ │
                                 │  │ Add to list    │ │
                                 │  │ (skip invalid) │ │
                                 │  └────────────────┘ │
                                 └──────────┬──────────┘
                                            │
                                 ┌──────────▼──────────┐
                                 │ centroids empty?    │
                                 └──────────┬──────────┘
                                      Yes ──┘  └── No
                                      │              │
                               [Warning, skip]       │
                                              ┌──────▼──────────────────┐
                                              │  ProcessAxisSorting()   │
                                              │  ┌───────────────────┐  │
                                              │  │ SortByAxis(X)     │  │
                                              │  │ OrderBy(X)        │  │
                                              │  │ .ThenBy(index)    │  │
                                              │  │ → keysX, valuesX  │  │
                                              │  ├───────────────────┤  │
                                              │  │ SortByAxis(Y)     │  │
                                              │  │ → keysY, valuesY  │  │
                                              │  ├───────────────────┤  │
                                              │  │ SortByAxis(Z)     │  │
                                              │  │ → keysZ, valuesZ  │  │
                                              │  └───────────────────┘  │
                                              └──────┬──────────────────┘
                                                     │
                                            [Next branch]
                                                     │
                              ┌──────────────────────▼──────────────────────┐
                              │  DA.SetDataTree (0..5)                      │
                              │  KX, VX, KY, VY, KZ, VZ                     │
                              └─────────────────────────────────────────────┘
```

### `GetAccurateCentroid()` sub-flowchart

```
Input: GeometryBase geo
           │
     ┌─────▼──────┐    Yes   ┌────────────────────┐
     │ is Point?  ├─────────►│ return .Location   │
     └─────┬──────┘          └────────────────────┘
           │ No
     ┌─────▼──────┐    Yes   ┌────────────────────┐
     │ is Line    ├─────────►│ return PointAt(0.5)│
     │ Curve?     │          └────────────────────┘
     └─────┬──────┘
           │ No
     ┌─────▼──────────┐
     │ is Polyline    │
     │ Curve?         │
     └────┬───────┬───┘
       closed   open
          │       └──► return PointAt(domain.Mid)
          ▼
     AreaMassProperties.Compute()
          │
     ┌─────▼──────┐    Yes   ┌────────────────────┐
     │ is Curve?  ├─────────►│ closed → AreaMass  │
     └─────┬──────┘          │ open   → domain.Mid│
           │ No              └────────────────────┘
     ┌─────▼──────┐
     │ is Brep?   │
     └────┬───────┘
       solid     surface
          │           └──► AreaMassProperties → fallback bbox
          ▼
     VolumeMassProperties → fallback AreaMass → fallback bbox
          │
     ┌─────▼──────┐
     │ is Surface?├─────────► AreaMassProperties → fallback domain.Mid
     └─────┬──────┘
           │
     ┌─────▼──────┐
     │ is Mesh?   │
     └────┬───────┘
       closed     open
          │           └──► AreaMassProperties → fallback bbox
          ▼
     VolumeMassProperties → fallback AreaMass → fallback bbox
          │
     ┌─────▼──────────┐    Yes   ┌───────────────────────────┐
     │ is Extrusion?  ├─────────►│ ToBrep() → recursive call │
     └─────┬──────────┘          └───────────────────────────┘
           │ No
     ┌─────▼────────────────────┐
     │ Final fallback:          │
     │ GetBoundingBox().Center  │
     └──────────────────────────┘
```

---

## 3. Inputs & Outputs

### Inputs

| # | Name | Nickname | Type | Access | Required |
|---|------|----------|------|--------|----------|
| 0 | Keys | K | Geometry | Tree | Yes |
| 1 | Values | V | Generic | Tree | No |

**Keys** — any geometry type: Point, Line, Polyline, Rectangle, Vector, Curve, Brep, Mesh, Extrusion, Surface.
**Values** — any data associated with each geometry. If omitted, the geometry itself is used as the value.

### Outputs

| # | Name | Nickname | Type | Description |
|---|------|----------|------|-------------|
| 0 | KeysX | KX | Number Tree | Centroid X coords, sorted ascending |
| 1 | ValuesX | VX | Generic Tree | Values sorted by X |
| 2 | KeysY | KY | Number Tree | Centroid Y coords, sorted ascending |
| 3 | ValuesY | VY | Generic Tree | Values sorted by Y |
| 4 | KeysZ | KZ | Number Tree | Centroid Z coords, sorted ascending |
| 5 | ValuesZ | VZ | Generic Tree | Values sorted by Z |

---

## 4. Examples

### Example A — Points

**Input:** 4 points at random positions, labeled A–D.

```
A = (3.0, 1.0, 0.0)
B = (1.0, 4.0, 2.0)
C = (2.0, 2.0, 5.0)
D = (4.0, 3.0, 1.0)
```

Centroid = the point itself (no calculation needed).

**Sorted by X** (ascending):
```
KX = [1.0, 2.0, 3.0, 4.0]
VX = [B,   C,   A,   D  ]
```

**Sorted by Y** (ascending):
```
KY = [1.0, 2.0, 3.0, 4.0]
VY = [A,   C,   D,   B  ]
```

**Sorted by Z** (ascending):
```
KZ = [0.0, 1.0, 2.0, 5.0]
VZ = [A,   D,   B,   C  ]
```

> Use case: order a list of attractor points from left to right, bottom to top.

---

### Example B — Surfaces

**Input:** 3 planar surfaces (Brep faces) at different heights.

```
S1 = flat surface centered at (0.0,  0.0, 0.0), 2×2 m
S2 = flat surface centered at (5.0,  0.0, 3.0), 2×2 m
S3 = flat surface centered at (2.5,  0.0, 6.0), 2×2 m
```

Centroid is computed via `AreaMassProperties.Compute()`.

**Sorted by Z** (floor order — bottom to top):
```
KZ = [0.0,  3.0,  6.0]
VZ = [S1,   S2,   S3 ]
```

> Use case: number floor slabs from ground level up, assign floor tags automatically.

---

### Example C — Curves

**Input:** 3 closed curves (circles) on the XY plane.

```
C1 = circle, center (4.0, 0.0, 0.0), r=1
C2 = circle, center (1.0, 0.0, 0.0), r=1
C3 = circle, center (7.0, 0.0, 0.0), r=1
```

Closed curves → centroid via `AreaMassProperties.Compute()` = center of circle.

**Sorted by X** (left to right):
```
KX = [1.0, 4.0, 7.0]
VX = [C2,  C1,  C3 ]
```

**Open curve example:**
```
L1 = arc from (0,0,0) to (6,0,0)   → centroid = PointAt(domain.Mid) ≈ (3.0, y, 0)
L2 = arc from (0,0,0) to (2,0,0)   → centroid ≈ (1.0, y, 0)
```

> Use case: sort profile curves before lofting so the loft direction is predictable.

---

### Example D — Mixed Geometry (Brep + Mesh + Curve)

**Input:** heterogeneous list in one branch.

```
G1 = Brep box,   bounding center ≈ (1.0, 1.0, 0.5)   → VolumeMassProperties
G2 = Mesh dome,  closed mesh    → VolumeMassProperties  → center ≈ (5.0, 1.0, 1.0)
G3 = NurbsCurve, open           → PointAt(domain.Mid)  → midpoint ≈ (3.0, 2.0, 0.0)
```

**Sorted by X:**
```
KX = [1.0,  3.0,  5.0]
VX = [G1,   G3,   G2 ]
```

> Use case: sort structural elements (columns = Brep, beams = curves, panels = mesh) by their X position for sequential numbering.

---

### Example E — DataTree (multiple branches)

**Input tree:**
```
{0;0} → [A(1,0,0), B(3,0,0)]
{0;1} → [C(2,0,0), D(0,0,0)]
```

**Output KX tree (sorted by X):**
```
{0;0} → [1.0, 3.0]   VX → [A, B]
{0;1} → [0.0, 2.0]   VX → [D, C]
```

Each branch is sorted independently. Branch paths are preserved exactly.

---

## 5. Class Reference

### `SortByCentroidComponent`

```csharp
public class SortByCentroidComponent : GH_Component
```

The main component class. All logic lives here.

#### Constructor

```csharp
public SortByCentroidComponent()
    : base(
        "Sort XYZ",               // display name
        "SrtXYZ",                 // nickname
        "Sort geometry objects…", // description
        "Mäkeläinen automation",  // category tab
        "Others"                  // subcategory
    )
```

#### Key Overrides

```csharp
public override Guid ComponentGuid
    => new Guid("217E719C-5BDA-4076-AEDB-559917278A13");

protected override System.Drawing.Bitmap Icon
    => Resources.Sort_XYZ;

public override GH_Exposure Exposure
    => GH_Exposure.primary;
```

---

## 6. Method Reference

### `RegisterInputParams(GH_InputParamManager pManager)`

```csharp
protected override void RegisterInputParams(GH_InputParamManager pManager)
```

Declares the two input parameters.

```csharp
// Index 0 — Keys (required)
pManager.AddGeometryParameter("Keys", "K", "...", GH_ParamAccess.tree);

// Index 1 — Values (optional)
pManager.AddGenericParameter("Values", "V", "...", GH_ParamAccess.tree);
pManager[1].Optional = true;
```

**Rule:** Always use `GH_ParamAccess.tree` when the component must handle DataTree inputs. Mark optional params with `pManager[n].Optional = true` to suppress the "no data" error.

---

### `RegisterOutputParams(GH_OutputParamManager pManager)`

```csharp
protected override void RegisterOutputParams(GH_OutputParamManager pManager)
```

Declares six output parameters (indices 0–5):

```csharp
pManager.AddNumberParameter ("KeysX",   "KX", "...", GH_ParamAccess.tree); // 0
pManager.AddGenericParameter("ValuesX", "VX", "...", GH_ParamAccess.tree); // 1
pManager.AddNumberParameter ("KeysY",   "KY", "...", GH_ParamAccess.tree); // 2
pManager.AddGenericParameter("ValuesY", "VY", "...", GH_ParamAccess.tree); // 3
pManager.AddNumberParameter ("KeysZ",   "KZ", "...", GH_ParamAccess.tree); // 4
pManager.AddGenericParameter("ValuesZ", "VZ", "...", GH_ParamAccess.tree); // 5
```

---

### `SolveInstance(IGH_DataAccess DA)`

```csharp
protected override void SolveInstance(IGH_DataAccess DA)
```

Main execution. Called by Grasshopper on every input change.

```csharp
// Read
DA.GetDataTree(0, out GH_Structure<IGH_GeometricGoo> keysTree);
DA.GetDataTree(1, out GH_Structure<IGH_Goo> valuesTree);  // null if not connected

// Init outputs
var keysXTree   = new DataTree<double>();
var valuesXTree = new DataTree<object>();
// ... repeat for Y, Z

// Process branches
for (int i = 0; i < keysTree.PathCount; i++)
{
    GH_Path path = keysTree.Paths[i];
    List<IGH_GeometricGoo> keysBranch = keysTree.get_Branch(path);

    List<object> valuesBranch = GetValuesBranch(valuesTree, path, keysBranch);
    // trim if sizes differ ...
    var (centroids, validValues) = ExtractCentroids(keysBranch, valuesBranch);
    ProcessAxisSorting(centroids, validValues, path, ...all 6 trees...);
}

// Write
DA.SetDataTree(0, keysXTree);
DA.SetDataTree(1, valuesXTree);
// ... indices 2-5
```

---

### `GetValuesBranch(...)`

```csharp
private List<object> GetValuesBranch(
    GH_Structure<IGH_Goo> valuesTree,   // full values tree (nullable)
    GH_Path path,                        // branch to look up
    List<IGH_GeometricGoo> keysBranch)  // fallback source
```

**Returns:** `List<object>` — values for the branch.

**Decision logic:**
```
valuesTree == null                      → return keys as objects
valuesTree does not contain path        → return keys as objects
branch at path is null or empty         → return keys as objects
otherwise                               → v.ScriptVariable() for each item
```

**Reuse tip:** Copy this method verbatim for any component with an optional values input.

---

### `ExtractCentroids(...)`

```csharp
private (List<Point3d> centroids, List<object> values) ExtractCentroids(
    List<IGH_GeometricGoo> geometries,
    List<object> values)
```

**Returns:** Two parallel lists — only items where a valid centroid was found are included.

**Detection cascade (10 steps):**

```csharp
// Step 1-5: direct type checks
if (geo is GH_Point)      → centroid = ghPoint.Value
if (geo is GH_Line)       → centroid = line.PointAt(0.5)
if (geo is GH_Rectangle)  → centroid = rect.Center
if (geo is GH_Curve)      → centroid = GetAccurateCentroid(curve)
if (geo is GH_Vector)     → centroid = new Point3d(v.X/2, v.Y/2, v.Z/2)

// Step 6-9: CastTo fallbacks
geo.CastTo(out GH_Point ghPt)      → centroid = ghPt.Value
geo.CastTo(out GH_Rectangle ghRc)  → centroid = ghRc.Value.Center
geo.CastTo(out GH_Curve ghCv)      → centroid = GetAccurateCentroid(ghCv.Value)
geo.CastTo(out GH_Vector ghVc)     → centroid = new Point3d(v.X/2, ...)

// Step 10: ScriptVariable raw extraction
object raw = geo.ScriptVariable();
// handles: Point3d, Line, Rectangle3d, Polyline, Vector3d, Curve, GeometryBase
```

**Rounding (applied once, before adding to list):**
```csharp
centroid = new Point3d(
    Math.Round(centroid.X, 2, MidpointRounding.AwayFromZero),
    Math.Round(centroid.Y, 2, MidpointRounding.AwayFromZero),
    Math.Round(centroid.Z, 2, MidpointRounding.AwayFromZero)
);
// If value is Point3d, round it too
```

---

### `GetAccurateCentroid(GeometryBase geo)`

```csharp
private Point3d GetAccurateCentroid(GeometryBase geo)
```

**Returns:** `Point3d` centroid, or `Point3d.Unset` on failure. Wrapped in `try/catch`.

**Full dispatch table:**

```csharp
// Point
if (geo is Rhino.Geometry.Point)    → return point.Location

// Line
if (geo is LineCurve)               → return lineCurve.Line.PointAt(0.5)

// Polyline
if (geo is PolylineCurve)
    closed → AreaMassProperties.Compute(polyCurve).Centroid
    open   → polyCurve.PointAt(domain.Mid)

// General Curve
if (geo is Curve)
    closed → AreaMassProperties.Compute(crv).Centroid
    open   → crv.PointAt(domain.Mid)

// Brep
if (geo is Brep)
    solid   → VolumeMassProperties.Compute(brep).Centroid
    surface → AreaMassProperties.Compute(brep).Centroid
    fallback→ brep.GetBoundingBox(true).Center

// Surface
if (geo is Surface)
    → AreaMassProperties.Compute(srf).Centroid
    fallback → srf.PointAt(u.Mid, v.Mid)

// Mesh
if (geo is Mesh)
    closed  → VolumeMassProperties.Compute(mesh).Centroid
    open    → AreaMassProperties.Compute(mesh).Centroid
    fallback→ mesh.GetBoundingBox(true).Center

// Extrusion
if (geo is Extrusion)
    → ext.ToBrep() then GetAccurateCentroid(brep)  // recursive

// Final fallback
→ geo.GetBoundingBox(true).Center
```

---

### `ProcessAxisSorting(...)`

```csharp
private void ProcessAxisSorting(
    List<Point3d> centroids,
    List<object> values,
    GH_Path path,
    DataTree<double> keysXTree,  DataTree<object> valuesXTree,
    DataTree<double> keysYTree,  DataTree<object> valuesYTree,
    DataTree<double> keysZTree,  DataTree<object> valuesZTree)
```

Calls `SortByAxis()` three times and appends results to the output trees.

```csharp
var sortedX = SortByAxis(centroids, values, Axis.X);
keysXTree.AddRange(sortedX.coordinates, path);
valuesXTree.AddRange(sortedX.sortedValues, path);
// repeat for Y, Z
```

---

### `SortByAxis(...)`

```csharp
private (List<double> coordinates, List<object> sortedValues) SortByAxis(
    List<Point3d> centroids,
    List<object> values,
    Axis axis)
```

**Returns:** Tuple of sorted coordinate doubles and sorted values.

```csharp
// Build pairs with original index
List<CentroidValuePair> pairs = centroids.Select((c, i) => new CentroidValuePair
{
    Centroid = c, Value = values[i], OriginalIndex = i
}).ToList();

// Sort — stable via ThenBy(OriginalIndex)
sortedPairs = pairs.OrderBy(p => p.Centroid.X)   // or .Y / .Z
                   .ThenBy(p => p.OriginalIndex)
                   .ToList();

// Extract
coordinates  = sortedPairs.Select(p => GetCoordinate(p.Centroid, axis)).ToList();
sortedValues = sortedPairs.Select(p => p.Value).ToList();
```

**Edge cases:**
- Empty input → returns two empty lists immediately.
- Single item → returns without sorting.

---

### `GetCoordinate(Point3d point, Axis axis)`

```csharp
private double GetCoordinate(Point3d point, Axis axis)
// Axis.X → point.X
// Axis.Y → point.Y
// Axis.Z → point.Z
// default → 0.0
```

Simple switch dispatcher. Used inside `SortByAxis()` to extract the target coordinate after sorting.

---

## 7. Helper Types

### `enum Axis`

```csharp
private enum Axis { X = 0, Y = 1, Z = 2 }
```

Avoids magic numbers when selecting the sort axis. Passed into `SortByAxis()` and `GetCoordinate()`.

---

### `class CentroidValuePair`

```csharp
private class CentroidValuePair
{
    public Point3d Centroid     { get; set; }
    public object  Value        { get; set; }
    public int     OriginalIndex { get; set; }
}
```

Keeps the centroid, value, and original position index bound together through LINQ sorting.
`OriginalIndex` is the stable-sort tiebreaker — when two centroids have the same coordinate on the sort axis, original input order is preserved.

---

## 8. Runtime Messages

| Level | Condition |
|-------|-----------|
| `Error` | `DA.GetDataTree(0)` fails |
| `Warning` | `keysTree` is null or empty |
| `Warning` | No valid geometry in a branch |
| `Warning` | Exception thrown inside `GetAccurateCentroid()` |
| `Remark` | Keys and Values branch sizes differ — trimmed to shorter |

---

## 9. Template — Building a Similar Component

Use this checklist when creating a new component that sorts or reorders geometry by any computed scalar.

### Step 1 — Class skeleton

```csharp
public class MyNewSortComponent : GH_Component
{
    public MyNewSortComponent()
        : base("My Sort", "MySort", "Description", "Category", "Subcategory") { }

    public override Guid ComponentGuid => new Guid("/* generate a new GUID */");
    protected override Bitmap Icon     => Resources.MyIcon;
    public override GH_Exposure Exposure => GH_Exposure.primary;
}
```

### Step 2 — Inputs

```csharp
protected override void RegisterInputParams(GH_InputParamManager pManager)
{
    pManager.AddGeometryParameter("Keys",   "K", "...", GH_ParamAccess.tree);
    pManager.AddGenericParameter ("Values", "V", "...", GH_ParamAccess.tree);
    pManager[1].Optional = true;
}
```

### Step 3 — Outputs

```csharp
// One pair (key + value) per sort criterion
pManager.AddNumberParameter ("KeysX",   "KX", "...", GH_ParamAccess.tree);
pManager.AddGenericParameter("ValuesX", "VX", "...", GH_ParamAccess.tree);
```

### Step 4 — SolveInstance pattern

```csharp
protected override void SolveInstance(IGH_DataAccess DA)
{
    DA.GetDataTree(0, out GH_Structure<IGH_GeometricGoo> keysTree);
    DA.GetDataTree(1, out GH_Structure<IGH_Goo> valuesTree);

    var outputKeys   = new DataTree<double>();
    var outputValues = new DataTree<object>();

    for (int i = 0; i < keysTree.PathCount; i++)
    {
        GH_Path path = keysTree.Paths[i];
        var keysBranch   = keysTree.get_Branch(path) as List<IGH_GeometricGoo>;
        var valuesBranch = GetValuesBranch(valuesTree, path, keysBranch);

        // replace with your scalar extraction
        var scalars = keysBranch.Select(g => ComputeScalar(g)).ToList();

        // sort
        var pairs = scalars.Zip(valuesBranch, (s, v) => (s, v))
                           .Select((x, idx) => (x.s, x.v, idx))
                           .OrderBy(x => x.s)
                           .ThenBy(x => x.idx)
                           .ToList();

        outputKeys.AddRange(pairs.Select(x => x.s), path);
        outputValues.AddRange(pairs.Select(x => x.v), path);
    }

    DA.SetDataTree(0, outputKeys);
    DA.SetDataTree(1, outputValues);
}
```

### Step 5 — Replace centroid with your scalar

| Sort goal | Scalar expression |
|-----------|-------------------|
| By X position | `GetAccurateCentroid(geo).X` |
| By Y position | `GetAccurateCentroid(geo).Y` |
| By Z position | `GetAccurateCentroid(geo).Z` |
| By surface area | `AreaMassProperties.Compute(brep).Area` |
| By volume | `VolumeMassProperties.Compute(brep).Volume` |
| By curve length | `curve.GetLength()` |
| By distance to point | `centroid.DistanceTo(referencePoint)` |
| By bounding box size | `bbox.Diagonal.Length` |

### Step 6 — Key rules to follow

- Always round scalars before sorting to avoid floating-point noise.
- Always use a data class (like `CentroidValuePair`) to keep keys and values aligned — never sort two separate lists independently.
- Always use `ThenBy(OriginalIndex)` as tiebreaker for stable output.
- Always use `GH_ParamAccess.tree` and loop over `PathCount` for multi-branch support.
- Wrap geometry calculations in `try/catch` and emit `Warning` messages instead of crashing.
