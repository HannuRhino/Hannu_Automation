# ViewToWorldXY — `251221_ViewToWorldXY_R1.cs`

---

## 1. Overview

| Field | Value |
|-------|-------|
| Component Name | `TransformToDrawingView` |
| Nickname | `V→XY` |
| Category | Mäkeläinen automation → Geometry |
| GUID | `27EE8E6F-1A42-4595-8D08-7D878FC9328F` |
| Class | `ViewCSToWorldXY_TeklaViewInput` |
| Base Class | `GH_Component` |

**What it does:** Receives a Tekla Drawing View object (or a fallback Rhino Plane) and any Rhino geometry, extracts the view's coordinate system, builds an inverse transform from the view plane back to World XY, and outputs the geometry repositioned in World XY space.

**Design decisions:**
- Both `View` and `Plane` inputs are optional individually, but at least one must be provided.
- If both are provided the extracted plane and the input plane are validated to match — a mismatch is treated as an error.
- Geometry is unwrapped from Grasshopper wrapper types before transformation and returned as raw Rhino geometry.
- Value-type structs (`Point3d`, `Rectangle3d`, etc.) are transformed via reflection so the same method handles all geometry types.

---

## 2. Flowchart

### `SolveInstance` — top-level flow

```
┌─────────────────────────────────────────────────────────────────┐
│                         SolveInstance                           │
└─────────────────────────────────────────────────────────────────┘
                               │
              ┌────────────────▼─────────────────┐
              │  DA.GetData(0, ref teklaViewObj) │  → hasView
              │  DA.GetData(1, ref inputPlane)   │  → hasPlane
              │  DA.GetData(2, ref geometryInput)│  → required
              └────────────────┬─────────────────┘
                               │
                   ┌───────────▼───────────┐
                   │  geometry missing?    │
                   └───────────┬───────────┘
                         Yes ──┘  └── No
                         │              │
                  [Error + return]      │
                                ┌───────▼────────────────┐
                                │  !hasView && !hasPlane?│
                                └───────┬────────────────┘
                                  Yes ──┘  └── No
                                  │              │
                           [Error + return]      │
                                         ┌───────▼──────────────────┐
                                         │  geometryInput == null?  │
                                         └───────┬──────────────────┘
                                           Yes ──┘  └── No
                                           │              │
                                    [Error + return]      │
                                                  ┌───────▼────────────────────┐
                                                  │  ExtractGeometryFromWrapper│
                                                  │  (geometryInput)           │
                                                  └───────┬────────────────────┘
                                                          │
                                              ┌───────────▼───────────┐
                                              │  rawGeometry == null? │
                                              └───────────┬───────────┘
                                                    Yes ──┘  └── No
                                                    │              │
                                             [Error + return]      │
                                                           ┌───────▼────────────────────┐
                                                           │  DetermineWorkingPlane()   │
                                                           └───────┬────────────────────┘
                                                                   │
                                                          ┌────────▼────────────────┐
                                                          │  NormalizeViewPlane()   │
                                                          └────────┬────────────────┘
                                                                   │
                                                          ┌────────▼────────────────────┐
                                                          │  ValidateNormalizedPlane()  │
                                                          └────────┬────────────────────┘
                                                            valid? │
                                                             No ───┘  └── Yes
                                                             │               │
                                                      [Warning]              │
                                                             └───────────────▼
                                                                    ┌────────────────────────────┐
                                                                    │  Transform.PlaneToPlane    │
                                                                    │  (WorldXY → normalizedPlane│
                                                                    └────────┬───────────────────┘
                                                                             │
                                                                    ┌────────▼────────────────────┐
                                                                    │  TryGetInverse()            │
                                                                    └────────┬────────────────────┘
                                                                      fail ──┘  └── ok
                                                                      │               │
                                                               [Error + return]       │
                                                                              ┌───────▼─────────────────┐
                                                                              │  TransformGeometryObject│
                                                                              │  (rawGeometry,          │
                                                                              │   inverseTransform)     │
                                                                              └───────┬─────────────────┘
                                                                                      │
                                                                             ┌────────▼─────────────┐
                                                                             │  result == null?     │
                                                                             └────────┬─────────────┘
                                                                               Yes ───┘  └── No
                                                                               │               │
                                                                        [Error + return]  DA.SetData(0, result)
```

---

### `DetermineWorkingPlane` — sub-flowchart

```
Input: teklaViewObject, hasView, inputPlane, hasPlane
                │
    ┌───────────▼────────────────────┐
    │  !hasView || viewObject == null│
    └───────────┬────────────────────┘
          Yes ──┘  └─────── No
          │                 │
  ┌───────▼──────────┐      │
  │  hasPlane &&     │      │
  │  inputPlane valid│      │
  └───────┬──────────┘      │
    No ───┘  └── Yes        │
    │          │            │
  [throw]  [Remark]         │
             │              │
         return inputPlane  │
                    ┌───────▼──────────────────────┐
                    │  ExtractTeklaViewCS(viewObj) │
                    └───────┬──────────────────────┘
                            │
                   ┌────────▼──────────────────────┐
                   │  ConvertTeklaCSToRhinoPlane() │
                   └────────┬──────────────────────┘
                            │
                ┌───────────▼───────────────────┐
                │  hasPlane && inputPlane valid?│
                └───────────┬───────────────────┘
                      No ───┘  └── Yes
                      │               │
                return            PlanesAreEqual()?
                extractedPlane      No → [throw]
                                    Yes → [Remark] → return extractedPlane
```

---

### `ExtractTeklaViewCS` — sub-flowchart

```
Input: teklaViewObject
          │
  ┌───────▼────────────────────┐
  │  is GH_ObjectWrapper?      │
  └───────┬────────────────────┘
    Yes ──┘  └── No
    │               │
  unwrap.Value    already unwrapped
    │               │
    └──────┬────────┘
           │
  ┌────────▼──────────────────────┐
  │  has .Value property (Goo)?   │  ← reflection
  └────────┬──────────────────────┘
     Yes ──┘  └── No
     │               │
  unwrap again    continue
     │               │
     └──────┬────────┘
            │
   ┌────────▼──────────────────┐
   │  is Tekla.Drawing.View?   │  ← direct cast
   └────────┬──────────────────┘
      Yes ──┘  └── No
      │               │
  ExtractFromTeklaView()    ExtractViaReflection()
      │                         │
      └────────────┬────────────┘
                   │
             return TSG.CoordinateSystem
```

---

### `ExtractFromTeklaView` — sub-flowchart

```
Input: View teklaView
          │
  ┌───────▼────────────────────────┐
  │  teklaView.ViewCoordinateSystem│
  └───────┬────────────────────────┘
    != null──┘  └── null
    │                   │
  return viewCS   ┌─────▼─────────────────────────────┐
                  │  teklaView.DisplayCoordinateSystem│
                  └─────┬─────────────────────────────┘
                  != null──┘  └── null
                  │                   │
                return displayCS   [throw]
```

---

## 3. Inputs & Outputs

### Inputs

| # | Name | Nickname | Type | Access | Required |
|---|------|----------|------|--------|----------|
| 0 | View | View | Generic | Item | No (optional) |
| 1 | Plane | Pln | Plane | Item | No (optional) |
| 2 | Geometry | G | Geometry | Item | **Yes** |

**View** — A Tekla Drawing `View` object (passed as `GH_ObjectWrapper` or wrapped Goo). Used to extract the `ViewCoordinateSystem` or `DisplayCoordinateSystem`.

**Plane** — A Rhino `Plane` used as the source coordinate system when no View is provided, or for cross-validation when View is provided.

**Geometry** — Any Rhino geometry type: Point, Curve, Line, Arc, Circle, Surface, Brep, Mesh, Rectangle, Box, Plane, Vector.

> At least one of **View** or **Plane** must be connected.

### Outputs

| # | Name | Nickname | Type | Description |
|---|------|----------|------|-------------|
| 0 | TransGeo | TG | Geometry | Geometry transformed from the View/Plane coordinate system into World XY |

---

## 4. Examples

### Example A — Transform a Point from a tilted View plane to World XY

**Setup:**
- View plane: origin at `(10, 5, 0)`, X-axis pointing at 45° in XY, Y-axis pointing at 135°.
- Input point (in world space, expressed in the view's local frame): `(1, 0, 0)` from the view origin along the view's X axis.

**In world coordinates the point sits at approximately:** `(10.71, 5.71, 0)`.

After applying the inverse of `PlaneToPlane(WorldXY → viewPlane)`:

```
TransGeo = (1.0, 0.0, 0.0)
```

The geometry is now expressed in World XY — the tilt of the view has been "undone".

> Use case: dimensions or annotation objects created in a tilted drawing view need to be checked or processed in flat World XY space.

---

### Example B — Fallback to Plane input (no View)

**Setup:**
- View input: not connected.
- Plane input: `origin=(5,5,0), X=(1,0,0), Y=(0,1,0)` (same as WorldXY, offset by 5,5).
- Geometry: a rectangle with corners at `(6,6,0)` and `(9,9,0)`.

Since the plane matches WorldXY in orientation, the inverse transform is a pure translation.

```
TransGeo = rectangle shifted by (-5, -5, 0)
         → corners at (1,1,0) and (4,4,0)
```

> Use case: geometry from a translated local coordinate system needs to be compared against a global reference.

---

### Example C — Both View and Plane connected (validation)

**Setup:**
- View input: Tekla View with `ViewCoordinateSystem` at `origin=(0,0,3)`, axes aligned to World.
- Plane input: `origin=(0,0,3), X=(1,0,0), Y=(0,1,0)`.

Both describe the same plane → `PlanesAreEqual()` returns `true`.

**Remark message:** `"View and Plane match - using extracted Plane"`

**Result:** Geometry is transformed by the inverse of the view plane. A point at `(2,3,5)` world → `(2,3,2)` in World XY (Z offset removed).

---

### Example D — Mismatch triggers error

**Setup:**
- View plane: `origin=(0,0,0)`, X-axis along world X.
- Plane input: `origin=(10,0,0)`, X-axis along world X.

`PlanesAreEqual()` returns `false` — origins differ by more than `PLANE_TOLERANCE (0.001)`.

**Error message:** `"Invalid View: Extracted Plane does not match input Plane"`

No output is produced.

> Use case: acts as a built-in sanity check when automating drawing view selection.

---

### Example E — Non-perpendicular plane (warning, not error)

**Setup:**
- Input plane has axes that are not exactly unit-length due to floating-point accumulation from a script.

After `NormalizeViewPlane()` unitizes both axes, `ValidateNormalizedPlane()` checks:
- `|XAxis| ≈ 1.0` within `VALIDATION_TOLERANCE (0.01)` ✓
- `|YAxis| ≈ 1.0` ✓
- `XAxis · YAxis < 0.01` (dot product close to zero = perpendicular) ✓

If any check fails: **Warning** `"Plane axes are not perpendicular or normalized. Results may be inaccurate."` — transform still proceeds.

---

## 5. Class Reference

### `ViewCSToWorldXY_TeklaViewInput`

```csharp
public class ViewCSToWorldXY_TeklaViewInput : GH_Component
```

The main component class.

#### Constructor

```csharp
public ViewCSToWorldXY_TeklaViewInput()
    : base(
        "TransformToDrawingView",                   // display name
        "V→XY",                                     // nickname
        "Transform geometry from Tekla Plane to World XY", // description
        "Mäkeläinen automation",                    // category tab
        "Geometry"                                  // subcategory
    )
```

#### Key Overrides

```csharp
public override Guid ComponentGuid
    => new Guid("27EE8E6F-1A42-4595-8D08-7D878FC9328F");

protected override System.Drawing.Bitmap Icon
    => Resources.ToDrawingCoordinate;

public override GH_Exposure Exposure
    => GH_Exposure.primary;
```

#### Constants

```csharp
private const double PLANE_TOLERANCE      = 0.001;  // origin & axis comparison
private const double VALIDATION_TOLERANCE = 0.01;   // unit-length & perpendicularity check
```

---

## 6. Method Reference

### `RegisterInputParams(GH_InputParamManager pManager)`

```csharp
protected override void RegisterInputParams(GH_InputParamManager pManager)
```

```csharp
pManager.AddGenericParameter("View",     "View", "Tekla Drawing View object",             GH_ParamAccess.item); // 0
pManager.AddPlaneParameter  ("Plane",    "Pln",  "Plane to transform from (View fallback)", GH_ParamAccess.item); // 1
pManager.AddGeometryParameter("Geometry","G",    "Geometry to transform",                 GH_ParamAccess.item); // 2

pManager[0].Optional = true;
pManager[1].Optional = true;
// index 2 is required — no Optional flag
```

**Rule:** Use `AddGenericParameter` for Tekla objects since they are passed as `GH_ObjectWrapper`. Mark Tekla and plane inputs Optional so that the component can run with just one of them.

---

### `RegisterOutputParams(GH_OutputParamManager pManager)`

```csharp
protected override void RegisterOutputParams(GH_OutputParamManager pManager)
```

```csharp
pManager.AddGeometryParameter("TransGeo", "TG", "Geometry transformed to World XY", GH_ParamAccess.item); // 0
```

---

### `SolveInstance(IGH_DataAccess DA)`

```csharp
protected override void SolveInstance(IGH_DataAccess DA)
```

Orchestrates the full pipeline. Steps in order:

```csharp
// 1. Read inputs
object teklaViewObject = null;
Plane  inputPlane      = Plane.Unset;
object geometryInput   = null;

bool hasView  = DA.GetData(0, ref teklaViewObject);
bool hasPlane = DA.GetData(1, ref inputPlane);

if (!DA.GetData(2, ref geometryInput)) { /* Error */ return; }

// 2. Validate at least one coordinate source
if (!hasView && !hasPlane) { /* Error */ return; }
if (geometryInput == null) { /* Error */ return; }

// 3. Unwrap geometry
object rawGeometry = ExtractGeometryFromWrapper(geometryInput);
if (rawGeometry == null) { /* Error */ return; }

// 4. Resolve working plane
Plane workingPlane    = DetermineWorkingPlane(teklaViewObject, hasView, inputPlane, hasPlane);
Plane normalizedPlane = NormalizeViewPlane(workingPlane);
if (!ValidateNormalizedPlane(normalizedPlane)) { /* Warning — continue */ }

// 5. Build inverse transform
Transform forward = Transform.PlaneToPlane(Plane.WorldXY, normalizedPlane);
if (!forward.TryGetInverse(out Transform inverse)) { /* Error */ return; }

// 6. Apply and output
object result = TransformGeometryObject(rawGeometry, inverse);
if (result == null) { /* Error */ return; }
DA.SetData(0, result);
```

---

### `DetermineWorkingPlane(...)`

```csharp
private Plane DetermineWorkingPlane(
    object teklaViewObject,
    bool   hasView,
    Plane  inputPlane,
    bool   hasPlane)
```

**Returns:** The `Plane` to use as the source coordinate system.

**Decision table:**

| hasView / viewObj | hasPlane / valid | Action |
|---|---|---|
| false / null | true + valid | Return `inputPlane` + Remark |
| false / null | false / invalid | `throw InvalidOperationException("Empty View")` |
| true + non-null | — | `ExtractTeklaViewCS` → convert → optionally validate against `inputPlane` |
| true + non-null | true + valid | `PlanesAreEqual` → mismatch throws; match → Remark + return extracted |

---

### `ExtractGeometryFromWrapper(object geometryInput)`

```csharp
private object ExtractGeometryFromWrapper(object geometryInput)
```

**Returns:** Raw Rhino geometry (`GeometryBase` subclass or `Point3d` struct), or `null`.

**Unwrap cascade:**

```csharp
// GH wrapper types — direct cast
if (geometryInput is GH_Point     ghPoint)   return ghPoint.Value;
if (geometryInput is GH_Curve     ghCurve)   return ghCurve.Value;
if (geometryInput is GH_Surface   ghSurface) return ghSurface.Value;
if (geometryInput is GH_Mesh      ghMesh)    return ghMesh.Value;
if (geometryInput is GH_Brep      ghBrep)    return ghBrep.Value;
if (geometryInput is GH_Circle    ghCircle)  return ghCircle.Value;
if (geometryInput is GH_Arc       ghArc)     return ghArc.Value;
if (geometryInput is GH_Line      ghLine)    return ghLine.Value;
if (geometryInput is GH_Rectangle ghRect)    return ghRect.Value;
if (geometryInput is GH_Box       ghBox)     return ghBox.Value;

// Generic Goo fallback
if (geometryInput is IGH_Goo goo) return goo.ScriptVariable();

// Already raw
if (geometryInput is GeometryBase || geometryInput is Point3d) return geometryInput;

return null;
```

**Reuse tip:** Copy this method verbatim for any component that receives geometry via `AddGeometryParameter` or `AddGenericParameter` and needs the underlying Rhino object.

---

### `TransformGeometryObject(object geometry, Transform transform)`

```csharp
private object TransformGeometryObject(object geometry, Transform transform)
```

**Returns:** Transformed geometry as `object`, or throws for unsupported types.

**Dispatch logic:**

```csharp
// Value types — use reflection-based TransformStruct<T>
if (geometry is Point3d)             return TransformStruct((Point3d)geometry,             transform);
if (geometry is Rectangle3d)         return TransformStruct((Rectangle3d)geometry,         transform);
if (geometry is Plane)               return TransformStruct((Plane)geometry,               transform);
if (geometry is Vector3d)            return TransformStruct((Vector3d)geometry,            transform);
if (geometry is Rhino.Geometry.Circle)  return TransformStruct((Circle)geometry,           transform);
if (geometry is Rhino.Geometry.Arc)     return TransformStruct((Arc)geometry,              transform);
if (geometry is Rhino.Geometry.Line)    return TransformStruct((Line)geometry,             transform);
if (geometry is Rhino.Geometry.Box)     return TransformStruct((Box)geometry,              transform);

// Reference types — duplicate and transform in-place
if (geometry is GeometryBase geomBase)
{
    GeometryBase copy = geomBase.Duplicate();
    copy.Transform(transform);
    return copy;
}

throw new InvalidOperationException($"Unsupported geometry type: {geometry.GetType().Name}");
```

---

### `TransformStruct<T>(T geometry, Transform transform)`

```csharp
private T TransformStruct<T>(T geometry, Transform transform) where T : struct
```

**Why reflection is needed:** C# structs are value types. Calling `.Transform()` on a local copy would discard the result. By boxing the struct and invoking via reflection on the boxed object, the mutation is preserved before unboxing.

```csharp
object boxed = geometry;                                          // box
var method   = typeof(T).GetMethod("Transform", new[] { typeof(Transform) });
if (method == null) throw new InvalidOperationException(...);
method.Invoke(boxed, new object[] { transform });                // mutate boxed
return (T)boxed;                                                 // unbox
```

**Reuse tip:** This pattern works for any Rhino struct that has a `void Transform(Transform xform)` method — `Point3d`, `Plane`, `Line`, `Arc`, `Circle`, `Rectangle3d`, `Box`, `Vector3d`.

---

### `ExtractTeklaViewCS(object teklaViewObject)`

```csharp
private TSG.CoordinateSystem ExtractTeklaViewCS(object teklaViewObject)
```

**Returns:** `TSG.CoordinateSystem` (Tekla `Geometry3d` namespace).

**Unwrapping tiers:**

| Tier | Check | Action |
|------|-------|--------|
| 1 | `is GH_ObjectWrapper` | `.Value` → unwrap |
| 2 | Has `.Value` property (via reflection) | `.GetValue()` → unwrap Goo |
| 3 | `is Tekla.Structures.Drawing.View` | Direct `ExtractFromTeklaView()` |
| Fallback | Any type | `ExtractViaReflection()` |

---

### `ExtractFromTeklaView(View teklaView)`

```csharp
private TSG.CoordinateSystem ExtractFromTeklaView(View teklaView)
```

```csharp
// Priority 1
TSG.CoordinateSystem viewCS = teklaView.ViewCoordinateSystem;
if (viewCS != null) return viewCS;   // Remark: "Using ViewCoordinateSystem"

// Priority 2
TSG.CoordinateSystem displayCS = teklaView.DisplayCoordinateSystem;
if (displayCS != null) return displayCS;  // Remark: "Using DisplayCoordinateSystem"

throw new InvalidOperationException("Both ViewCoordinateSystem and DisplayCoordinateSystem are null");
```

---

### `ExtractViaReflection(object unwrapped)`

```csharp
private TSG.CoordinateSystem ExtractViaReflection(object unwrapped)
```

Used when the object cannot be directly cast to `View` (e.g., wrapped in a custom Goo type).

```csharp
// Try "ViewCoordinateSystem" property
var prop = unwrapped.GetType().GetProperty("ViewCoordinateSystem");
if (prop != null && prop.GetValue(unwrapped) is TSG.CoordinateSystem cs) return cs;

// Try "DisplayCoordinateSystem" property
var prop2 = unwrapped.GetType().GetProperty("DisplayCoordinateSystem");
if (prop2 != null && prop2.GetValue(unwrapped) is TSG.CoordinateSystem cs2) return cs2;

throw new InvalidOperationException($"Cannot find coordinate system on type: {viewType.FullName}");
```

---

### `ConvertTeklaCSToRhinoPlane(TSG.CoordinateSystem teklaCS)`

```csharp
private Plane ConvertTeklaCSToRhinoPlane(TSG.CoordinateSystem teklaCS)
```

```csharp
Point3d  origin = new Point3d (teklaCS.Origin.X, teklaCS.Origin.Y, teklaCS.Origin.Z);
Vector3d xAxis  = new Vector3d(teklaCS.AxisX.X,  teklaCS.AxisX.Y,  teklaCS.AxisX.Z);
Vector3d yAxis  = new Vector3d(teklaCS.AxisY.X,  teklaCS.AxisY.Y,  teklaCS.AxisY.Z);
return new Plane(origin, xAxis, yAxis);
```

**Reuse tip:** Use this exact mapping in any component that reads a `TSG.CoordinateSystem` and needs to work with Rhino geometry.

---

### `NormalizeViewPlane(Plane viewPlane)`

```csharp
private Plane NormalizeViewPlane(Plane viewPlane)
```

Unitizes X and Y axes to remove floating-point scaling before the transform is built.

```csharp
Vector3d xAxis = viewPlane.XAxis; xAxis.Unitize();
Vector3d yAxis = viewPlane.YAxis; yAxis.Unitize();
return new Plane(viewPlane.Origin, xAxis, yAxis);
```

---

### `ValidateNormalizedPlane(Plane plane)`

```csharp
private bool ValidateNormalizedPlane(Plane plane)
```

```csharp
bool xIsUnit       = Math.Abs(plane.XAxis.Length - 1.0) < VALIDATION_TOLERANCE;
bool yIsUnit       = Math.Abs(plane.YAxis.Length - 1.0) < VALIDATION_TOLERANCE;
bool isPerpendicular = Math.Abs(plane.XAxis * plane.YAxis) < VALIDATION_TOLERANCE;
return xIsUnit && yIsUnit && isPerpendicular;
```

Returns `false` → Warning is added but the transform still runs.

---

### `PlanesAreEqual(Plane plane1, Plane plane2)`

```csharp
private bool PlanesAreEqual(Plane plane1, Plane plane2)
```

Used only when both View and Plane inputs are connected.

```csharp
// 1. Origin distance
if (plane1.Origin.DistanceTo(plane2.Origin) >= PLANE_TOLERANCE) return false;

// 2. Axis comparison (unitized, allows flipped directions)
Vector3d x1 = plane1.XAxis; x1.Unitize();
Vector3d x2 = plane2.XAxis; x2.Unitize();
// ... same for Y and Z

bool xMatch = (x1 - x2).Length < PLANE_TOLERANCE || (x1 + x2).Length < PLANE_TOLERANCE;
bool yMatch = (y1 - y2).Length < PLANE_TOLERANCE || (y1 + y2).Length < PLANE_TOLERANCE;
bool zMatch = (z1 - z2).Length < PLANE_TOLERANCE || (z1 + z2).Length < PLANE_TOLERANCE;

return xMatch && yMatch && zMatch;
```

**Note:** Opposite-direction axes (`x1 + x2 ≈ 0`) are considered matching, which handles mirrored views.

---

## 7. Runtime Messages

| Level | Condition |
|-------|-----------|
| `Error` | `DA.GetData(2)` fails — no geometry connected |
| `Error` | Neither View nor Plane is connected |
| `Error` | `geometryInput` is null after getting data |
| `Error` | `ExtractGeometryFromWrapper` returns null |
| `Error` | `DetermineWorkingPlane` throws (invalid view, mismatched planes) |
| `Error` | `TryGetInverse` fails — degenerate transform |
| `Error` | `TransformGeometryObject` returns null |
| `Warning` | Plane axes are not perpendicular or normalized |
| `Remark` | Using input Plane (View is null) |
| `Remark` | Tier 1: Unwrapped GH_ObjectWrapper |
| `Remark` | Tier 2: Unwrapped Goo |
| `Remark` | Direct cast to Tekla View succeeded |
| `Remark` | Using ViewCoordinateSystem |
| `Remark` | Using DisplayCoordinateSystem |
| `Remark` | View and Plane match — using extracted Plane |

---

## 8. Template — Building a Similar Component

Use this checklist when creating a new component that transforms geometry between coordinate systems.

### Step 1 — Class skeleton

```csharp
public class MyTransformComponent : GH_Component
{
    private const double PLANE_TOLERANCE      = 0.001;
    private const double VALIDATION_TOLERANCE = 0.01;

    public MyTransformComponent()
        : base(
            "MyTransform",            // display name
            "MYT",                    // nickname
            "Description",            // description
            "Mäkeläinen automation",  // category tab
            "Geometry"                // subcategory
        ) { }

    public override Guid ComponentGuid => new Guid("/* generate a new GUID */");
    protected override System.Drawing.Bitmap Icon => Resources.MyIcon;
    public override GH_Exposure Exposure => GH_Exposure.primary;
}
```

### Step 2 — Inputs

```csharp
protected override void RegisterInputParams(GH_InputParamManager pManager)
{
    // Option A: Tekla object source
    pManager.AddGenericParameter("View",     "View", "Tekla View",           GH_ParamAccess.item);
    // Option B: Direct plane source
    pManager.AddPlaneParameter  ("Plane",    "Pln",  "Source plane",         GH_ParamAccess.item);
    // Required geometry
    pManager.AddGeometryParameter("Geometry","G",    "Geometry to transform", GH_ParamAccess.item);

    pManager[0].Optional = true;
    pManager[1].Optional = true;
}
```

### Step 3 — Outputs

```csharp
protected override void RegisterOutputParams(GH_OutputParamManager pManager)
{
    pManager.AddGeometryParameter("TransGeo", "TG", "Transformed geometry", GH_ParamAccess.item);
}
```

### Step 4 — SolveInstance pattern

```csharp
protected override void SolveInstance(IGH_DataAccess DA)
{
    object viewObj   = null;
    Plane  plane     = Plane.Unset;
    object geoInput  = null;

    bool hasView  = DA.GetData(0, ref viewObj);
    bool hasPlane = DA.GetData(1, ref plane);
    if (!DA.GetData(2, ref geoInput)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "..."); return; }

    if (!hasView && !hasPlane) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "..."); return; }

    object raw = ExtractGeometryFromWrapper(geoInput);
    if (raw == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "..."); return; }

    try
    {
        Plane sourcePlane    = DetermineWorkingPlane(viewObj, hasView, plane, hasPlane);
        Plane normalizedPlane = NormalizeViewPlane(sourcePlane);
        if (!ValidateNormalizedPlane(normalizedPlane))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Plane not normalized");

        // Choose transform direction:
        // View → World:   Transform.PlaneToPlane(Plane.WorldXY, normalizedPlane) then invert
        // World → View:   Transform.PlaneToPlane(Plane.WorldXY, normalizedPlane) (no invert)
        Transform xform = Transform.PlaneToPlane(Plane.WorldXY, normalizedPlane);
        if (!xform.TryGetInverse(out Transform inverse)) { AddRuntimeMessage(...); return; }

        object result = TransformGeometryObject(raw, inverse);
        if (result == null) { AddRuntimeMessage(...); return; }

        DA.SetData(0, result);
    }
    catch (Exception ex)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
    }
}
```

### Step 5 — Reusable methods (copy directly)

| Method | Purpose | Notes |
|--------|---------|-------|
| `ExtractGeometryFromWrapper` | Unwrap GH wrappers to raw Rhino | Copy verbatim |
| `TransformGeometryObject` | Apply `Transform` to any geometry type | Copy verbatim |
| `TransformStruct<T>` | Transform value-type structs via reflection | Copy verbatim |
| `ExtractTeklaViewCS` | Unwrap view object to `TSG.CoordinateSystem` | Copy verbatim |
| `ExtractFromTeklaView` | Read CS from a known `View` type | Copy verbatim |
| `ExtractViaReflection` | Read CS via reflection for unknown wrappers | Copy verbatim |
| `ConvertTeklaCSToRhinoPlane` | `TSG.CoordinateSystem` → Rhino `Plane` | Copy verbatim |
| `NormalizeViewPlane` | Unitize plane axes | Copy verbatim |
| `ValidateNormalizedPlane` | Check unit-length + perpendicularity | Copy verbatim |
| `PlanesAreEqual` | Compare two planes with tolerance | Copy verbatim |

### Step 6 — Choose transform direction

| Goal | Transform to use |
|------|-----------------|
| **View/Plane → World XY** (this component) | `PlaneToPlane(WorldXY, viewPlane)` → `TryGetInverse` |
| **World XY → View/Plane** | `PlaneToPlane(WorldXY, viewPlane)` directly (no invert) |
| **Plane A → Plane B** | `PlaneToPlane(planeA, planeB)` |

### Step 7 — Key rules

- Always `Unitize()` axes before building the transform to avoid scaling artefacts.
- Always call `TryGetInverse()` — never assume a plane-to-plane transform is invertible.
- Always wrap the transform block in `try/catch` and emit `Error` messages instead of crashing.
- Always use `ExtractGeometryFromWrapper` before passing geometry to transform — raw types differ from GH wrapper types.
- Always use reflection (`TransformStruct<T>`) for Rhino value-type structs; they cannot be mutated through the normal reference path.
- Use `GH_ParamAccess.item` (not `.tree`) for single-item geometry transforms; upgrade to `.tree` + branch loop when batch processing is needed.
