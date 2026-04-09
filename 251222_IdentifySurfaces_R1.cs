using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;

namespace SurfaceClassification
{
    /// <summary>
    /// Surface Classifier - Phân loại Inner/Outer surfaces
    /// Version: Giữ nguyên Input/Output gốc + Fix tất cả bugs
    /// </summary>
    public class SurfaceClassifier : GH_Component
    {
        public SurfaceClassifier()
          : base(
              "Identify Surfaces",
              "SrfClass",
              "Specified Inner/Outer surfaces",
              "Hannu Automation",
              "Geometry"
          )
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// INPUT PARAMETERS - ĐÚNG Y HỆT SCRIPT GỐC
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Input 0: BREP (Data Tree)
            pManager.AddBrepParameter(
                "BREP",
                "BREP",
                "3D Brep to analyse",
                GH_ParamAccess.tree
            );

            // Input 1: InnerPoint
            pManager.AddPointParameter(
                "Reference Point",
                "Point",
                "Inner Reference Point",
                GH_ParamAccess.item
            );

            // Input 2: LongtitudeLine
            pManager.AddCurveParameter(
                "LongtitudeLine",
                "LongtitudeLine",
                "Longtitude Line of Brep",
                GH_ParamAccess.item
            );

            // Input 3: ExcludeVector
            pManager.AddVectorParameter(
                "ExcludeVector",
                "ExcludeVector",
                "Vector",
                GH_ParamAccess.item
            );
        }

        /// <summary>
        /// OUTPUT PARAMETERS - ĐÚNG Y HỆT SCRIPT GỐC
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Output 0: InnerSurface (Data Tree)
            pManager.AddSurfaceParameter(
                "InnerSurface",
                "InnerSurface",
                "Inner Surface",
                GH_ParamAccess.tree
            );

            // Output 1: OuterSurface (Data Tree)
            pManager.AddSurfaceParameter(
                "OuterSurface",
                "OuterSurface",
                "Outer Surface",
                GH_ParamAccess.tree
            );

            // Output 2: Vector (Data Tree)
            pManager.AddVectorParameter(
                "Vector",
                "Vector",
                "Direction vectors",
                GH_ParamAccess.tree
            );
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════
            // KHAI BÁO BIẾN - ĐÚNG Y HỆT SCRIPT GỐC
            // ═══════════════════════════════════════════════════════

            GH_Structure<GH_Brep> BREP = null;
            Point3d InnerPoint = Point3d.Unset;
            Curve LongtitudeLine = null;
            Vector3d ExcludeVector = Vector3d.Unset;

            // ═══════════════════════════════════════════════════════
            // LẤY INPUT
            // ═══════════════════════════════════════════════════════

            if (!DA.GetDataTree(0, out BREP)) return;
            if (!DA.GetData(1, ref InnerPoint)) return;
            if (!DA.GetData(2, ref LongtitudeLine)) return;
            if (!DA.GetData(3, ref ExcludeVector)) return;

            // ═══════════════════════════════════════════════════════
            // FIX #2: VALIDATE INPUT (tránh crash)
            // ═══════════════════════════════════════════════════════

            if (LongtitudeLine == null || !LongtitudeLine.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Invalid Longitude Line");
                return;
            }

            if (!InnerPoint.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Invalid InnerPoint");
                return;
            }

            // ═══════════════════════════════════════════════════════
            // OUTPUT TREES - ĐÚNG Y HỆT SCRIPT GỐC
            // ═══════════════════════════════════════════════════════

            DataTree<Surface> InnerSurface = new DataTree<Surface>();
            DataTree<Surface> OuterSurface = new DataTree<Surface>();
            DataTree<Vector3d> Vector = new DataTree<Vector3d>();

            // ═══════════════════════════════════════════════════════
            // SETUP DIRECTIONS - ĐÚNG Y HỆT SCRIPT GỐC
            // ═══════════════════════════════════════════════════════

            Vector3d longDir = LongtitudeLine.TangentAtStart;
            longDir.Unitize();

            Vector3d excludeDir = ExcludeVector;
            if (excludeDir == Vector3d.Zero)
            {
                excludeDir = Vector3d.ZAxis;
            }
            excludeDir.Unitize();

            // ═══════════════════════════════════════════════════════
            // FIX #1: KIỂM TRA SONG SONG (tránh Zero Vector)
            // ═══════════════════════════════════════════════════════

            double parallelCheck = Math.Abs(Vector3d.Multiply(longDir, excludeDir));
            if (parallelCheck > 0.99)
            {
                // Vectors gần song song → tạo vector vuông góc
                excludeDir = Vector3d.CrossProduct(longDir, Vector3d.ZAxis);
                if (excludeDir.Length < 0.001)
                {
                    excludeDir = Vector3d.XAxis;
                }
                excludeDir.Unitize();
            }

            double LONG_THRESHOLD = 0.3;
            double EXCLUDE_THRESHOLD = 0.9;

            // ═══════════════════════════════════════════════════════
            // LOOP QUA BRANCHES - ĐÚNG Y HỆT SCRIPT GỐC
            // ═══════════════════════════════════════════════════════

            foreach (GH_Path path in BREP.Paths)
            {
                List<GH_Brep> branchBreps = BREP[path];  // FIX #6: Syntax

                foreach (GH_Brep ghBrep in branchBreps)
                {
                    Brep brep = ghBrep.Value;
                    if (brep == null || !brep.IsValid) continue;

                    // ═══════════════════════════════════════════════════════
                    // FILTER SURFACES - ĐÚNG Y HỆT SCRIPT GỐC
                    // ═══════════════════════════════════════════════════════

                    List<Surface> validSurfaces = new List<Surface>();

                    foreach (BrepFace face in brep.Faces)
                    {
                        Surface srf = face.ToNurbsSurface();
                        if (srf == null) continue;

                        double u = srf.Domain(0).Mid;
                        double v = srf.Domain(1).Mid;
                        Vector3d normal = srf.NormalAt(u, v);
                        normal.Unitize();

                        double dotLong = Math.Abs(Vector3d.Multiply(normal, longDir));
                        double dotExclude = Math.Abs(Vector3d.Multiply(normal, excludeDir));

                        if (dotLong < LONG_THRESHOLD && dotExclude < EXCLUDE_THRESHOLD)
                        {
                            validSurfaces.Add(srf);
                        }
                    }

                    // ═══════════════════════════════════════════════════════
                    // FIX #3: CHECK COUNT (tránh output trống im lặng)
                    // ═══════════════════════════════════════════════════════

                    if (validSurfaces.Count < 2)
                    {
                        continue;  // Bỏ qua branch này
                    }

                    // ═══════════════════════════════════════════════════════
                    // CLASSIFY INNER/OUTER - VỚI FIX #5
                    // ═══════════════════════════════════════════════════════

                    var result = ClassifyInnerOuterSurfaces(validSurfaces, InnerPoint);

                    if (result == null)
                    {
                        continue;  // Không thể classify → bỏ qua
                    }

                    Surface innerSrf = result.Item1;
                    Surface outerSrf = result.Item2;

                    // ═══════════════════════════════════════════════════════
                    // TÍNH VECTOR - ĐÚNG Y HỆT SCRIPT GỐC
                    // ═══════════════════════════════════════════════════════

                    Point3d innerCenter = innerSrf.PointAt(
                        innerSrf.Domain(0).Mid,
                        innerSrf.Domain(1).Mid
                    );

                    Vector3d toPoint = InnerPoint - innerCenter;
                    Vector3d perpVector = Vector3d.CrossProduct(longDir, excludeDir);

                    // ═══════════════════════════════════════════════════════
                    // FIX #1: CHECK ZERO VECTOR (tránh NaN)
                    // ═══════════════════════════════════════════════════════

                    if (perpVector.Length < 0.001)
                    {
                        perpVector = Vector3d.XAxis;
                    }
                    else
                    {
                        perpVector.Unitize();
                    }

                    double dotCheck = Vector3d.Multiply(perpVector, toPoint);
                    if (dotCheck < 0)
                    {
                        perpVector = -perpVector;
                    }

                    // ═══════════════════════════════════════════════════════
                    // OUTPUT - ĐÚNG Y HỆT SCRIPT GỐC
                    // ═══════════════════════════════════════════════════════

                    InnerSurface.Add(innerSrf, path);
                    OuterSurface.Add(outerSrf, path);
                    Vector.Add(perpVector, path);
                }
            }

            // ═══════════════════════════════════════════════════════
            // SET OUTPUT - ĐÚNG Y HỆT SCRIPT GỐC
            // ═══════════════════════════════════════════════════════

            DA.SetDataTree(0, InnerSurface);
            DA.SetDataTree(1, OuterSurface);
            DA.SetDataTree(2, Vector);
        }

        #region HELPER METHODS

        /// <summary>
        /// FIX #5: Phân loại Inner/Outer với validation đầy đủ
        /// Tránh crash khi distances.Count = 0
        /// </summary>
        private Tuple<Surface, Surface, double, double> ClassifyInnerOuterSurfaces(
            List<Surface> surfaces,
            Point3d referencePoint)
        {
            if (surfaces.Count < 2) return null;

            // ═══════════════════════════════════════════════════════
            // FIX #5: Dùng Dictionary + Try-Catch để handle exceptions
            // ═══════════════════════════════════════════════════════

            Dictionary<int, double> validDistances = new Dictionary<int, double>();

            for (int i = 0; i < surfaces.Count; i++)
            {
                Surface srf = surfaces[i];

                try
                {
                    double u, v;
                    if (!srf.ClosestPoint(referencePoint, out u, out v))
                    {
                        continue;
                    }

                    Point3d closestPt = srf.PointAt(u, v);

                    if (!closestPt.IsValid)
                    {
                        continue;
                    }

                    double dist = referencePoint.DistanceTo(closestPt);

                    if (double.IsNaN(dist) || double.IsInfinity(dist))
                    {
                        continue;
                    }

                    validDistances[i] = dist;
                }
                catch
                {
                    continue;
                }
            }

            // ═══════════════════════════════════════════════════════
            // FIX #5: CHECK EMPTY LIST trước khi Min/Max
            // ═══════════════════════════════════════════════════════

            if (validDistances.Count == 0)
            {
                return null;  // Không có surface hợp lệ
            }

            if (validDistances.Count < 2)
            {
                return null;  // Cần ít nhất 2 surfaces
            }

            // Giờ an toàn rồi
            double minDist = validDistances.Values.Min();
            double maxDist = validDistances.Values.Max();

            int minIdx = validDistances.First(kvp => kvp.Value == minDist).Key;
            int maxIdx = validDistances.First(kvp => kvp.Value == maxDist).Key;

            Surface innerSrf = surfaces[minIdx];
            Surface outerSrf = surfaces[maxIdx];

            return Tuple.Create(innerSrf, outerSrf, minDist, maxDist);
        }

        #endregion

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                //return Resources.IconForThisComponent;
                return Resources.IdentifySurfaces;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("F8029F3C-A60D-4AD2-A126-F34F1640EF18"); }
        }
    }
}