# ViewToWorldXY — `251221_ViewToWorldXY_R1.cs`

---

## 1. Tổng quan

| Trường | Giá trị |
|--------|---------|
| Tên Component | `TransformToDrawingView` |
| Nickname | `V→XY` |
| Danh mục | Mäkeläinen automation → Geometry |
| GUID | `27EE8E6F-1A42-4595-8D08-7D878FC9328F` |
| Class | `ViewCSToWorldXY_TeklaViewInput` |
| Base Class | `GH_Component` |

**Chức năng:** Nhận một đối tượng Tekla Drawing View (hoặc Rhino Plane dự phòng) và bất kỳ geometry Rhino nào, trích xuất hệ tọa độ của view, xây dựng phép biến đổi ngược từ mặt phẳng view về World XY, và xuất ra geometry đã được định vị lại trong không gian World XY.

**Quyết định thiết kế:**
- Cả `View` và `Plane` đều là tùy chọn riêng lẻ, nhưng bắt buộc phải có ít nhất một trong hai.
- Nếu cả hai được cung cấp, mặt phẳng trích xuất và mặt phẳng nhập vào sẽ được kiểm tra khớp — không khớp sẽ báo lỗi.
- Geometry được tháo gói (unwrap) khỏi các kiểu bao bọc Grasshopper trước khi biến đổi và trả về dạng Rhino geometry thô.
- Các struct kiểu giá trị (`Point3d`, `Rectangle3d`, v.v.) được biến đổi thông qua reflection để cùng một phương thức xử lý được mọi kiểu geometry.

---

## 2. Sơ đồ luồng (Flowchart)

### `SolveInstance` — luồng cấp cao nhất

```
┌─────────────────────────────────────────────────────────────────┐
│                         SolveInstance                           │
└─────────────────────────────────────────────────────────────────┘
                               │
              ┌────────────────▼─────────────────┐
              │  DA.GetData(0, ref teklaViewObj) │  → hasView
              │  DA.GetData(1, ref inputPlane)   │  → hasPlane
              │  DA.GetData(2, ref geometryInput)│  → bắt buộc
              └────────────────┬─────────────────┘
                               │
                   ┌───────────▼───────────┐
                   │  thiếu geometry?      │
                   └───────────┬───────────┘
                         Có ───┘  └── Không
                         │                │
                  [Lỗi + return]          │
                                ┌─────────▼──────────────┐
                                │  !hasView && !hasPlane?│
                                └─────────┬──────────────┘
                                  Có ─────┘  └── Không
                                  │                │
                           [Lỗi + return]          │
                                         ┌─────────▼────────────────┐
                                         │  geometryInput == null?  │
                                         └─────────┬────────────────┘
                                           Có ─────┘  └── Không
                                           │                │
                                    [Lỗi + return]          │
                                                  ┌─────────▼──────────────────┐
                                                  │  ExtractGeometryFromWrapper│
                                                  │  (geometryInput)           │
                                                  └─────────┬──────────────────┘
                                                            │
                                              ┌─────────────▼─────────────┐
                                              │  rawGeometry == null?     │
                                              └─────────────┬─────────────┘
                                                    Có ─────┘  └── Không
                                                    │                │
                                             [Lỗi + return]         │
                                                           ┌─────────▼──────────────────┐
                                                           │  DetermineWorkingPlane()   │
                                                           └─────────┬──────────────────┘
                                                                     │
                                                          ┌──────────▼──────────────┐
                                                          │  NormalizeViewPlane()   │
                                                          └──────────┬──────────────┘
                                                                     │
                                                          ┌──────────▼──────────────────┐
                                                          │  ValidateNormalizedPlane()  │
                                                          └──────────┬──────────────────┘
                                                            hợp lệ? │
                                                             Không ──┘  └── Có
                                                             │               │
                                                      [Cảnh báo]            │
                                                             └───────────────▼
                                                                    ┌────────────────────────────┐
                                                                    │  Transform.PlaneToPlane    │
                                                                    │  (WorldXY → normalizedPlane│
                                                                    └────────┬───────────────────┘
                                                                             │
                                                                    ┌────────▼────────────────────┐
                                                                    │  TryGetInverse()            │
                                                                    └────────┬────────────────────┘
                                                                      lỗi ──┘  └── ok
                                                                      │               │
                                                               [Lỗi + return]        │
                                                                              ┌───────▼─────────────────┐
                                                                              │  TransformGeometryObject│
                                                                              │  (rawGeometry,          │
                                                                              │   inverseTransform)     │
                                                                              └───────┬─────────────────┘
                                                                                      │
                                                                             ┌────────▼─────────────┐
                                                                             │  result == null?     │
                                                                             └────────┬─────────────┘
                                                                               Có ────┘  └── Không
                                                                               │               │
                                                                        [Lỗi + return]  DA.SetData(0, result)
```

---

### `DetermineWorkingPlane` — sơ đồ con

```
Đầu vào: teklaViewObject, hasView, inputPlane, hasPlane
                │
    ┌───────────▼────────────────────┐
    │  !hasView || viewObject == null│
    └───────────┬────────────────────┘
          Có ───┘  └─────── Không
          │                 │
  ┌───────▼──────────┐      │
  │  hasPlane &&     │      │
  │  inputPlane hợp  │      │
  │  lệ?             │      │
  └───────┬──────────┘      │
    Không ┘  └── Có         │
    │          │            │
  [throw]  [Remark]         │
             │              │
         trả về inputPlane  │
                    ┌───────▼──────────────────────┐
                    │  ExtractTeklaViewCS(viewObj) │
                    └───────┬──────────────────────┘
                            │
                   ┌────────▼──────────────────────┐
                   │  ConvertTeklaCSToRhinoPlane() │
                   └────────┬──────────────────────┘
                            │
                ┌───────────▼───────────────────┐
                │  hasPlane && inputPlane hợp lệ│
                └───────────┬───────────────────┘
                      Không ┘  └── Có
                      │               │
                trả về            PlanesAreEqual()?
                extractedPlane      Không → [throw]
                                    Có → [Remark] → trả về extractedPlane
```

---

### `ExtractTeklaViewCS` — sơ đồ con

```
Đầu vào: teklaViewObject
          │
  ┌───────▼────────────────────┐
  │  là GH_ObjectWrapper?      │
  └───────┬────────────────────┘
    Có ───┘  └── Không
    │               │
  unwrap.Value    đã unwrap sẵn
    │               │
    └──────┬────────┘
           │
  ┌────────▼──────────────────────┐
  │  có thuộc tính .Value (Goo)? │  ← reflection
  └────────┬──────────────────────┘
     Có ───┘  └── Không
     │               │
  unwrap tiếp    tiếp tục
     │               │
     └──────┬────────┘
            │
   ┌────────▼──────────────────┐
   │  là Tekla.Drawing.View?   │  ← ép kiểu trực tiếp
   └────────┬──────────────────┘
      Có ───┘  └── Không
      │               │
  ExtractFromTeklaView()    ExtractViaReflection()
      │                         │
      └────────────┬────────────┘
                   │
             trả về TSG.CoordinateSystem
```

---

### `ExtractFromTeklaView` — sơ đồ con

```
Đầu vào: View teklaView
          │
  ┌───────▼────────────────────────┐
  │  teklaView.ViewCoordinateSystem│
  └───────┬────────────────────────┘
    != null──┘  └── null
    │                   │
  trả về viewCS   ┌─────▼─────────────────────────────┐
                  │  teklaView.DisplayCoordinateSystem│
                  └─────┬─────────────────────────────┘
                  != null──┘  └── null
                  │                   │
                trả về displayCS   [throw]
```

---

## 3. Đầu vào & Đầu ra

### Đầu vào

| # | Tên | Nickname | Kiểu | Truy cập | Bắt buộc |
|---|-----|----------|------|----------|----------|
| 0 | View | View | Generic | Item | Không (tùy chọn) |
| 1 | Plane | Pln | Plane | Item | Không (tùy chọn) |
| 2 | Geometry | G | Geometry | Item | **Có** |

**View** — Đối tượng Tekla Drawing `View` (truyền dưới dạng `GH_ObjectWrapper` hoặc Goo bao bọc). Dùng để trích xuất `ViewCoordinateSystem` hoặc `DisplayCoordinateSystem`.

**Plane** — Rhino `Plane` dùng làm hệ tọa độ nguồn khi không có View, hoặc để kiểm tra chéo khi có View.

**Geometry** — Bất kỳ kiểu geometry Rhino nào: Point, Curve, Line, Arc, Circle, Surface, Brep, Mesh, Rectangle, Box, Plane, Vector.

> Ít nhất một trong hai **View** hoặc **Plane** phải được kết nối.

### Đầu ra

| # | Tên | Nickname | Kiểu | Mô tả |
|---|-----|----------|------|-------|
| 0 | TransGeo | TG | Geometry | Geometry đã biến đổi từ hệ tọa độ View/Plane sang World XY |

---

## 4. Ví dụ

### Ví dụ A — Biến đổi điểm từ mặt phẳng View nghiêng về World XY

**Thiết lập:**
- Mặt phẳng View: gốc tại `(10, 5, 0)`, trục X hướng 45° trong XY, trục Y hướng 135°.
- Điểm nhập vào (trong không gian world, biểu diễn trong khung cục bộ của view): `(1, 0, 0)` tính từ gốc view dọc theo trục X của view.

**Trong tọa độ world, điểm nằm tại xấp xỉ:** `(10.71, 5.71, 0)`.

Sau khi áp dụng nghịch đảo của `PlaneToPlane(WorldXY → viewPlane)`:

```
TransGeo = (1.0, 0.0, 0.0)
```

Geometry bây giờ được biểu diễn trong World XY — độ nghiêng của view đã được "hoàn tác".

> Trường hợp dùng: kích thước hoặc đối tượng chú thích được tạo trong view bản vẽ nghiêng cần được kiểm tra hoặc xử lý trong không gian World XY phẳng.

---

### Ví dụ B — Dự phòng dùng Plane (không có View)

**Thiết lập:**
- Đầu vào View: không kết nối.
- Đầu vào Plane: `origin=(5,5,0), X=(1,0,0), Y=(0,1,0)` (giống WorldXY, lệch 5,5).
- Geometry: hình chữ nhật với các góc tại `(6,6,0)` và `(9,9,0)`.

Vì mặt phẳng khớp với WorldXY về hướng, phép biến đổi ngược chỉ là tịnh tiến thuần túy.

```
TransGeo = hình chữ nhật dịch (-5, -5, 0)
         → các góc tại (1,1,0) và (4,4,0)
```

> Trường hợp dùng: geometry từ hệ tọa độ cục bộ đã tịnh tiến cần được so sánh với tham chiếu toàn cục.

---

### Ví dụ C — Cả View và Plane đều kết nối (kiểm tra hợp lệ)

**Thiết lập:**
- Đầu vào View: Tekla View với `ViewCoordinateSystem` tại `origin=(0,0,3)`, các trục căn chỉnh theo World.
- Đầu vào Plane: `origin=(0,0,3), X=(1,0,0), Y=(0,1,0)`.

Cả hai mô tả cùng một mặt phẳng → `PlanesAreEqual()` trả về `true`.

**Thông báo Remark:** `"View and Plane match - using extracted Plane"`

**Kết quả:** Geometry được biến đổi bởi nghịch đảo của mặt phẳng view. Điểm tại `(2,3,5)` world → `(2,3,2)` trong World XY (loại bỏ lệch Z).

---

### Ví dụ D — Không khớp gây ra lỗi

**Thiết lập:**
- Mặt phẳng View: `origin=(0,0,0)`, trục X dọc theo world X.
- Đầu vào Plane: `origin=(10,0,0)`, trục X dọc theo world X.

`PlanesAreEqual()` trả về `false` — các gốc tọa độ khác nhau hơn `PLANE_TOLERANCE (0.001)`.

**Thông báo lỗi:** `"Invalid View: Extracted Plane does not match input Plane"`

Không có đầu ra được tạo ra.

> Trường hợp dùng: hoạt động như kiểm tra an toàn tích hợp khi tự động hóa việc chọn view bản vẽ.

---

### Ví dụ E — Mặt phẳng không vuông góc (cảnh báo, không phải lỗi)

**Thiết lập:**
- Mặt phẳng nhập vào có các trục không chính xác đơn vị do tích lũy dấu phẩy động từ một script.

Sau khi `NormalizeViewPlane()` unitize cả hai trục, `ValidateNormalizedPlane()` kiểm tra:
- `|XAxis| ≈ 1.0` trong phạm vi `VALIDATION_TOLERANCE (0.01)` ✓
- `|YAxis| ≈ 1.0` ✓
- `XAxis · YAxis < 0.01` (tích vô hướng gần bằng không = vuông góc) ✓

Nếu bất kỳ kiểm tra nào thất bại: **Cảnh báo** `"Plane axes are not perpendicular or normalized. Results may be inaccurate."` — quá trình biến đổi vẫn tiếp tục.

---

## 5. Tham chiếu Class

### `ViewCSToWorldXY_TeklaViewInput`

```csharp
public class ViewCSToWorldXY_TeklaViewInput : GH_Component
```

Class component chính.

#### Constructor

```csharp
public ViewCSToWorldXY_TeklaViewInput()
    : base(
        "TransformToDrawingView",                   // tên hiển thị
        "V→XY",                                     // nickname
        "Transform geometry from Tekla Plane to World XY", // mô tả
        "Mäkeläinen automation",                    // tab danh mục
        "Geometry"                                  // danh mục con
    )
```

#### Các Override quan trọng

```csharp
public override Guid ComponentGuid
    => new Guid("27EE8E6F-1A42-4595-8D08-7D878FC9328F");

protected override System.Drawing.Bitmap Icon
    => Resources.ToDrawingCoordinate;

public override GH_Exposure Exposure
    => GH_Exposure.primary;
```

#### Hằng số

```csharp
private const double PLANE_TOLERANCE      = 0.001;  // so sánh gốc & trục
private const double VALIDATION_TOLERANCE = 0.01;   // kiểm tra độ dài đơn vị & vuông góc
```

---

## 6. Tham chiếu Method

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
// index 2 là bắt buộc — không có flag Optional
```

**Quy tắc:** Dùng `AddGenericParameter` cho các đối tượng Tekla vì chúng được truyền dưới dạng `GH_ObjectWrapper`. Đánh dấu đầu vào Tekla và Plane là Optional để component có thể chạy chỉ với một trong hai.

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

Điều phối toàn bộ pipeline. Các bước theo thứ tự:

```csharp
// 1. Đọc đầu vào
object teklaViewObject = null;
Plane  inputPlane      = Plane.Unset;
object geometryInput   = null;

bool hasView  = DA.GetData(0, ref teklaViewObject);
bool hasPlane = DA.GetData(1, ref inputPlane);

if (!DA.GetData(2, ref geometryInput)) { /* Lỗi */ return; }

// 2. Xác nhận ít nhất một nguồn tọa độ
if (!hasView && !hasPlane) { /* Lỗi */ return; }
if (geometryInput == null) { /* Lỗi */ return; }

// 3. Tháo gói geometry
object rawGeometry = ExtractGeometryFromWrapper(geometryInput);
if (rawGeometry == null) { /* Lỗi */ return; }

// 4. Xác định mặt phẳng làm việc
Plane workingPlane    = DetermineWorkingPlane(teklaViewObject, hasView, inputPlane, hasPlane);
Plane normalizedPlane = NormalizeViewPlane(workingPlane);
if (!ValidateNormalizedPlane(normalizedPlane)) { /* Cảnh báo — tiếp tục */ }

// 5. Xây dựng phép biến đổi ngược
Transform forward = Transform.PlaneToPlane(Plane.WorldXY, normalizedPlane);
if (!forward.TryGetInverse(out Transform inverse)) { /* Lỗi */ return; }

// 6. Áp dụng và xuất kết quả
object result = TransformGeometryObject(rawGeometry, inverse);
if (result == null) { /* Lỗi */ return; }
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

**Trả về:** `Plane` dùng làm hệ tọa độ nguồn.

**Bảng quyết định:**

| hasView / viewObj | hasPlane / hợp lệ | Hành động |
|---|---|---|
| false / null | true + hợp lệ | Trả về `inputPlane` + Remark |
| false / null | false / không hợp lệ | `throw InvalidOperationException("Empty View")` |
| true + khác null | — | `ExtractTeklaViewCS` → chuyển đổi → tùy chọn kiểm tra với `inputPlane` |
| true + khác null | true + hợp lệ | `PlanesAreEqual` → không khớp throw; khớp → Remark + trả về extracted |

---

### `ExtractGeometryFromWrapper(object geometryInput)`

```csharp
private object ExtractGeometryFromWrapper(object geometryInput)
```

**Trả về:** Rhino geometry thô (`GeometryBase` subclass hoặc struct `Point3d`), hoặc `null`.

**Chuỗi tháo gói:**

```csharp
// Kiểu bao bọc GH — ép kiểu trực tiếp
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

// Dự phòng Generic Goo
if (geometryInput is IGH_Goo goo) return goo.ScriptVariable();

// Đã là dạng thô
if (geometryInput is GeometryBase || geometryInput is Point3d) return geometryInput;

return null;
```

**Mẹo tái sử dụng:** Copy nguyên phương thức này cho bất kỳ component nào nhận geometry qua `AddGeometryParameter` hoặc `AddGenericParameter` và cần đối tượng Rhino gốc.

---

### `TransformGeometryObject(object geometry, Transform transform)`

```csharp
private object TransformGeometryObject(object geometry, Transform transform)
```

**Trả về:** Geometry đã biến đổi dạng `object`, hoặc throw cho các kiểu không hỗ trợ.

**Logic phân luồng:**

```csharp
// Kiểu giá trị — dùng TransformStruct<T> dựa trên reflection
if (geometry is Point3d)             return TransformStruct((Point3d)geometry,             transform);
if (geometry is Rectangle3d)         return TransformStruct((Rectangle3d)geometry,         transform);
if (geometry is Plane)               return TransformStruct((Plane)geometry,               transform);
if (geometry is Vector3d)            return TransformStruct((Vector3d)geometry,            transform);
if (geometry is Rhino.Geometry.Circle)  return TransformStruct((Circle)geometry,           transform);
if (geometry is Rhino.Geometry.Arc)     return TransformStruct((Arc)geometry,              transform);
if (geometry is Rhino.Geometry.Line)    return TransformStruct((Line)geometry,             transform);
if (geometry is Rhino.Geometry.Box)     return TransformStruct((Box)geometry,              transform);

// Kiểu tham chiếu — duplicate rồi biến đổi tại chỗ
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

**Tại sao cần reflection:** Struct trong C# là kiểu giá trị. Gọi `.Transform()` trên bản sao cục bộ sẽ loại bỏ kết quả. Bằng cách boxing struct và gọi qua reflection trên đối tượng đã box, sự thay đổi được giữ lại trước khi unbox.

```csharp
object boxed = geometry;                                          // boxing
var method   = typeof(T).GetMethod("Transform", new[] { typeof(Transform) });
if (method == null) throw new InvalidOperationException(...);
method.Invoke(boxed, new object[] { transform });                // thay đổi trên boxed
return (T)boxed;                                                 // unboxing
```

**Mẹo tái sử dụng:** Pattern này hoạt động cho bất kỳ struct Rhino nào có phương thức `void Transform(Transform xform)` — `Point3d`, `Plane`, `Line`, `Arc`, `Circle`, `Rectangle3d`, `Box`, `Vector3d`.

---

### `ExtractTeklaViewCS(object teklaViewObject)`

```csharp
private TSG.CoordinateSystem ExtractTeklaViewCS(object teklaViewObject)
```

**Trả về:** `TSG.CoordinateSystem` (namespace `Geometry3d` của Tekla).

**Các tầng tháo gói:**

| Tầng | Kiểm tra | Hành động |
|------|---------|-----------|
| 1 | `is GH_ObjectWrapper` | `.Value` → tháo gói |
| 2 | Có thuộc tính `.Value` (qua reflection) | `.GetValue()` → tháo gói Goo |
| 3 | `is Tekla.Structures.Drawing.View` | Trực tiếp `ExtractFromTeklaView()` |
| Dự phòng | Bất kỳ kiểu nào | `ExtractViaReflection()` |

---

### `ExtractFromTeklaView(View teklaView)`

```csharp
private TSG.CoordinateSystem ExtractFromTeklaView(View teklaView)
```

```csharp
// Ưu tiên 1
TSG.CoordinateSystem viewCS = teklaView.ViewCoordinateSystem;
if (viewCS != null) return viewCS;   // Remark: "Using ViewCoordinateSystem"

// Ưu tiên 2
TSG.CoordinateSystem displayCS = teklaView.DisplayCoordinateSystem;
if (displayCS != null) return displayCS;  // Remark: "Using DisplayCoordinateSystem"

throw new InvalidOperationException("Both ViewCoordinateSystem and DisplayCoordinateSystem are null");
```

---

### `ExtractViaReflection(object unwrapped)`

```csharp
private TSG.CoordinateSystem ExtractViaReflection(object unwrapped)
```

Dùng khi đối tượng không thể ép kiểu trực tiếp thành `View` (ví dụ: bị bao bọc trong kiểu Goo tùy chỉnh).

```csharp
// Thử thuộc tính "ViewCoordinateSystem"
var prop = unwrapped.GetType().GetProperty("ViewCoordinateSystem");
if (prop != null && prop.GetValue(unwrapped) is TSG.CoordinateSystem cs) return cs;

// Thử thuộc tính "DisplayCoordinateSystem"
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

**Mẹo tái sử dụng:** Dùng chính xác mapping này trong bất kỳ component nào đọc `TSG.CoordinateSystem` và cần làm việc với Rhino geometry.

---

### `NormalizeViewPlane(Plane viewPlane)`

```csharp
private Plane NormalizeViewPlane(Plane viewPlane)
```

Unitize các trục X và Y để loại bỏ scaling dấu phẩy động trước khi xây dựng phép biến đổi.

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
bool xIsUnit         = Math.Abs(plane.XAxis.Length - 1.0) < VALIDATION_TOLERANCE;
bool yIsUnit         = Math.Abs(plane.YAxis.Length - 1.0) < VALIDATION_TOLERANCE;
bool isPerpendicular = Math.Abs(plane.XAxis * plane.YAxis) < VALIDATION_TOLERANCE;
return xIsUnit && yIsUnit && isPerpendicular;
```

Trả về `false` → Cảnh báo được thêm nhưng phép biến đổi vẫn chạy.

---

### `PlanesAreEqual(Plane plane1, Plane plane2)`

```csharp
private bool PlanesAreEqual(Plane plane1, Plane plane2)
```

Chỉ dùng khi cả đầu vào View và Plane đều được kết nối.

```csharp
// 1. Khoảng cách gốc tọa độ
if (plane1.Origin.DistanceTo(plane2.Origin) >= PLANE_TOLERANCE) return false;

// 2. So sánh trục (đã unitize, cho phép hướng ngược)
Vector3d x1 = plane1.XAxis; x1.Unitize();
Vector3d x2 = plane2.XAxis; x2.Unitize();
// ... tương tự cho Y và Z

bool xMatch = (x1 - x2).Length < PLANE_TOLERANCE || (x1 + x2).Length < PLANE_TOLERANCE;
bool yMatch = (y1 - y2).Length < PLANE_TOLERANCE || (y1 + y2).Length < PLANE_TOLERANCE;
bool zMatch = (z1 - z2).Length < PLANE_TOLERANCE || (z1 + z2).Length < PLANE_TOLERANCE;

return xMatch && yMatch && zMatch;
```

**Lưu ý:** Các trục ngược chiều (`x1 + x2 ≈ 0`) được coi là khớp, xử lý được các view bị phản chiếu (mirror).

---

## 7. Thông báo Runtime

| Mức | Điều kiện |
|-----|-----------|
| `Error` | `DA.GetData(2)` thất bại — không có geometry kết nối |
| `Error` | Không có View hay Plane nào được kết nối |
| `Error` | `geometryInput` là null sau khi lấy dữ liệu |
| `Error` | `ExtractGeometryFromWrapper` trả về null |
| `Error` | `DetermineWorkingPlane` throw (view không hợp lệ, mặt phẳng không khớp) |
| `Error` | `TryGetInverse` thất bại — phép biến đổi suy biến |
| `Error` | `TransformGeometryObject` trả về null |
| `Warning` | Các trục mặt phẳng không vuông góc hoặc không chuẩn hóa |
| `Remark` | Đang dùng Plane nhập vào (View là null) |
| `Remark` | Tầng 1: Đã tháo gói GH_ObjectWrapper |
| `Remark` | Tầng 2: Đã tháo gói Goo |
| `Remark` | Ép kiểu trực tiếp thành Tekla View thành công |
| `Remark` | Đang dùng ViewCoordinateSystem |
| `Remark` | Đang dùng DisplayCoordinateSystem |
| `Remark` | View và Plane khớp nhau — dùng Plane đã trích xuất |

---

## 8. Template — Tạo Component Tương Tự

Dùng danh sách kiểm tra này khi tạo component mới biến đổi geometry giữa các hệ tọa độ.

### Bước 1 — Khung sườn Class

```csharp
public class MyTransformComponent : GH_Component
{
    private const double PLANE_TOLERANCE      = 0.001;
    private const double VALIDATION_TOLERANCE = 0.01;

    public MyTransformComponent()
        : base(
            "MyTransform",            // tên hiển thị
            "MYT",                    // nickname
            "Mô tả",                  // description
            "Mäkeläinen automation",  // tab danh mục
            "Geometry"                // danh mục con
        ) { }

    public override Guid ComponentGuid => new Guid("/* tạo GUID mới */");
    protected override System.Drawing.Bitmap Icon => Resources.MyIcon;
    public override GH_Exposure Exposure => GH_Exposure.primary;
}
```

### Bước 2 — Đầu vào

```csharp
protected override void RegisterInputParams(GH_InputParamManager pManager)
{
    // Lựa chọn A: nguồn đối tượng Tekla
    pManager.AddGenericParameter("View",     "View", "Tekla View",           GH_ParamAccess.item);
    // Lựa chọn B: nguồn mặt phẳng trực tiếp
    pManager.AddPlaneParameter  ("Plane",    "Pln",  "Mặt phẳng nguồn",     GH_ParamAccess.item);
    // Geometry bắt buộc
    pManager.AddGeometryParameter("Geometry","G",    "Geometry cần biến đổi", GH_ParamAccess.item);

    pManager[0].Optional = true;
    pManager[1].Optional = true;
}
```

### Bước 3 — Đầu ra

```csharp
protected override void RegisterOutputParams(GH_OutputParamManager pManager)
{
    pManager.AddGeometryParameter("TransGeo", "TG", "Geometry đã biến đổi", GH_ParamAccess.item);
}
```

### Bước 4 — Pattern SolveInstance

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
        Plane sourcePlane     = DetermineWorkingPlane(viewObj, hasView, plane, hasPlane);
        Plane normalizedPlane = NormalizeViewPlane(sourcePlane);
        if (!ValidateNormalizedPlane(normalizedPlane))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Plane chưa chuẩn hóa");

        // Chọn hướng biến đổi:
        // View → World:   Transform.PlaneToPlane(Plane.WorldXY, normalizedPlane) rồi invert
        // World → View:   Transform.PlaneToPlane(Plane.WorldXY, normalizedPlane) trực tiếp (không invert)
        Transform xform = Transform.PlaneToPlane(Plane.WorldXY, normalizedPlane);
        if (!xform.TryGetInverse(out Transform inverse)) { AddRuntimeMessage(...); return; }

        object result = TransformGeometryObject(raw, inverse);
        if (result == null) { AddRuntimeMessage(...); return; }

        DA.SetData(0, result);
    }
    catch (Exception ex)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Lỗi: {ex.Message}");
    }
}
```

### Bước 5 — Các phương thức tái sử dụng (copy trực tiếp)

| Phương thức | Mục đích | Ghi chú |
|-------------|---------|---------|
| `ExtractGeometryFromWrapper` | Tháo gói GH wrappers thành Rhino thô | Copy nguyên |
| `TransformGeometryObject` | Áp dụng `Transform` cho mọi kiểu geometry | Copy nguyên |
| `TransformStruct<T>` | Biến đổi struct kiểu giá trị qua reflection | Copy nguyên |
| `ExtractTeklaViewCS` | Tháo gói view object thành `TSG.CoordinateSystem` | Copy nguyên |
| `ExtractFromTeklaView` | Đọc CS từ kiểu `View` đã biết | Copy nguyên |
| `ExtractViaReflection` | Đọc CS qua reflection cho wrapper không biết | Copy nguyên |
| `ConvertTeklaCSToRhinoPlane` | `TSG.CoordinateSystem` → Rhino `Plane` | Copy nguyên |
| `NormalizeViewPlane` | Unitize các trục mặt phẳng | Copy nguyên |
| `ValidateNormalizedPlane` | Kiểm tra độ dài đơn vị + vuông góc | Copy nguyên |
| `PlanesAreEqual` | So sánh hai mặt phẳng với ngưỡng sai số | Copy nguyên |

### Bước 6 — Chọn hướng biến đổi

| Mục tiêu | Transform sử dụng |
|----------|------------------|
| **View/Plane → World XY** (component này) | `PlaneToPlane(WorldXY, viewPlane)` → `TryGetInverse` |
| **World XY → View/Plane** | `PlaneToPlane(WorldXY, viewPlane)` trực tiếp (không invert) |
| **Plane A → Plane B** | `PlaneToPlane(planeA, planeB)` |

### Bước 7 — Các quy tắc quan trọng

- Luôn `Unitize()` các trục trước khi xây dựng phép biến đổi để tránh hiện tượng scaling do lỗi dấu phẩy động.
- Luôn gọi `TryGetInverse()` — không bao giờ giả định phép biến đổi plane-to-plane là khả nghịch.
- Luôn bao bọc khối biến đổi trong `try/catch` và phát thông báo `Error` thay vì crash.
- Luôn dùng `ExtractGeometryFromWrapper` trước khi truyền geometry vào biến đổi — kiểu thô khác với kiểu bao bọc GH.
- Luôn dùng reflection (`TransformStruct<T>`) cho các struct kiểu giá trị Rhino; chúng không thể thay đổi qua đường tham chiếu thông thường.
- Dùng `GH_ParamAccess.item` (không phải `.tree`) cho biến đổi geometry đơn lẻ; nâng cấp lên `.tree` + vòng lặp branch khi cần xử lý hàng loạt.
