using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;

namespace PointSorting
{
    public class SortPointsByVectorComponent : GH_Component
    {
        #region METADATA & CONSTRUCTOR

        /// <summary>
        /// Constructor
        /// </summary>
        public SortPointsByVectorComponent()
          : base(
              "Sort Points By Vector",
              "SortPts",
              "Sorted Points thought vector",
              "Hannu Automation",
              "Points"
          )
        {
        }
        #endregion

        #region INPUT PARAMETERS

        /// <summary>
        /// Register input parameters
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Input 0: Points - Accepts List or Tree
            pManager.AddPointParameter(
                "Points",
                "P",
                "Point List",
                GH_ParamAccess.tree
            );

            // Input 1: Vector - Direction vector
            pManager.AddVectorParameter(
                "Vector",
                "V",
                "Vector for sorting points",
                GH_ParamAccess.item
            );

            // Input 2: Tolerance - Floating point comparison tolerance
            pManager.AddNumberParameter(
                "Tolerance",
                "T",
                "Tolerance for floating point comparison",
                GH_ParamAccess.item,
                0.001  // Default value
            );
        }

        #endregion

        #region OUTPUT PARAMETERS

        /// <summary>
        /// Register output parameters
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Output 0: MinPoints
            pManager.AddPointParameter(
                "MinPoints",
                "Min",
                "Min Points List",
                GH_ParamAccess.tree
            );

            // Output 1: MaxPoints
            pManager.AddPointParameter(
                "MaxPoints",
                "Max",
                "Max Points List",
                GH_ParamAccess.tree
            );

            // Output 2: SortedPoints
            pManager.AddPointParameter(
                "SortedPoints",
                "Sorted",
                "Sorted Points list",
                GH_ParamAccess.tree
            );
        }

        #endregion

        #region MAIN EXECUTION

        /// <summary>
        /// Main execution method
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════
            // STEP 1: DECLARE VARIABLES
            // ═══════════════════════════════════════════════════════

            GH_Structure<GH_Point> pointsTree = new GH_Structure<GH_Point>();
            Vector3d vector = Vector3d.Unset;
            double tolerance = 0.001;

            // ═══════════════════════════════════════════════════════
            // STEP 2: GET INPUT DATA
            // ═══════════════════════════════════════════════════════

            if (!DA.GetDataTree(0, out pointsTree))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Empty Points");
                return;
            }

            if (!DA.GetData(1, ref vector))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Empty Vector");
                return;
            }

            if (!DA.GetData(2, ref tolerance))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Empty Tolerance");
                return;
            }

            // ═══════════════════════════════════════════════════════
            // STEP 3: VALIDATE INPUTS
            // ═══════════════════════════════════════════════════════

            // Check if vector is valid (not zero vector)
            double vectorMagnitude = vector.Length;
            if (vectorMagnitude < 1e-10)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Invalid Vector");
                return;
            }

            // Check if tolerance is valid (positive number)
            if (tolerance <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Tolerance must be greater than 0");
                return;
            }

            // Check if points tree is empty
            if (pointsTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Empty Points");

                // Set empty outputs
                DA.SetDataTree(0, new GH_Structure<GH_Point>());
                DA.SetDataTree(1, new GH_Structure<GH_Point>());
                DA.SetDataTree(2, new GH_Structure<GH_Point>());
                return;
            }

            // ═══════════════════════════════════════════════════════
            // STEP 4: PROCESS DATA
            // ═══════════════════════════════════════════════════════

            // Prepare output trees
            GH_Structure<GH_Point> minPointsTree = new GH_Structure<GH_Point>();
            GH_Structure<GH_Point> maxPointsTree = new GH_Structure<GH_Point>();
            GH_Structure<GH_Point> sortedPointsTree = new GH_Structure<GH_Point>();

            // Process each branch independently
            foreach (GH_Path path in pointsTree.Paths)
            {
                // Get points from current branch
                List<GH_Point> branchPoints = pointsTree.get_Branch(path) as List<GH_Point>;

                if (branchPoints == null || branchPoints.Count == 0)
                    continue;

                // Convert GH_Point to Point3d
                List<Point3d> points = branchPoints
                    .Select(ghPt => ghPt.Value)
                    .ToList();

                // ═══════════════════════════════════════════════════════
                // SPECIAL CASE: Single point - Min = Max = Sorted
                // ═══════════════════════════════════════════════════════
                if (points.Count == 1)
                {
                    Point3d singlePoint = points[0];

                    // Min = Max = Sorted = the single point
                    minPointsTree.AppendRange(
                        new List<GH_Point> { new GH_Point(singlePoint) },
                        path
                    );

                    maxPointsTree.AppendRange(
                        new List<GH_Point> { new GH_Point(singlePoint) },
                        path
                    );

                    sortedPointsTree.AppendRange(
                        new List<GH_Point> { new GH_Point(singlePoint) },
                        path
                    );

                    continue; // Skip to next branch
                }

                // Sort points by vector with custom tolerance
                var (minPoints, maxPoints, sortedPoints) = SortPointsByVector(points, vector, tolerance);

                // Add results to output trees with same path
                minPointsTree.AppendRange(
                    minPoints.Select(pt => new GH_Point(pt)),
                    path
                );

                maxPointsTree.AppendRange(
                    maxPoints.Select(pt => new GH_Point(pt)),
                    path
                );

                sortedPointsTree.AppendRange(
                    sortedPoints.Select(pt => new GH_Point(pt)),
                    path
                );
            }

            // ═══════════════════════════════════════════════════════
            // STEP 5: SET OUTPUT DATA
            // ═══════════════════════════════════════════════════════

            DA.SetDataTree(0, minPointsTree);
            DA.SetDataTree(1, maxPointsTree);
            DA.SetDataTree(2, sortedPointsTree);
        }

        #endregion

        #region HELPER METHODS

        /// <summary>
        /// Core algorithm: Sort points by vector projection
        /// </summary>
        /// <param name="points">List of points to sort</param>
        /// <param name="vector">Direction vector</param>
        /// <param name="tolerance">Tolerance for floating point comparison</param>
        /// <returns>Tuple of (minPoints, maxPoints, sortedPoints)</returns>
        private (List<Point3d>, List<Point3d>, List<Point3d>) SortPointsByVector(
            List<Point3d> points,
            Vector3d vector,
            double tolerance)
        {
            // Empty list check
            if (points == null || points.Count == 0)
            {
                return (new List<Point3d>(), new List<Point3d>(), new List<Point3d>());
            }

            // ═══════════════════════════════════════════════════════
            // STEP 1: NORMALIZE VECTOR
            // ═══════════════════════════════════════════════════════

            Vector3d vUnit = vector;
            vUnit.Unitize();  // Convert to unit vector

            // ═══════════════════════════════════════════════════════
            // STEP 2: CALCULATE DOT PRODUCT & DISTANCE
            // ═══════════════════════════════════════════════════════

            var pointData = new List<PointData>();

            for (int i = 0; i < points.Count; i++)
            {
                Point3d pt = points[i];

                // Calculate dot product (projection onto vector)
                double dotProduct = pt.X * vUnit.X + pt.Y * vUnit.Y + pt.Z * vUnit.Z;

                // Calculate distance from origin
                double distanceFromOrigin = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y + pt.Z * pt.Z);

                pointData.Add(new PointData
                {
                    Point = pt,
                    DotProduct = dotProduct,
                    Distance = distanceFromOrigin,
                    OriginalIndex = i
                });
            }

            // ═══════════════════════════════════════════════════════
            // STEP 3: FIND MIN/MAX DOT PRODUCT VALUES
            // ═══════════════════════════════════════════════════════

            double minDot = pointData.Min(pd => pd.DotProduct);
            double maxDot = pointData.Max(pd => pd.DotProduct);

            // ═══════════════════════════════════════════════════════
            // STEP 4: CLASSIFY POINTS INTO 3 GROUPS
            // ═══════════════════════════════════════════════════════

            var minGroup = new List<PointData>();
            var maxGroup = new List<PointData>();
            var middleGroup = new List<PointData>();

            foreach (var data in pointData)
            {
                bool isMin = Math.Abs(data.DotProduct - minDot) <= tolerance;
                bool isMax = Math.Abs(data.DotProduct - maxDot) <= tolerance;

                if (isMin)
                {
                    minGroup.Add(data);
                }

                if (isMax)
                {
                    maxGroup.Add(data);
                }

                // Only add to middle if NOT in min or max
                if (!isMin && !isMax)
                {
                    middleGroup.Add(data);
                }
            }

            // ═══════════════════════════════════════════════════════
            // STEP 5: SORT EACH GROUP
            // ═══════════════════════════════════════════════════════

            var sortedMinGroup = SortGroup(minGroup, tolerance);
            var sortedMaxGroup = SortGroup(maxGroup, tolerance);
            var sortedMiddleGroup = SortGroup(middleGroup, tolerance);

            // ═══════════════════════════════════════════════════════
            // STEP 6: EXTRACT POINTS FROM SORTED GROUPS
            // ═══════════════════════════════════════════════════════

            return (
                sortedMinGroup.Select(pd => pd.Point).ToList(),
                sortedMaxGroup.Select(pd => pd.Point).ToList(),
                sortedMiddleGroup.Select(pd => pd.Point).ToList()
            );
        }

        /// <summary>
        /// Sort a group of points:
        /// 1. Group by dot product (within tolerance)
        /// 2. Within each dot group, sort by distance from origin
        /// 3. Flatten groups in ascending dot product order
        /// </summary>
        private List<PointData> SortGroup(List<PointData> group, double tolerance)
        {
            if (group.Count == 0)
                return new List<PointData>();

            // Group points by dot product (within tolerance)
            var dotGroups = new Dictionary<double, List<PointData>>();

            foreach (var data in group)
            {
                // Round dot to tolerance level to group similar values
                double dotKey = Math.Round(data.DotProduct / tolerance) * tolerance;

                if (!dotGroups.ContainsKey(dotKey))
                {
                    dotGroups[dotKey] = new List<PointData>();
                }

                dotGroups[dotKey].Add(data);
            }

            // Sort each dot group by distance from origin, then by original index
            var sortedResult = new List<PointData>();

            foreach (var dotValue in dotGroups.Keys.OrderBy(k => k))
            {
                var subGroup = dotGroups[dotValue];

                // Sort by: 1. Distance, 2. Original index (for stability)
                var sortedSubGroup = subGroup
                    .OrderBy(pd => pd.Distance)
                    .ThenBy(pd => pd.OriginalIndex)
                    .ToList();

                sortedResult.AddRange(sortedSubGroup);
            }

            return sortedResult;
        }

        /// <summary>
        /// Data structure to hold point information
        /// </summary>
        private class PointData
        {
            public Point3d Point { get; set; }
            public double DotProduct { get; set; }
            public double Distance { get; set; }
            public int OriginalIndex { get; set; }
        }

        #endregion

        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Resources.SortedPoints;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("49D08532-1F2E-4313-9F34-CF54BFC9CBF2"); }
        }
    }
}