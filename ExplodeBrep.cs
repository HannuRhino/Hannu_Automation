using Grasshopper.Kernel;
using GroupPoint_XY.Properties;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrepNavigationComponents
{
    /// <summary>
    /// BREP Navigation Component
    /// Analyzes and sorts corner points, determines long/short edges
    /// </summary>
    public class BrepNavigationComponent : GH_Component
    {
        private const double TOLERANCE = 1e-10;
        private const double MIN_VECTOR_LENGTH = 1e-10;
        private const double MIN_CROSS_PRODUCT = 1e-6;
        private const double MIN_ANGLE_WARN = 10.0;
        private const double MAX_ANGLE_WARN = 170.0;

        public BrepNavigationComponent()
          : base(
              "ExplodeBrep",
              "BrepNav",
              "Analyze BREP and sort corner points by custom or automatic coordinate system",
              "Hannu Automation",
              "Geometry"
          )
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("92F03180-1AA5-494B-B6BC-62BCD11C54A7");
        protected override System.Drawing.Bitmap Icon => Resources.ExplodeBrep;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("BREP", "B", "BREP geometry to analyze", GH_ParamAccess.item);
            pManager.AddVectorParameter("VectorLongEdge", "VL", "Vector along long edge (manual mode only)", GH_ParamAccess.item, Vector3d.XAxis);
            pManager.AddVectorParameter("VectorShortEdge", "VS", "Vector along short edge (manual mode only)", GH_ParamAccess.item, Vector3d.YAxis);
            pManager.AddBooleanParameter("TOF", "T", "True = Automatic detection, False = Manual vectors", GH_ParamAccess.item, true);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("MinPointsY", "MinY", "Points with minimum Y (sorted by Z)", GH_ParamAccess.list);
            pManager.AddPointParameter("MaxPointY", "MaxY", "Points with maximum Y (sorted by Z)", GH_ParamAccess.list);
            pManager.AddPointParameter("MinPointsX", "MinX", "Points with minimum X (sorted by Z)", GH_ParamAccess.list);
            pManager.AddPointParameter("MaxPointsX", "MaxX", "Points with maximum X (sorted by Z)", GH_ParamAccess.list);
            pManager.AddLineParameter("LinesX", "LX", "Lines along X direction", GH_ParamAccess.list);
            pManager.AddLineParameter("LinesY", "LY", "Lines along Y direction", GH_ParamAccess.list);
            pManager.AddVectorParameter("VecLongOFBrep", "VLOut", "Vector along long edge", GH_ParamAccess.item);
            pManager.AddVectorParameter("VecShortOFBrep", "VSOut", "Vector along short edge", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            Brep brep = null;
            Vector3d vectorLongEdge = Vector3d.XAxis;
            Vector3d vectorShortEdge = Vector3d.YAxis;
            bool autoMode = true;

            if (!DA.GetData(0, ref brep)) return;
            DA.GetData(1, ref vectorLongEdge);
            DA.GetData(2, ref vectorShortEdge);
            DA.GetData(3, ref autoMode);

            // Validate inputs
            if (brep == null || !brep.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid BREP geometry");
                return;
            }

            if (!autoMode && !ValidateManualVectors(vectorLongEdge, vectorShortEdge))
                return;

            try
            {
                // Determine coordinate system
                Vector3d workingVectorX, workingVectorY, vectorLong, vectorShort, vectorZ;
                DetermineCoordinateSystem(brep, vectorLongEdge, vectorShortEdge, autoMode,
                    out workingVectorX, out workingVectorY, out vectorLong, out vectorShort, out vectorZ);

                // Get and classify corner points
                List<Point3d> corners = GetBoundingBoxCorners(brep, workingVectorX, workingVectorY, vectorZ);

                List<Point3d> minPointsX, maxPointsX, minPointsY, maxPointsY;
                ClassifyAndSortPoints(corners, workingVectorX, workingVectorY, vectorZ,
                    out minPointsX, out maxPointsX, out minPointsY, out maxPointsY);

                // Create connecting lines
                List<Line> linesX = CreateConnectingLines(minPointsX, maxPointsX);
                List<Line> linesY = CreateConnectingLines(minPointsY, maxPointsY);

                // ============ NEW LOGIC ============
                // Determine which lines are long/short based on actual line lengths
                Vector3d finalVectorLong, finalVectorShort;
                DetermineLongShortFromLines(linesX, linesY,
                    out finalVectorLong, out finalVectorShort);
                // ===================================

                // Set outputs
                DA.SetDataList(0, minPointsY);
                DA.SetDataList(1, maxPointsY);
                DA.SetDataList(2, minPointsX);
                DA.SetDataList(3, maxPointsX);
                DA.SetDataList(4, linesX);
                DA.SetDataList(5, linesY);
                DA.SetData(6, finalVectorLong);   // Changed from vectorLong
                DA.SetData(7, finalVectorShort);  // Changed from vectorShort
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Determine long/short vectors based on actual line lengths
        /// </summary>
        private void DetermineLongShortFromLines(
    List<Line> linesX,
    List<Line> linesY,
    out Vector3d vectorLong,
    out Vector3d vectorShort)
        {
            // Calculate average length of lines in each direction
            double avgLengthX = 0;
            double avgLengthY = 0;

            if (linesX.Count > 0)
            {
                avgLengthX = linesX.Average(line => line.Length);
            }

            if (linesY.Count > 0)
            {
                avgLengthY = linesY.Average(line => line.Length);
            }

            // Determine which is longer and extract FULL vectors (not just direction)
            if (avgLengthX >= avgLengthY)
            {
                // Lines_X is long edge
                if (linesX.Count > 0)
                {
                    vectorLong = linesX[0].To - linesX[0].From;  // Vector FROM->TO (có độ dài)
                }
                else
                {
                    vectorLong = Vector3d.XAxis;
                }

                if (linesY.Count > 0)
                {
                    vectorShort = linesY[0].To - linesY[0].From;  // Vector FROM->TO (có độ dài)
                }
                else
                {
                    vectorShort = Vector3d.YAxis;
                }

                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Lines_X is long edge (avg: {avgLengthX:F2}), Lines_Y is short edge (avg: {avgLengthY:F2})");
            }
            else
            {
                // Lines_Y is long edge
                if (linesY.Count > 0)
                {
                    vectorLong = linesY[0].To - linesY[0].From;  // Vector FROM->TO (có độ dài)
                }
                else
                {
                    vectorLong = Vector3d.YAxis;
                }

                if (linesX.Count > 0)
                {
                    vectorShort = linesX[0].To - linesX[0].From;  // Vector FROM->TO (có độ dài)
                }
                else
                {
                    vectorShort = Vector3d.XAxis;
                }

                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Lines_Y is long edge (avg: {avgLengthY:F2}), Lines_X is short edge (avg: {avgLengthX:F2})");
            }
        }

        /// <summary>
        /// Validate manual vectors for proper coordinate system
        /// </summary>
        private bool ValidateManualVectors(Vector3d vectorLong, Vector3d vectorShort)
        {
            if (vectorLong.Length < MIN_VECTOR_LENGTH)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "VectorLongEdge has zero length");
                return false;
            }

            if (vectorShort.Length < MIN_VECTOR_LENGTH)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "VectorShortEdge has zero length");
                return false;
            }

            Vector3d vl = vectorLong;
            Vector3d vs = vectorShort;
            vl.Unitize();
            vs.Unitize();

            Vector3d cross = Vector3d.CrossProduct(vl, vs);
            if (cross.Length < MIN_CROSS_PRODUCT)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Vectors are parallel. Please provide non-parallel vectors.");
                return false;
            }

            double angleDegrees = RhinoMath.ToDegrees(Vector3d.VectorAngle(vl, vs));
            if (angleDegrees < MIN_ANGLE_WARN || angleDegrees > MAX_ANGLE_WARN)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Angle between vectors is {angleDegrees:F1}°. Consider using vectors closer to 90°.");
            }

            return true;
        }

        /// <summary>
        /// Determine coordinate system with automatic long/short edge detection
        /// </summary>
        private void DetermineCoordinateSystem(
            Brep brep,
            Vector3d inputVectorLong,
            Vector3d inputVectorShort,
            bool autoMode,
            out Vector3d workingVectorX,
            out Vector3d workingVectorY,
            out Vector3d vectorLong,
            out Vector3d vectorShort,
            out Vector3d vectorZ)
        {
            vectorZ = Vector3d.ZAxis;

            if (autoMode)
            {
                // Automatic: detect long/short from bounding box
                BoundingBox bbox = brep.GetBoundingBox(true);
                Point3d[] corners = bbox.GetCorners();

                Vector3d bboxVectorX = corners[1] - corners[0];
                Vector3d bboxVectorY = corners[3] - corners[0];

                // Calculate XY plane lengths only
                double lengthX = Math.Sqrt(bboxVectorX.X * bboxVectorX.X + bboxVectorX.Y * bboxVectorX.Y);
                double lengthY = Math.Sqrt(bboxVectorY.X * bboxVectorY.X + bboxVectorY.Y * bboxVectorY.Y);

                if (lengthX >= lengthY)
                {
                    vectorLong = bboxVectorX;
                    vectorShort = bboxVectorY;
                }
                else
                {
                    vectorLong = bboxVectorY;
                    vectorShort = bboxVectorX;
                }

                vectorLong.Unitize();
                vectorShort.Unitize();

                workingVectorX = bboxVectorX;
                workingVectorY = bboxVectorY;
                workingVectorX.Unitize();
                workingVectorY.Unitize();
            }
            else
            {
                // Manual: use input vectors
                vectorLong = inputVectorLong;
                vectorShort = inputVectorShort;
                vectorLong.Unitize();
                vectorShort.Unitize();

                workingVectorX = vectorLong;
                workingVectorY = vectorShort;
            }
        }

        /// <summary>
        /// Get 8 corner points of bounding box in custom coordinate system
        /// </summary>
        private List<Point3d> GetBoundingBoxCorners(Brep brep, Vector3d vectorX, Vector3d vectorY, Vector3d vectorZ)
        {
            BoundingBox worldBBox = brep.GetBoundingBox(true);
            Point3d origin = worldBBox.Center;

            Plane customPlane = new Plane(origin, vectorX, vectorY);
            BoundingBox customBBox = brep.GetBoundingBox(customPlane);

            Point3d[] corners = customBBox.GetCorners();
            Transform toWorld = Transform.PlaneToPlane(Plane.WorldXY, customPlane);

            List<Point3d> worldCorners = new List<Point3d>();
            foreach (Point3d corner in corners)
            {
                Point3d worldCorner = corner;
                worldCorner.Transform(toWorld);
                worldCorners.Add(worldCorner);
            }

            return worldCorners;
        }

        /// <summary>
        /// Classify and sort points with high precision tolerance
        /// </summary>
        private void ClassifyAndSortPoints(
            List<Point3d> points,
            Vector3d vectorX,
            Vector3d vectorY,
            Vector3d vectorZ,
            out List<Point3d> minPointsX,
            out List<Point3d> maxPointsX,
            out List<Point3d> minPointsY,
            out List<Point3d> maxPointsY)
        {
            Plane plane = new Plane(points[0], vectorX, vectorY);

            // Convert to local coordinates
            List<PointWithCoords> localPoints = points.Select(pt =>
            {
                Point3d localPt;
                plane.RemapToPlaneSpace(pt, out localPt);
                return new PointWithCoords(pt, localPt.X, localPt.Y, localPt.Z);
            }).ToList();

            // Classify with tolerance
            minPointsX = GetPointsAtExtremeValue(localPoints, p => p.LocalX, true, p => p.LocalZ, p => p.LocalY);
            maxPointsX = GetPointsAtExtremeValue(localPoints, p => p.LocalX, false, p => p.LocalZ, p => p.LocalY);
            minPointsY = GetPointsAtExtremeValue(localPoints, p => p.LocalY, true, p => p.LocalZ, p => p.LocalX);
            maxPointsY = GetPointsAtExtremeValue(localPoints, p => p.LocalY, false, p => p.LocalZ, p => p.LocalX);
        }

        /// <summary>
        /// Get points at extreme value within tolerance
        /// </summary>
        private List<Point3d> GetPointsAtExtremeValue(
            List<PointWithCoords> points,
            Func<PointWithCoords, double> coordinateSelector,
            bool findMinimum,
            Func<PointWithCoords, double> primarySort,
            Func<PointWithCoords, double> secondarySort)
        {
            if (points.Count == 0) return new List<Point3d>();

            double extremeValue = findMinimum
                ? points.Min(coordinateSelector)
                : points.Max(coordinateSelector);

            var candidates = points
                .Where(p => Math.Abs(coordinateSelector(p) - extremeValue) <= TOLERANCE)
                .OrderBy(primarySort)
                .ThenBy(secondarySort)
                .ToList();

            return candidates.Count <= 4
                ? candidates.Select(p => p.WorldPoint).ToList()
                : candidates.Take(4).Select(p => p.WorldPoint).ToList();
        }

        /// <summary>
        /// Create connecting lines between corresponding points
        /// </summary>
        private List<Line> CreateConnectingLines(List<Point3d> startPoints, List<Point3d> endPoints)
        {
            List<Line> lines = new List<Line>();
            int count = Math.Min(startPoints.Count, endPoints.Count);

            for (int i = 0; i < count; i++)
                lines.Add(new Line(startPoints[i], endPoints[i]));

            return lines;
        }

        /// <summary>
        /// Helper class for point with local coordinates
        /// </summary>
        private class PointWithCoords
        {
            public Point3d WorldPoint { get; }
            public double LocalX { get; }
            public double LocalY { get; }
            public double LocalZ { get; }

            public PointWithCoords(Point3d worldPoint, double localX, double localY, double localZ)
            {
                WorldPoint = worldPoint;
                LocalX = localX;
                LocalY = localY;
                LocalZ = localZ;
            }
        }
    }
}