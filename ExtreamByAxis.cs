using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;

namespace ExtremeCurveAnalysis
{
    public class ExtremeCurveComponent : GH_Component
    {
        #region METADATA & CONSTRUCTOR

        /// <summary>
        /// </summary>
        public ExtremeCurveComponent()
          : base(
              "Extreme Curve and Points",
              "ExCur&Pts",
              "Finds extreme curves or points along X, Y, or Z axis by filtering perpendicular line segments",
              "Hannu Automation",
              "Curves"
          )
        {
        }

        /// <summary>
        /// Exposure level - controls where component appears in panel
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;

        #endregion

        #region INPUT PARAMETERS

        /// <summary>
        /// Register all INPUT parameters
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Input 0: Curves (Required, List)
            pManager.AddCurveParameter(
                "Curves",
                "C",
                "List of curves to analyze for extreme positions",
                GH_ParamAccess.list
            );

            // Input 1: Axis (Required, Item) - USER MUST INPUT
            pManager.AddTextParameter(
                "Axis",
                "A",
                "Reference axis: 'x', 'y', or 'z' (REQUIRED - must be specified by user)",
                GH_ParamAccess.item
            );

            // Input 2: ToPoint (Optional, Item, Default = false)
            pManager.AddBooleanParameter(
                "ToPoint",
                "P",
                "If true, output points; if false, output lines (default: false)",
                GH_ParamAccess.item,
                false
            );

            // Make only ToPoint optional
            pManager[2].Optional = true;
        }

        #endregion

        #region OUTPUT PARAMETERS

        /// <summary>
        /// Register all OUTPUT parameters
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Output 0: MinPoints
            pManager.AddPointParameter(
                "Min Points",
                "MinP",
                "Points at minimum position along the axis (rounded to 1 decimal)",
                GH_ParamAccess.list
            );

            // Output 1: MaxPoints
            pManager.AddPointParameter(
                "Max Points",
                "MaxP",
                "Points at maximum position along the axis (rounded to 1 decimal)",
                GH_ParamAccess.list
            );

            // Output 2: MinLines
            pManager.AddCurveParameter(
                "Min Curves",
                "MinC",
                "Curves at minimum position along the axis",
                GH_ParamAccess.list
            );

            // Output 3: MaxLines
            pManager.AddCurveParameter(
                "Max Curves",
                "MaxC",
                "Curves at maximum position along the axis",
                GH_ParamAccess.list
            );
        }

        #endregion

        #region MAIN EXECUTION

        /// <summary>
        /// MAIN EXECUTION METHOD - Called every time inputs change
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════════
            // STEP 1: DECLARE VARIABLES
            // ═══════════════════════════════════════════════════════════

            List<Curve> curves = new List<Curve>();
            string axis = string.Empty;  // No default - user must provide
            bool toPoint = false;

            // ═══════════════════════════════════════════════════════════
            // STEP 2: GET INPUT DATA
            // ═══════════════════════════════════════════════════════════

            if (!DA.GetDataList(0, curves)) return;
            if (!DA.GetData(1, ref axis)) return;
            if (!DA.GetData(2, ref toPoint)) return;

            // ═══════════════════════════════════════════════════════════
            // STEP 3: VALIDATE INPUTS
            // ═══════════════════════════════════════════════════════════

            // Validate curves
            if (curves == null || curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Empty list");
                return;
            }

            // Validate axis - MUST BE PROVIDED BY USER
            if (string.IsNullOrEmpty(axis))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Axis is required! Please specify 'x', 'y', or 'z'");
                return;
            }

            // Normalize and validate axis
            axis = axis.ToLower().Trim();

            if (axis != "x" && axis != "y" && axis != "z")
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Invalid axis. Use 'x', 'y', or 'z'");
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // STEP 4: PROCESS DATA (CORE LOGIC)
            // ═══════════════════════════════════════════════════════════

            // Tolerance settings
            const double PERP_TOL = 0.02;    // ~1 degree - perpendicular check
            const double POS_TOL = 0.01;     // 10mm - position grouping
            const double PT_TOL = 0.01;      // 10mm - duplicate points
            const double MIN_LENGTH = 0.001; // Minimum line length
            const int CURVE_DIVISIONS = 20;  // Divisions for complex curves

            try
            {
                // Step 4.1: Explode curves to lines
                List<Line> allLines = ExplodeCurvesToLines(curves, CURVE_DIVISIONS);

                if (allLines.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "No lines generated from curves");
                    return;
                }

                // Step 4.2: Filter perpendicular lines
                List<Line> perpLines = FilterPerpendicularLines(allLines, axis, PERP_TOL, MIN_LENGTH);

                if (perpLines.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"No lines perpendicular to axis {axis.ToUpper()}");
                    return;
                }

                // Step 4.3: Find min/max values
                double minVal, maxVal;
                FindMinMaxValues(perpLines, axis, out minVal, out maxVal);

                // Step 4.4: Check if min == max (all lines at same position)
                if (Math.Abs(maxVal - minVal) < POS_TOL)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"All lines at same position ({axis.ToUpper()} = {minVal:F2}) - Min and Max are identical");
                    return;
                }

                // Step 4.5: Filter extreme lines
                List<Line> minLines, maxLines;
                FilterExtremeLines(perpLines, axis, minVal, maxVal, POS_TOL, out minLines, out maxLines);

                if (minLines.Count == 0 && maxLines.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "No extreme lines found");
                    return;
                }

                // ═══════════════════════════════════════════════════════════
                // STEP 5: SET OUTPUT DATA
                // ═══════════════════════════════════════════════════════════

                if (toPoint)
                {
                    // Extract points from lines
                    List<Point3d> minPoints = ExtractUniquePoints(minLines, PT_TOL);
                    List<Point3d> maxPoints = ExtractUniquePoints(maxLines, PT_TOL);

                    // ★ ROUND COORDINATES TO 1 DECIMAL PLACE ★
                    List<Point3d> minPointsRounded = RoundPoints(minPoints, 1);
                    List<Point3d> maxPointsRounded = RoundPoints(maxPoints, 1);

                    // Set outputs
                    DA.SetDataList(0, minPointsRounded);
                    DA.SetDataList(1, maxPointsRounded);
                    DA.SetDataList(2, new List<Curve>());
                    DA.SetDataList(3, new List<Curve>());
                }
                else
                {
                    // Convert lines to curves
                    List<Curve> minCurves = minLines.Select(ln => ln.ToNurbsCurve() as Curve).ToList();
                    List<Curve> maxCurves = maxLines.Select(ln => ln.ToNurbsCurve() as Curve).ToList();

                    // Set outputs
                    DA.SetDataList(0, new List<Point3d>());
                    DA.SetDataList(1, new List<Point3d>());
                    DA.SetDataList(2, minCurves);
                    DA.SetDataList(3, maxCurves);

                    // Success message
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Error: {ex.Message}");
            }
        }

        #endregion

        #region HELPER METHODS

        /// <summary>
        /// ★ Round all point coordinates to specified decimal places ★
        /// 
        /// EXAMPLES:
        /// (1.9999, 1.07888, 1.11888) → (2.0, 1.1, 1.1) with decimals=1
        /// </summary>
        /// <param name="points">List of points to round</param>
        /// <param name="decimals">Number of decimal places</param>
        /// <returns>List of rounded points</returns>
        private List<Point3d> RoundPoints(List<Point3d> points, int decimals)
        {
            List<Point3d> rounded = new List<Point3d>();

            foreach (Point3d pt in points)
            {
                double x = Math.Round(pt.X, decimals);
                double y = Math.Round(pt.Y, decimals);
                double z = Math.Round(pt.Z, decimals);

                rounded.Add(new Point3d(x, y, z));
            }

            return rounded;
        }

        /// <summary>
        /// Explode all curves to line segments
        /// </summary>
        /// <param name="curves">Input curves to explode</param>
        /// <param name="divisions">Number of divisions for complex curves</param>
        /// <returns>List of line segments</returns>
        private List<Line> ExplodeCurvesToLines(List<Curve> curves, int divisions)
        {
            List<Line> lines = new List<Line>();

            foreach (Curve crv in curves)
            {
                if (crv == null) continue;

                // Try to extract polyline segments directly
                Polyline poly;
                if (crv.TryGetPolyline(out poly))
                {
                    Line[] segments = poly.GetSegments();
                    if (segments != null)
                        lines.AddRange(segments);
                }
                else
                {
                    // Divide complex curve into line segments
                    double[] parameters = crv.DivideByCount(divisions, true);
                    if (parameters != null && parameters.Length > 1)
                    {
                        for (int i = 0; i < parameters.Length - 1; i++)
                        {
                            Point3d p1 = crv.PointAt(parameters[i]);
                            Point3d p2 = crv.PointAt(parameters[i + 1]);
                            lines.Add(new Line(p1, p2));
                        }
                    }
                }
            }

            return lines;
        }

        /// <summary>
        /// Filter lines perpendicular to specified axis
        /// </summary>
        /// <param name="lines">Input lines to filter</param>
        /// <param name="axis">Axis to check perpendicularity ('x', 'y', 'z')</param>
        /// <param name="angleTol">Angle tolerance for perpendicularity check</param>
        /// <param name="minLength">Minimum line length to consider</param>
        /// <returns>List of perpendicular lines</returns>
        private List<Line> FilterPerpendicularLines(List<Line> lines, string axis,
                                                     double angleTol, double minLength)
        {
            List<Line> result = new List<Line>();

            foreach (Line ln in lines)
            {
                // Skip lines shorter than minimum length
                if (ln.Length < minLength) continue;

                // Get unit direction vector
                Vector3d dir = ln.Direction;
                dir.Unitize();

                // Get component along axis
                double component = GetAxisComponent(dir, axis);

                // Check if perpendicular (component ≈ 0)
                if (Math.Abs(component) < angleTol)
                    result.Add(ln);
            }

            return result;
        }

        /// <summary>
        /// Find min and max coordinate values along axis
        /// </summary>
        /// <param name="lines">Lines to analyze</param>
        /// <param name="axis">Axis to measure along</param>
        /// <param name="minVal">Output minimum value</param>
        /// <param name="maxVal">Output maximum value</param>
        private void FindMinMaxValues(List<Line> lines, string axis,
                                       out double minVal, out double maxVal)
        {
            // Use LINQ for cleaner code
            var allCoords = lines.SelectMany(ln =>
                new[] { GetCoord(ln.From, axis), GetCoord(ln.To, axis) });

            minVal = allCoords.Min();
            maxVal = allCoords.Max();
        }

        /// <summary>
        /// Filter lines at extreme (min/max) positions
        /// </summary>
        /// <param name="lines">Lines to filter</param>
        /// <param name="axis">Axis to check</param>
        /// <param name="minVal">Minimum value</param>
        /// <param name="maxVal">Maximum value</param>
        /// <param name="tolerance">Position tolerance</param>
        /// <param name="minLines">Output lines at min position</param>
        /// <param name="maxLines">Output lines at max position</param>
        private void FilterExtremeLines(List<Line> lines, string axis,
                                         double minVal, double maxVal, double tolerance,
                                         out List<Line> minLines, out List<Line> maxLines)
        {
            minLines = new List<Line>();
            maxLines = new List<Line>();

            foreach (Line ln in lines)
            {
                double val1 = GetCoord(ln.From, axis);
                double val2 = GetCoord(ln.To, axis);
                double lnMin = Math.Min(val1, val2);
                double lnMax = Math.Max(val1, val2);

                // Check if at min position
                if (Math.Abs(lnMin - minVal) < tolerance || Math.Abs(lnMax - minVal) < tolerance)
                    minLines.Add(ln);

                // Check if at max position
                if (Math.Abs(lnMin - maxVal) < tolerance || Math.Abs(lnMax - maxVal) < tolerance)
                    maxLines.Add(ln);
            }
        }

        /// <summary>
        /// Extract unique points from lines with tolerance
        /// </summary>
        /// <param name="lines">Lines to extract points from</param>
        /// <param name="tolerance">Duplicate point tolerance</param>
        /// <returns>List of unique points</returns>
        private List<Point3d> ExtractUniquePoints(List<Line> lines, double tolerance)
        {
            List<Point3d> points = new List<Point3d>();

            foreach (Line ln in lines)
            {
                if (!HasPoint(points, ln.From, tolerance))
                    points.Add(ln.From);
                if (!HasPoint(points, ln.To, tolerance))
                    points.Add(ln.To);
            }

            return points;
        }

        /// <summary>
        /// Get coordinate value from point based on axis
        /// </summary>
        /// <param name="pt">Point to get coordinate from</param>
        /// <param name="axis">Axis ('x', 'y', 'z')</param>
        /// <returns>Coordinate value</returns>
        private double GetCoord(Point3d pt, string axis)
        {
            switch (axis)
            {
                case "x": return pt.X;
                case "y": return pt.Y;
                default: return pt.Z;
            }
        }

        /// <summary>
        /// Get vector component along axis
        /// </summary>
        /// <param name="v">Vector to get component from</param>
        /// <param name="axis">Axis ('x', 'y', 'z')</param>
        /// <returns>Vector component</returns>
        private double GetAxisComponent(Vector3d v, string axis)
        {
            switch (axis)
            {
                case "x": return v.X;
                case "y": return v.Y;
                default: return v.Z;
            }
        }

        /// <summary>
        /// Check if list contains point within tolerance
        /// </summary>
        /// <param name="list">List of points to check</param>
        /// <param name="pt">Point to find</param>
        /// <param name="tolerance">Distance tolerance</param>
        /// <returns>True if point exists in list</returns>
        private bool HasPoint(List<Point3d> list, Point3d pt, double tolerance)
        {
            return list.Any(p => p.DistanceTo(pt) < tolerance);
        }

        #endregion

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Resources.ExtreamByAxis;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("C11F5962-A458-4176-BAB8-E42F3304A8C7"); }
        }
    }
} 