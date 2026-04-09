using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;

namespace HannuAutomation
{
    public class ExtremeCurveAndPointsComponent : GH_Component
    {
        #region CONSTANTS

        private const double DEFAULT_TOLERANCE = 0.02;

        private const double MIN_LENGTH = 0.001;

        private const int CURVE_DIV = 20;

        #endregion

        #region METADATA & CONSTRUCTOR

        /// <summary>
        /// </summary>
        public ExtremeCurveAndPointsComponent()
          : base(
              "Explode Curves and Points",           // Full name
              "ExCur&Pts",                          // Nickname
              "Identify Curves and Points at min max value with improved perpendicular check and optimized point extraction",
              "Hannu Automation",              // Category (Tab)
              "Curves"                              // Subcategory (Panel)
          )
        {
        }
        public override GH_Exposure Exposure => GH_Exposure.primary;

        #endregion

        #region INPUT PARAMETERS

        /// <summary>
        /// 
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter(
                "Poly/Curves",
                "PC",
                "List of PolyCurves/Curves to analyze",
                GH_ParamAccess.list
            );

            pManager.AddVectorParameter(
                "Vector",
                "V",
                "Reference direction vector for finding extreme curves",
                GH_ParamAccess.item
            );

            // INPUT 2: Tolerance - Optional
            pManager.AddNumberParameter(
                "Tolerance",
                "Tolerance",
                "default: 0.02",
                GH_ParamAccess.item,
                DEFAULT_TOLERANCE
            );
        }

        #endregion

        #region OUTPUT PARAMETERS

        /// <summary>
        /// 
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // OUTPUT 0: Min Points
            pManager.AddPointParameter(
                "MinPoints",
                "MinP",
                "Unique points at minimum extreme position",
                GH_ParamAccess.list
            );

            // OUTPUT 1: Max Points
            pManager.AddPointParameter(
                "MaxPoints",
                "MaxP",
                "Unique points at maximum extreme position",
                GH_ParamAccess.list
            );

            // OUTPUT 2: Min Curves
            pManager.AddCurveParameter(
                "MinCurves",
                "MinC",
                "Curves at minimum extreme position",
                GH_ParamAccess.list
            );

            // OUTPUT 3: Max Curves
            pManager.AddCurveParameter(
                "MaxCurves",
                "MaxC",
                "Curves at maximum extreme position",
                GH_ParamAccess.list
            );
        }

        #endregion

        #region MAIN EXECUTION

        /// <summary>
        /// 
        /// EXECUTION FLOW:
        /// 2. Validate inputs
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            List<Curve> curves = new List<Curve>();
            Vector3d vector = Vector3d.Unset;
            double tolerance = DEFAULT_TOLERANCE;

            if (!DA.GetDataList(0, curves)) return;

            if (!DA.GetData(1, ref vector)) return;

            if (!DA.GetData(2, ref tolerance))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Using default tolerance: 0.02");
                tolerance = DEFAULT_TOLERANCE;
            }

            // ═══════════════════════════════════════════════════════
            // STEP 2: VALIDATE INPUT
            // ═══════════════════════════════════════════════════════

            if (curves == null || curves.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Empty curve list"
                );
                return;
            }

            if (vector == Vector3d.Zero || vector.Length == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Vector cannot be zero! Please provide a valid direction vector"
                );
                return;
            }

            if (tolerance <= 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Tolerance must be greater than 0"
                );
                return;
            }

            Vector3d refVector = vector;
            refVector.Unitize();

            double angleTolerance = tolerance;

            double distanceTolerance = tolerance * 0.5;

            double pointTolerance = tolerance * 0.5;

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            try
            {
                List<Line> allLines = ExplodeCurvesToLines(curves, CURVE_DIV);

                if (allLines == null || allLines.Count == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        "No lines generated from curves"
                    );
                    return;
                }

                List<Line> perpLines = FilterPerpendicularLines(
                    allLines,
                    refVector,
                    angleTolerance,
                    MIN_LENGTH
                );

                if (perpLines == null || perpLines.Count == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        $"No lines perpendicular to vector within tolerance ({angleTolerance:F3})"
                    );
                    return;
                }

                (double minVal, double maxVal) = FindMinMaxValues(perpLines, refVector);

                if (Math.Abs(maxVal - minVal) < distanceTolerance)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Remark,
                        $"All lines at same position along vector (difference: {Math.Abs(maxVal - minVal):F6})"
                    );
                    return;
                }

                (List<Line> minLines, List<Line> maxLines) = FilterExtremeLinesNoDuplicate(
                    perpLines,
                    refVector,
                    minVal,
                    maxVal,
                    distanceTolerance
                );

                if ((minLines == null || minLines.Count == 0) &&
                    (maxLines == null || maxLines.Count == 0))
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        "No extreme lines found"
                    );
                    return;
                }

                int decimalPlaces = CalculateDecimalPlaces(pointTolerance);

                List<Point3d> minPoints = ExtractUniquePointsOptimized(
                    minLines,
                    pointTolerance,
                    decimalPlaces
                );

                List<Point3d> maxPoints = ExtractUniquePointsOptimized(
                    maxLines,
                    pointTolerance,
                    decimalPlaces
                );

                // STEP 8: Convert lines to curves
                List<Curve> minCurves = minLines?.Select(ln => ln.ToNurbsCurve() as Curve).ToList()
                                        ?? new List<Curve>();
                List<Curve> maxCurves = maxLines?.Select(ln => ln.ToNurbsCurve() as Curve).ToList()
                                        ?? new List<Curve>();

                // ═══════════════════════════════════════════════════
                // SET OUTPUT DATA
                // ═══════════════════════════════════════════════════

                DA.SetDataList(0, minPoints);  // Min Points
                DA.SetDataList(1, maxPoints);  // Max Points
                DA.SetDataList(2, minCurves);  // Min Curves
                DA.SetDataList(3, maxCurves);  // Max Curves

                // Info message
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Remark,
                    $"Found {minLines?.Count ?? 0} min curves, {maxLines?.Count ?? 0} max curves | " +
                    $"Range: {maxVal - minVal:F3} units"
                );
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"Error: {ex.Message}\nStack: {ex.StackTrace}"
                );
                return;
            }
        }

        #endregion

        #region HELPER METHODS - CORE GEOMETRY

        /// <summary>
        /// </summary>
        private double ProjectOntoVector(Point3d pt, Vector3d vec)
        {
            return pt.X * vec.X + pt.Y * vec.Y + pt.Z * vec.Z;
        }

        /// <summary>
        /// </summary>
        private int CalculateDecimalPlaces(double tolerance)
        {
            if (tolerance >= 1.0) return 0;
            if (tolerance >= 0.1) return 1;
            if (tolerance >= 0.01) return 2;
            if (tolerance >= 0.001) return 3;
            return 4;
        }

        #endregion

        #region HELPER METHODS - CURVE PROCESSING

        /// <summary>
        /// - Polyline → GetSegments()
        /// </summary>
        private List<Line> ExplodeCurvesToLines(List<Curve> curves, int divisionCount)
        {
            List<Line> lines = new List<Line>();

            foreach (Curve curve in curves)
            {
                if (curve == null) continue;

                if (curve.TryGetPolyline(out Polyline poly))
                {
                    Line[] segments = poly.GetSegments();
                    if (segments != null)
                    {
                        lines.AddRange(segments);
                    }
                }
                else
                {
                    double[] parameters = curve.DivideByCount(divisionCount, true);

                    if (parameters != null && parameters.Length >= 2)
                    {
                        for (int i = 0; i < parameters.Length - 1; i++)
                        {
                            Point3d p1 = curve.PointAt(parameters[i]);
                            Point3d p2 = curve.PointAt(parameters[i + 1]);

                            if (p1.DistanceTo(p2) >= MIN_LENGTH)
                            {
                                lines.Add(new Line(p1, p2));
                            }
                        }
                    }
                }
            }

            return lines;
        }

        #endregion

        #region HELPER METHODS - LINE FILTERING

        /// <summary>
        /// </summary>
        private List<Line> FilterPerpendicularLines(
            List<Line> allLines,
            Vector3d refVector,
            double angleTolerance,
            double minLength)
        {
            if (allLines == null || allLines.Count == 0)
                return new List<Line>();

            List<Line> perpLines = new List<Line>();

            foreach (Line line in allLines)
            {
                if (line.Length < minLength) continue;

                Vector3d dir = line.Direction;
                dir.Unitize();

                double dotProduct = Vector3d.Multiply(dir, refVector);

                dotProduct = Math.Max(-1.0, Math.Min(1.0, dotProduct));

                double angle = Math.Acos(Math.Abs(dotProduct));

                double deviationFromPerpendicular = Math.Abs(angle - Math.PI / 2);

                if (deviationFromPerpendicular < angleTolerance)
                {
                    perpLines.Add(line);
                }
            }

            return perpLines;
        }

        #endregion

        #region HELPER METHODS - MIN/MAX FINDING

        /// <summary>
        /// </summary>
        private (double minVal, double maxVal) FindMinMaxValues(List<Line> lines, Vector3d refVector)
        {
            if (lines == null || lines.Count == 0)
                return (0, 0);

            List<double> allProjectedValues = new List<double>();

            foreach (Line line in lines)
            {
                allProjectedValues.Add(ProjectOntoVector(line.From, refVector));
                allProjectedValues.Add(ProjectOntoVector(line.To, refVector));
            }

            if (allProjectedValues.Count == 0)
                return (0, 0);

            double minVal = allProjectedValues.Min();
            double maxVal = allProjectedValues.Max();

            return (minVal, maxVal);
        }

        #endregion

        #region HELPER METHODS - EXTREME LINE FILTERING

        /// <summary>
        /// </summary>
        private (List<Line> minLines, List<Line> maxLines) FilterExtremeLinesNoDuplicate(
            List<Line> lines,
            Vector3d refVector,
            double minVal,
            double maxVal,
            double distanceTolerance)
        {
            if (lines == null || lines.Count == 0)
                return (new List<Line>(), new List<Line>());

            List<Line> minLines = new List<Line>();
            List<Line> maxLines = new List<Line>();

            foreach (Line line in lines)
            {
                double val1 = ProjectOntoVector(line.From, refVector);
                double val2 = ProjectOntoVector(line.To, refVector);

                double lnMin = Math.Min(val1, val2);
                double lnMax = Math.Max(val1, val2);

                double distToMin = Math.Min(
                    Math.Abs(lnMin - minVal),
                    Math.Abs(lnMax - minVal)
                );

                double distToMax = Math.Min(
                    Math.Abs(lnMin - maxVal),
                    Math.Abs(lnMax - maxVal)
                );

                bool isNearMin = distToMin < distanceTolerance;
                bool isNearMax = distToMax < distanceTolerance;

                if (isNearMin && isNearMax)
                {
                    if (distToMin <= distToMax)
                        minLines.Add(line);
                    else
                        maxLines.Add(line);
                }
                else if (isNearMin)
                {
                    minLines.Add(line);
                }
                else if (isNearMax)
                {
                    maxLines.Add(line);
                }
            }

            return (minLines, maxLines);
        }

        #endregion

        #region HELPER METHODS - POINT EXTRACTION (OPTIMIZED)

        /// <summary>
        /// </summary>
        private List<Point3d> ExtractUniquePointsOptimized(
            List<Line> lines,
            double tolerance,
            int decimalPlaces)
        {
            if (lines == null || lines.Count == 0)
                return new List<Point3d>();

            var uniquePoints = new HashSet<Point3d>(new Point3dRoundedComparer(tolerance, decimalPlaces));

            foreach (Line line in lines)
            {
                Point3d roundedFrom = RoundPoint(line.From, decimalPlaces);
                Point3d roundedTo = RoundPoint(line.To, decimalPlaces);

                uniquePoints.Add(roundedFrom);
                uniquePoints.Add(roundedTo);
            }

            return uniquePoints.ToList();
        }

        /// <summary>
        /// </summary>
        private Point3d RoundPoint(Point3d pt, int decimals)
        {
            return new Point3d(
                Math.Round(pt.X, decimals),
                Math.Round(pt.Y, decimals),
                Math.Round(pt.Z, decimals)
            );
        }

        /// <summary>
        /// </summary>
        private class Point3dRoundedComparer : IEqualityComparer<Point3d>
        {
            private readonly double _tolerance;
            private readonly int _decimals;

            public Point3dRoundedComparer(double tolerance, int decimals)
            {
                _tolerance = tolerance;
                _decimals = decimals;
            }

            public bool Equals(Point3d p1, Point3d p2)
            {
                return p1.DistanceTo(p2) < _tolerance;
            }

            public int GetHashCode(Point3d pt)
            {
                int x = (int)Math.Round(pt.X / _tolerance);
                int y = (int)Math.Round(pt.Y / _tolerance);
                int z = (int)Math.Round(pt.Z / _tolerance);

                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + x.GetHashCode();
                    hash = hash * 23 + y.GetHashCode();
                    hash = hash * 23 + z.GetHashCode();
                    return hash;
                }
            }
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
                return Resources.ExplodePolyCurve;
            }
        }

        /// <summary>
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("90966CAB-582A-4D6E-94E2-906D85A932D7"); }
        }

        #endregion
    }
}