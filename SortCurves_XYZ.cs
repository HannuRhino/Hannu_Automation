using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;

namespace SortedLineByAxis
{
    /// <summary>
    /// SORT CURVES BY XYZ COMPONENT
    /// 
    /// 
    /// </summary>
    public class SortCurvesByXYZ : GH_Component
    {
        #region CONSTANTS

        private const double TOLERANCE_SQ = 0.998001;

        private const double MIN_LENGTH_SQ = 1e-12;

        #endregion

        #region METADATA & CONSTRUCTOR

        /// <summary>
        /// </summary>
        public SortCurvesByXYZ()
          : base(
              "Sort Curves by XYZ",
              "SortXYZ",
              "Sort and classify curves by X, Y, Z axis or Diagonal",
              "Hannu Automation",
              "Curves"
          )
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        #endregion

        #region INPUT PARAMETERS

        /// <summary>
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter(
                "Curves",
                "C",
                "List of Curves",
                GH_ParamAccess.tree
            );

            // INPUT 2: Sort by Length
            pManager.AddBooleanParameter(
                "By Length",
                "BL",
                "Sort curves by length",
                GH_ParamAccess.item,
                false
            );

            // INPUT 3: Sort by Vector
            pManager.AddBooleanParameter(
                "By Vector",
                "BV",
                "Sort curves by spatial position",
                GH_ParamAccess.item,
                false
            );
        }

        #endregion

        #region OUTPUT PARAMETERS

        /// <summary>
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves_X", "X", "Curves parallel to X axis", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Curves_Y", "Y", "Curves parallel to Y axis", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Curves_Z", "Z", "Curves parallel to Z axis", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Diagonal", "Dg", "Diagonal Curves", GH_ParamAccess.tree);
        }

        #endregion

        #region MAIN EXECUTION

        /// <summary>
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            GH_Structure<GH_Curve> inputTree;
            if (!DA.GetDataTree(0, out inputTree))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Empty Curves");
                return;
            }

            bool byLength = false;
            bool byVector = false;

            DA.GetData(1, ref byLength);
            DA.GetData(2, ref byVector);

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            if (byLength && byVector)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Invalid Input: Cannot use By Length and By Vector at the same time"
                );
                return;
            }

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            DataTree<Curve> treeX = new DataTree<Curve>();
            DataTree<Curve> treeY = new DataTree<Curve>();
            DataTree<Curve> treeZ = new DataTree<Curve>();
            DataTree<Curve> treeDiagonal = new DataTree<Curve>();

            int skippedCount = 0;

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            foreach (GH_Path path in inputTree.Paths)
            {
                List<GH_Curve> ghCurves = inputTree.get_Branch(path) as List<GH_Curve>;
                if (ghCurves == null) continue;

                foreach (GH_Curve ghCurve in ghCurves)
                {
                    if (ghCurve == null || ghCurve.Value == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    Curve curve = ghCurve.Value;

                    if (!curve.IsValid || curve.GetLength() < 1e-6)
                    {
                        skippedCount++;
                        continue;
                    }

                    Vector3d tangent = curve.TangentAtStart;

                    if (!tangent.IsValid)
                    {
                        skippedCount++;
                        continue;
                    }

                    double dx = tangent.X;
                    double dy = tangent.Y;
                    double dz = tangent.Z;

                    double lengthSq = dx * dx + dy * dy + dz * dz;

                    if (lengthSq < MIN_LENGTH_SQ)
                    {
                        skippedCount++;
                        continue;
                    }

                    double dotXSq = (dx * dx) / lengthSq;
                    double dotYSq = (dy * dy) / lengthSq;
                    double dotZSq = (dz * dz) / lengthSq;

                    double maxDotSq = Math.Max(dotXSq, Math.Max(dotYSq, dotZSq));

                    if (maxDotSq < TOLERANCE_SQ)
                    {
                        treeDiagonal.Add(curve, path);
                    }
                    else if (maxDotSq == dotXSq)
                    {
                        treeX.Add(curve, path);
                    }
                    else if (maxDotSq == dotYSq)
                    {
                        treeY.Add(curve, path);
                    }
                    else
                    {
                        treeZ.Add(curve, path);
                    }
                }
            }

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            if (byLength)
            {
                // Sort by Length
                treeX = SortTreeByLength(treeX);
                treeY = SortTreeByLength(treeY);
                treeZ = SortTreeByLength(treeZ);
                treeDiagonal = SortTreeByLength(treeDiagonal);
            }
            else if (byVector)
            {
                // Sort by Vector
                treeX = SortTreeByAxis(treeX, 'Y');  // X axis curves sort by Y
                treeY = SortTreeByAxis(treeY, 'X');  // Y axis curves sort by X
                treeZ = SortTreeByAxis(treeZ, 'Z');  // Z axis curves sort by Z
                // treeDiagonal remains empty (no sorting)
                treeDiagonal = new DataTree<Curve>();
            }

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            if (skippedCount > 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Skipped {skippedCount} curve(s)"
                );
            }

            DA.SetDataTree(0, treeX);
            DA.SetDataTree(1, treeY);
            DA.SetDataTree(2, treeZ);
            DA.SetDataTree(3, treeDiagonal);
        }

        #endregion

        #region HELPER METHODS

        /// <summary>
        /// </summary>
        private DataTree<Curve> SortTreeByLength(DataTree<Curve> tree)
        {
            DataTree<Curve> sortedTree = new DataTree<Curve>();

            foreach (GH_Path path in tree.Paths)
            {
                List<Curve> curves = tree.Branch(path);
                if (curves == null || curves.Count == 0) continue;

                var sortedCurves = curves.OrderBy(curve => curve.GetLength()).ToList();

                foreach (Curve curve in sortedCurves)
                {
                    sortedTree.Add(curve, path);
                }
            }

            return sortedTree;
        }

        /// <summary>
        /// </summary>
        private DataTree<Curve> SortTreeByAxis(DataTree<Curve> tree, char axis)
        {
            DataTree<Curve> sortedTree = new DataTree<Curve>();

            foreach (GH_Path path in tree.Paths)
            {
                List<Curve> curves = tree.Branch(path);
                if (curves == null || curves.Count == 0) continue;

                List<Curve> sortedCurves;

                switch (axis)
                {
                    case 'X':
                        sortedCurves = curves.OrderBy(curve =>
                        {
                            Point3d start = curve.PointAtStart;
                            Point3d end = curve.PointAtEnd;
                            return Math.Min(start.X, end.X);
                        }).ToList();
                        break;

                    case 'Y':
                        sortedCurves = curves.OrderBy(curve =>
                        {
                            Point3d start = curve.PointAtStart;
                            Point3d end = curve.PointAtEnd;
                            return Math.Min(start.Y, end.Y);
                        }).ToList();
                        break;

                    case 'Z':
                        sortedCurves = curves.OrderBy(curve =>
                        {
                            Point3d start = curve.PointAtStart;
                            Point3d end = curve.PointAtEnd;
                            return Math.Min(start.Z, end.Z);
                        }).ToList();
                        break;

                    default:
                        sortedCurves = curves;
                        break;
                }

                foreach (Curve curve in sortedCurves)
                {
                    sortedTree.Add(curve, path);
                }
            }

            return sortedTree;
        }

        #endregion

        #region COMPONENT METADATA

        /// <summary>
        /// Icon cho component
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Resources.SortCurve_XYZ;
            }
        }

        /// <summary>
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("F090FF1A-F5C5-4FB8-8BFB-7C94C9910A25"); }
        }

        #endregion
    }
}