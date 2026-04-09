# Sort XYZ — `260131_Sort_XYZ.cs`

---

## 1. Tổng quan

| Trường | Giá trị |
|--------|---------|
| Tên component | Sort XYZ |
| Tên viết tắt | `SrtXYZ` |
| Danh mục | Mäkeläinen automation → Others |
| GUID | `217E719C-5BDA-4076-AEDB-559917278A13` |
| Class | `SortByCentroidComponent` |
| Class gốc | `GH_Component` |

**Chức năng:** Nhận các đối tượng hình học (Keys) và dữ liệu liên kết tùy chọn (Values), tính tâm (centroid) của từng hình học, và xuất ra ba danh sách được sắp xếp độc lập — một cho mỗi trục (X, Y, Z). Mỗi đầu ra trục chứa tọa độ tâm đã sắp xếp (dưới dạng số) và các values tương ứng đã sắp xếp.

---

## 2. Lưu đồ (Flowchart)

```
┌─────────────────────────────────────────────────────────┐
│                      SolveInstance                       │
└─────────────────────────────────────────────────────────┘
                           │
              ┌────────────▼────────────┐
              │   DA.GetDataTree(Keys)  │
              └────────────┬────────────┘
                           │ THẤT BẠI?
                    Có ────┘  └──── Không
                    │                  │
             [Error + return]          │
                               ┌───────▼────────────────────┐
                               │  DA.GetDataTree(Values)    │
                               │  (tùy chọn, null nếu       │
                               │   không kết nối)            │
                               └───────┬────────────────────┘
                                       │
                            ┌──────────▼─────────┐
                            │  keysTree rỗng?    │
                            └──────────┬─────────┘
                                 Có ───┘  └── Không
                                 │                │
                          [Warning + return]      │
                                         ┌────────▼──────────────────────┐
                                         │  Khởi tạo 6 DataTree đầu ra  │
                                         │  keysX/Y/Z, valuesX/Y/Z      │
                                         └────────┬──────────────────────┘
                                                  │
                                   ┌──────────────▼──────────────────┐
                                   │  FOR mỗi nhánh trong keysTree  │
                                   └──────────────┬──────────────────┘
                                                  │
                                       ┌──────────▼──────────┐
                                       │  GetValuesBranch()  │
                                       │  (khớp hoặc dùng   │
                                       │   keys làm dự phòng)│
                                       └──────────┬──────────┘
                                                  │
                                       ┌──────────▼──────────┐
                                       │  Kích thước lệch?  │
                                       │  → cắt về min,     │
                                       │    thêm Remark     │
                                       └──────────┬──────────┘
                                                  │
                                       ┌──────────▼──────────┐
                                       │  ExtractCentroids() │
                                       │  Với mỗi hình học: │
                                       │  ┌────────────────┐ │
                                       │  │ Kiểm tra 1-5   │ │
                                       │  │ (GH_Point,     │ │
                                       │  │  GH_Line, ...)  │ │
                                       │  └───────┬────────┘ │
                                       │          │ không tìm thấy
                                       │  ┌───────▼────────┐ │
                                       │  │ CastTo 6-9     │ │
                                       │  │ (ép kiểu dự    │ │
                                       │  │  phòng)        │ │
                                       │  └───────┬────────┘ │
                                       │          │ không tìm thấy
                                       │  ┌───────▼────────┐ │
                                       │  │ ScriptVariable │ │
                                       │  │ bước 10        │ │
                                       │  └───────┬────────┘ │
                                       │          │           │
                                       │  ┌───────▼────────┐ │
                                       │  │ Làm tròn 2 dp  │ │
                                       │  │ AwayFromZero   │ │
                                       │  └───────┬────────┘ │
                                       │          │           │
                                       │  ┌───────▼────────┐ │
                                       │  │ Thêm vào list  │ │
                                       │  │ (bỏ qua nếu   │ │
                                       │  │  không hợp lệ) │ │
                                       │  └────────────────┘ │
                                       └──────────┬──────────┘
                                                  │
                                       ┌──────────▼──────────┐
                                       │ centroids rỗng?    │
                                       └──────────┬──────────┘
                                            Có ───┘  └── Không
                                            │                │
                                     [Warning, bỏ qua]      │
                                                    ┌────────▼──────────────────┐
                                                    │  ProcessAxisSorting()    │
                                                    │  ┌─────────────────────┐ │
                                                    │  │ SortByAxis(X)       │ │
                                                    │  │ OrderBy(X)          │ │
                                                    │  │ .ThenBy(index)      │ │
                                                    │  │ → keysX, valuesX   │ │
                                                    │  ├─────────────────────┤ │
                                                    │  │ SortByAxis(Y)       │ │
                                                    │  │ → keysY, valuesY   │ │
                                                    │  ├─────────────────────┤ │
                                                    │  │ SortByAxis(Z)       │ │
                                                    │  │ → keysZ, valuesZ   │ │
                                                    │  └─────────────────────┘ │
                                                    └────────┬──────────────────┘
                                                             │
                                                    [Nhánh tiếp theo]
                                                             │
                                   ┌─────────────────────────▼───────────────────────┐
                                   │  DA.SetDataTree (0..5)                           │
                                   │  KX, VX, KY, VY, KZ, VZ                         │
                                   └─────────────────────────────────────────────────┘
```

### Lưu đồ phụ `GetAccurateCentroid()`

```
Đầu vào: GeometryBase geo
              │
        ┌─────▼──────┐   Có   ┌──────────────────────┐
        │ là Point?  ├───────►│ return .Location     │
        └─────┬──────┘        └──────────────────────┘
              │ Không
        ┌─────▼──────┐   Có   ┌──────────────────────┐
        │ là Line    ├───────►│ return PointAt(0.5)  │
        │ Curve?     │        └──────────────────────┘
        └─────┬──────┘
              │ Không
        ┌─────▼──────────┐
        │ là Polyline    │
        │ Curve?         │
        └────┬───────┬───┘
           đóng    mở
             │       └──► return PointAt(domain.Mid)
             ▼
        AreaMassProperties.Compute()
             │
        ┌─────▼──────┐   Có   ┌────────────────────────┐
        │ là Curve?  ├───────►│ đóng → AreaMass        │
        └─────┬──────┘        │ mở   → domain.Mid      │
              │ Không         └────────────────────────┘
        ┌─────▼──────┐
        │ là Brep?   │
        └────┬───────┘
          đặc      bề mặt
            │           └──► AreaMassProperties → dự phòng bbox
            ▼
        VolumeMassProperties → dự phòng AreaMass → dự phòng bbox
             │
        ┌─────▼──────┐
        │ là Surface?├───────► AreaMassProperties → dự phòng domain.Mid
        └─────┬──────┘
              │
        ┌─────▼──────┐
        │ là Mesh?   │
        └────┬───────┘
          đóng     mở
            │           └──► AreaMassProperties → dự phòng bbox
            ▼
        VolumeMassProperties → dự phòng AreaMass → dự phòng bbox
             │
        ┌─────▼──────────┐   Có   ┌─────────────────────────────┐
        │ là Extrusion?  ├───────►│ ToBrep() → gọi đệ quy      │
        └─────┬──────────┘        └─────────────────────────────┘
              │ Không
        ┌─────▼────────────────────┐
        │ Dự phòng cuối:           │
        │ GetBoundingBox().Center  │
        └──────────────────────────┘
```

---

## 3. Đầu vào & Đầu ra

### Đầu vào (Inputs)

| # | Tên | Viết tắt | Kiểu | Truy cập | Bắt buộc |
|---|-----|----------|------|----------|----------|
| 0 | Keys | K | Geometry | Tree | Có |
| 1 | Values | V | Generic | Tree | Không |

**Keys** — bất kỳ loại hình học nào: Point, Line, Polyline, Rectangle, Vector, Curve, Brep, Mesh, Extrusion, Surface.
**Values** — bất kỳ dữ liệu nào liên kết với từng hình học. Nếu bỏ trống, chính hình học sẽ được dùng làm value.

### Đầu ra (Outputs)

| # | Tên | Viết tắt | Kiểu | Mô tả |
|---|-----|----------|------|-------|
| 0 | KeysX | KX | Number Tree | Tọa độ X của tâm, sắp xếp tăng dần |
| 1 | ValuesX | VX | Generic Tree | Values sắp xếp theo X |
| 2 | KeysY | KY | Number Tree | Tọa độ Y của tâm, sắp xếp tăng dần |
| 3 | ValuesY | VY | Generic Tree | Values sắp xếp theo Y |
| 4 | KeysZ | KZ | Number Tree | Tọa độ Z của tâm, sắp xếp tăng dần |
| 5 | ValuesZ | VZ | Generic Tree | Values sắp xếp theo Z |

---

## 4. Ví dụ

### Ví dụ A — Điểm (Points)

**Đầu vào:** 4 điểm tại các vị trí ngẫu nhiên, ký hiệu A–D.

```
A = (3.0, 1.0, 0.0)
B = (1.0, 4.0, 2.0)
C = (2.0, 2.0, 5.0)
D = (4.0, 3.0, 1.0)
```

Tâm = chính vị trí điểm đó (không cần tính toán).

**Sắp xếp theo X** (tăng dần):
```
KX = [1.0, 2.0, 3.0, 4.0]
VX = [B,   C,   A,   D  ]
```

**Sắp xếp theo Y** (tăng dần):
```
KY = [1.0, 2.0, 3.0, 4.0]
VY = [A,   C,   D,   B  ]
```

**Sắp xếp theo Z** (tăng dần):
```
KZ = [0.0, 1.0, 2.0, 5.0]
VZ = [A,   D,   B,   C  ]
```

> Ứng dụng: sắp xếp danh sách điểm thu hút từ trái sang phải, từ dưới lên trên.

---

### Ví dụ B — Bề mặt (Surfaces)

**Đầu vào:** 3 bề mặt phẳng (Brep face) ở các độ cao khác nhau.

```
S1 = bề mặt phẳng, tâm tại (0.0,  0.0, 0.0), kích thước 2×2 m
S2 = bề mặt phẳng, tâm tại (5.0,  0.0, 3.0), kích thước 2×2 m
S3 = bề mặt phẳng, tâm tại (2.5,  0.0, 6.0), kích thước 2×2 m
```

Tâm được tính bằng `AreaMassProperties.Compute()`.

**Sắp xếp theo Z** (thứ tự tầng — từ dưới lên):
```
KZ = [0.0,  3.0,  6.0]
VZ = [S1,   S2,   S3 ]
```

> Ứng dụng: đánh số các tấm sàn từ mặt đất lên, tự động gán nhãn tầng.

---

### Ví dụ C — Đường cong (Curves)

**Đầu vào:** 3 đường cong đóng (hình tròn) trên mặt phẳng XY.

```
C1 = hình tròn, tâm (4.0, 0.0, 0.0), r=1
C2 = hình tròn, tâm (1.0, 0.0, 0.0), r=1
C3 = hình tròn, tâm (7.0, 0.0, 0.0), r=1
```

Đường cong đóng → tâm qua `AreaMassProperties.Compute()` = tâm hình tròn.

**Sắp xếp theo X** (trái sang phải):
```
KX = [1.0, 4.0, 7.0]
VX = [C2,  C1,  C3 ]
```

**Ví dụ đường cong mở:**
```
L1 = cung từ (0,0,0) đến (6,0,0)  → tâm = PointAt(domain.Mid) ≈ (3.0, y, 0)
L2 = cung từ (0,0,0) đến (2,0,0)  → tâm ≈ (1.0, y, 0)
```

> Ứng dụng: sắp xếp các đường profile trước khi loft để hướng loft có thể đoán trước được.

---

### Ví dụ D — Hình học hỗn hợp (Brep + Mesh + Curve)

**Đầu vào:** danh sách không đồng nhất trong một nhánh.

```
G1 = Brep hộp,    tâm bbox ≈ (1.0, 1.0, 0.5)  → VolumeMassProperties
G2 = Mesh vòm,   mesh đóng → VolumeMassProperties → tâm ≈ (5.0, 1.0, 1.0)
G3 = NurbsCurve, mở        → PointAt(domain.Mid) → điểm giữa ≈ (3.0, 2.0, 0.0)
```

**Sắp xếp theo X:**
```
KX = [1.0,  3.0,  5.0]
VX = [G1,   G3,   G2 ]
```

> Ứng dụng: sắp xếp các cấu kiện kết cấu (cột = Brep, dầm = curve, tấm = mesh) theo vị trí X để đánh số thứ tự.

---

### Ví dụ E — DataTree (nhiều nhánh)

**Cây đầu vào:**
```
{0;0} → [A(1,0,0), B(3,0,0)]
{0;1} → [C(2,0,0), D(0,0,0)]
```

**Cây KX đầu ra (sắp xếp theo X):**
```
{0;0} → [1.0, 3.0]   VX → [A, B]
{0;1} → [0.0, 2.0]   VX → [D, C]
```

Mỗi nhánh được sắp xếp độc lập. Đường dẫn nhánh được giữ nguyên.

---

## 5. Tham chiếu Class

### `SortByCentroidComponent`

```csharp
public class SortByCentroidComponent : GH_Component
```

Class component chính. Toàn bộ logic nằm trong đây.

#### Constructor

```csharp
public SortByCentroidComponent()
    : base(
        "Sort XYZ",               // tên hiển thị
        "SrtXYZ",                 // tên viết tắt
        "Sort geometry objects…", // mô tả
        "Mäkeläinen automation",  // tab danh mục
        "Others"                  // danh mục con
    )
```

#### Các thuộc tính ghi đè quan trọng

```csharp
public override Guid ComponentGuid
    => new Guid("217E719C-5BDA-4076-AEDB-559917278A13");

protected override System.Drawing.Bitmap Icon
    => Resources.Sort_XYZ;

public override GH_Exposure Exposure
    => GH_Exposure.primary;
```

---

## 6. Tham chiếu Phương thức

### `RegisterInputParams(GH_InputParamManager pManager)`

```csharp
protected override void RegisterInputParams(GH_InputParamManager pManager)
```

Khai báo hai tham số đầu vào.

```csharp
// Chỉ mục 0 — Keys (bắt buộc)
pManager.AddGeometryParameter("Keys", "K", "...", GH_ParamAccess.tree);

// Chỉ mục 1 — Values (tùy chọn)
pManager.AddGenericParameter("Values", "V", "...", GH_ParamAccess.tree);
pManager[1].Optional = true;
```

**Quy tắc:** Luôn dùng `GH_ParamAccess.tree` khi component phải xử lý DataTree. Đánh dấu tham số tùy chọn bằng `pManager[n].Optional = true` để tắt lỗi "no data".

---

### `RegisterOutputParams(GH_OutputParamManager pManager)`

```csharp
protected override void RegisterOutputParams(GH_OutputParamManager pManager)
```

Khai báo sáu tham số đầu ra (chỉ mục 0–5):

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

Phương thức thực thi chính. Grasshopper gọi mỗi khi đầu vào thay đổi.

```csharp
// Đọc dữ liệu
DA.GetDataTree(0, out GH_Structure<IGH_GeometricGoo> keysTree);
DA.GetDataTree(1, out GH_Structure<IGH_Goo> valuesTree); // null nếu không kết nối

// Khởi tạo đầu ra
var keysXTree   = new DataTree<double>();
var valuesXTree = new DataTree<object>();
// ... lặp lại cho Y, Z

// Xử lý từng nhánh
for (int i = 0; i < keysTree.PathCount; i++)
{
    GH_Path path = keysTree.Paths[i];
    List<IGH_GeometricGoo> keysBranch = keysTree.get_Branch(path);

    List<object> valuesBranch = GetValuesBranch(valuesTree, path, keysBranch);
    // cắt bớt nếu kích thước lệch...
    var (centroids, validValues) = ExtractCentroids(keysBranch, valuesBranch);
    ProcessAxisSorting(centroids, validValues, path, ...6 cây...);
}

// Ghi kết quả
DA.SetDataTree(0, keysXTree);
DA.SetDataTree(1, valuesXTree);
// ... chỉ mục 2-5
```

---

### `GetValuesBranch(...)`

```csharp
private List<object> GetValuesBranch(
    GH_Structure<IGH_Goo> valuesTree,    // cây values đầy đủ (có thể null)
    GH_Path path,                         // nhánh cần tìm
    List<IGH_GeometricGoo> keysBranch)   // nguồn dự phòng
```

**Trả về:** `List<object>` — values cho nhánh.

**Logic quyết định:**
```
valuesTree == null                         → trả về keys dưới dạng object
valuesTree không chứa path                → trả về keys dưới dạng object
nhánh tại path là null hoặc rỗng          → trả về keys dưới dạng object
trường hợp còn lại                        → v.ScriptVariable() cho mỗi mục
```

**Gợi ý tái sử dụng:** Sao chép phương thức này nguyên vẹn cho bất kỳ component nào có đầu vào values tùy chọn.

---

### `ExtractCentroids(...)`

```csharp
private (List<Point3d> centroids, List<object> values) ExtractCentroids(
    List<IGH_GeometricGoo> geometries,
    List<object> values)
```

**Trả về:** Hai danh sách song song — chỉ bao gồm các mục tìm được tâm hợp lệ.

**Chuỗi phát hiện (10 bước):**

```csharp
// Bước 1-5: kiểm tra kiểu trực tiếp
if (geo is GH_Point)      → centroid = ghPoint.Value
if (geo is GH_Line)       → centroid = line.PointAt(0.5)
if (geo is GH_Rectangle)  → centroid = rect.Center
if (geo is GH_Curve)      → centroid = GetAccurateCentroid(curve)
if (geo is GH_Vector)     → centroid = new Point3d(v.X/2, v.Y/2, v.Z/2)

// Bước 6-9: ép kiểu dự phòng CastTo
geo.CastTo(out GH_Point ghPt)      → centroid = ghPt.Value
geo.CastTo(out GH_Rectangle ghRc)  → centroid = ghRc.Value.Center
geo.CastTo(out GH_Curve ghCv)      → centroid = GetAccurateCentroid(ghCv.Value)
geo.CastTo(out GH_Vector ghVc)     → centroid = new Point3d(v.X/2, ...)

// Bước 10: trích xuất thô qua ScriptVariable
object raw = geo.ScriptVariable();
// xử lý: Point3d, Line, Rectangle3d, Polyline, Vector3d, Curve, GeometryBase
```

**Làm tròn (áp dụng một lần, trước khi thêm vào danh sách):**
```csharp
centroid = new Point3d(
    Math.Round(centroid.X, 2, MidpointRounding.AwayFromZero),
    Math.Round(centroid.Y, 2, MidpointRounding.AwayFromZero),
    Math.Round(centroid.Z, 2, MidpointRounding.AwayFromZero)
);
// Nếu value là Point3d, cũng làm tròn tương tự
```

---

### `GetAccurateCentroid(GeometryBase geo)`

```csharp
private Point3d GetAccurateCentroid(GeometryBase geo)
```

**Trả về:** `Point3d` tâm, hoặc `Point3d.Unset` nếu thất bại. Được bao bọc trong `try/catch`.

**Bảng phân loại đầy đủ:**

```csharp
// Point
if (geo is Rhino.Geometry.Point)    → return point.Location

// Line
if (geo is LineCurve)               → return lineCurve.Line.PointAt(0.5)

// Polyline
if (geo is PolylineCurve)
    đóng → AreaMassProperties.Compute(polyCurve).Centroid
    mở   → polyCurve.PointAt(domain.Mid)

// Curve tổng quát
if (geo is Curve)
    đóng → AreaMassProperties.Compute(crv).Centroid
    mở   → crv.PointAt(domain.Mid)

// Brep
if (geo is Brep)
    khối đặc  → VolumeMassProperties.Compute(brep).Centroid
    bề mặt   → AreaMassProperties.Compute(brep).Centroid
    dự phòng → brep.GetBoundingBox(true).Center

// Surface
if (geo is Surface)
    → AreaMassProperties.Compute(srf).Centroid
    dự phòng → srf.PointAt(u.Mid, v.Mid)

// Mesh
if (geo is Mesh)
    đóng    → VolumeMassProperties.Compute(mesh).Centroid
    mở      → AreaMassProperties.Compute(mesh).Centroid
    dự phòng → mesh.GetBoundingBox(true).Center

// Extrusion
if (geo is Extrusion)
    → ext.ToBrep() rồi GetAccurateCentroid(brep)  // đệ quy

// Dự phòng cuối
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

Gọi `SortByAxis()` ba lần và thêm kết quả vào các cây đầu ra.

```csharp
var sortedX = SortByAxis(centroids, values, Axis.X);
keysXTree.AddRange(sortedX.coordinates, path);
valuesXTree.AddRange(sortedX.sortedValues, path);
// lặp lại cho Y, Z
```

---

### `SortByAxis(...)`

```csharp
private (List<double> coordinates, List<object> sortedValues) SortByAxis(
    List<Point3d> centroids,
    List<object> values,
    Axis axis)
```

**Trả về:** Tuple gồm danh sách tọa độ double đã sắp xếp và danh sách values đã sắp xếp.

```csharp
// Tạo các cặp với chỉ mục gốc
List<CentroidValuePair> pairs = centroids.Select((c, i) => new CentroidValuePair
{
    Centroid = c, Value = values[i], OriginalIndex = i
}).ToList();

// Sắp xếp — ổn định qua ThenBy(OriginalIndex)
sortedPairs = pairs.OrderBy(p => p.Centroid.X)   // hoặc .Y / .Z
                   .ThenBy(p => p.OriginalIndex)
                   .ToList();

// Trích xuất
coordinates  = sortedPairs.Select(p => GetCoordinate(p.Centroid, axis)).ToList();
sortedValues = sortedPairs.Select(p => p.Value).ToList();
```

**Các trường hợp biên:**
- Đầu vào rỗng → trả về hai danh sách rỗng ngay lập tức.
- Một mục duy nhất → trả về ngay không cần sắp xếp.

---

### `GetCoordinate(Point3d point, Axis axis)`

```csharp
private double GetCoordinate(Point3d point, Axis axis)
// Axis.X → point.X
// Axis.Y → point.Y
// Axis.Z → point.Z
// mặc định → 0.0
```

Bộ phân phối switch đơn giản. Được dùng bên trong `SortByAxis()` để trích xuất tọa độ đích sau khi sắp xếp.

---

## 7. Các kiểu Helper

### `enum Axis`

```csharp
private enum Axis { X = 0, Y = 1, Z = 2 }
```

Tránh dùng số ma thuật khi chọn trục sắp xếp. Truyền vào `SortByAxis()` và `GetCoordinate()`.

---

### `class CentroidValuePair`

```csharp
private class CentroidValuePair
{
    public Point3d Centroid      { get; set; }
    public object  Value         { get; set; }
    public int     OriginalIndex { get; set; }
}
```

Giữ tâm, value, và chỉ mục vị trí ban đầu gắn liền với nhau trong quá trình sắp xếp LINQ.
`OriginalIndex` là tiebreaker để sắp xếp ổn định — khi hai tâm có cùng tọa độ trên trục sắp xếp, thứ tự đầu vào ban đầu được giữ nguyên.

---

## 8. Thông báo Runtime

| Mức độ | Điều kiện kích hoạt |
|--------|---------------------|
| `Error` | `DA.GetDataTree(0)` thất bại |
| `Warning` | `keysTree` null hoặc rỗng |
| `Warning` | Không có hình học hợp lệ trong nhánh |
| `Warning` | Exception trong `GetAccurateCentroid()` |
| `Remark` | Kích thước nhánh Keys và Values lệch nhau — đã cắt về ngắn hơn |

---

## 9. Mẫu — Tạo component tương tự

Dùng danh sách kiểm tra này khi tạo component mới sắp xếp hoặc sắp xếp lại hình học theo bất kỳ giá trị vô hướng nào.

### Bước 1 — Khung class

```csharp
public class MyNewSortComponent : GH_Component
{
    public MyNewSortComponent()
        : base("My Sort", "MySort", "Mô tả", "Danh mục", "Danh mục con") { }

    public override Guid ComponentGuid => new Guid("/* tạo GUID mới */");
    protected override Bitmap Icon     => Resources.MyIcon;
    public override GH_Exposure Exposure => GH_Exposure.primary;
}
```

### Bước 2 — Đầu vào

```csharp
protected override void RegisterInputParams(GH_InputParamManager pManager)
{
    pManager.AddGeometryParameter("Keys",   "K", "...", GH_ParamAccess.tree);
    pManager.AddGenericParameter ("Values", "V", "...", GH_ParamAccess.tree);
    pManager[1].Optional = true;
}
```

### Bước 3 — Đầu ra

```csharp
// Mỗi tiêu chí sắp xếp có một cặp (key + value)
pManager.AddNumberParameter ("KeysX",   "KX", "...", GH_ParamAccess.tree);
pManager.AddGenericParameter("ValuesX", "VX", "...", GH_ParamAccess.tree);
```

### Bước 4 — Mẫu SolveInstance

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

        // thay bằng logic trích xuất scalar của bạn
        var scalars = keysBranch.Select(g => ComputeScalar(g)).ToList();

        // sắp xếp
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

### Bước 5 — Thay tâm bằng scalar của bạn

| Mục tiêu sắp xếp | Biểu thức scalar |
|-------------------|-----------------|
| Theo vị trí X | `GetAccurateCentroid(geo).X` |
| Theo vị trí Y | `GetAccurateCentroid(geo).Y` |
| Theo vị trí Z | `GetAccurateCentroid(geo).Z` |
| Theo diện tích bề mặt | `AreaMassProperties.Compute(brep).Area` |
| Theo thể tích | `VolumeMassProperties.Compute(brep).Volume` |
| Theo độ dài đường cong | `curve.GetLength()` |
| Theo khoảng cách đến điểm | `centroid.DistanceTo(referencePoint)` |
| Theo kích thước bounding box | `bbox.Diagonal.Length` |

### Bước 6 — Các quy tắc quan trọng cần tuân thủ

- Luôn làm tròn scalar trước khi sắp xếp để tránh nhiễu dấu phẩy động.
- Luôn dùng class dữ liệu (như `CentroidValuePair`) để giữ keys và values căn chỉnh — không bao giờ sắp xếp hai danh sách riêng biệt độc lập.
- Luôn dùng `ThenBy(OriginalIndex)` làm tiebreaker để đảm bảo đầu ra ổn định.
- Luôn dùng `GH_ParamAccess.tree` và vòng lặp `PathCount` để hỗ trợ nhiều nhánh.
- Bọc các phép tính hình học trong `try/catch` và phát `Warning` thay vì crash.
