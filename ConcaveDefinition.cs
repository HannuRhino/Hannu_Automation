using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SlicingTools
{
    /// <summary>
    /// Slice Boundary with Concave Detection
    /// Extracts boundaries and detects concave angles
    /// </summary>
    public class SliceGeometry : GH_Component
    {
        private const double MIN_CURVE_LENGTH = 1e-10;
        private const double MIN_POINT_DISTANCE = 0.001;
        private const double MIN_VECTOR_LENGTH = 0.001;
        private const double DEFAULT_TOLERANCE = 0.01;

        public SliceGeometry()
          : base(
              "ConcavePolygon",
              "Concave",
              "Get boundaries with concave detection from geometry slice",
              "Hannu Automation",
              "Geometry"
          )
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("584AA12D-F41F-4209-8BC1-67DB80EEDF80");
        protected override System.Drawing.Bitmap Icon => Resources.ConcaveBoundary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("BREP/Geometry", "BG", "Geometry to slice (supports Brep, Mesh, Surface, Curve)", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "P", "Reference plane for slicing", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("Distance", "D", "Distance offset along plane normal (default = geometry midpoint)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tolerance", "T", "Tolerance for curve processing and concave detection", GH_ParamAccess.item, DEFAULT_TOLERANCE);
            pManager.AddBooleanParameter("Use h_max", "hMax", "Slice at maximum height along plane normal", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Use h_min", "hMin", "Slice at minimum height along plane normal", GH_ParamAccess.item, false);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("ConcaveCurves", "BCC", "Lines forming concave angles in boundaries", GH_ParamAccess.tree);
            pManager.AddPointParameter("ConcavePoints", "BCP", "Points forming concave angles", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            List<object> geometryInputs = new List<object>();
            Plane referencePlane = Plane.WorldXY;
            double distance = 0.0;
            double tolerance = DEFAULT_TOLERANCE;
            bool useHMax = false;
            bool useHMin = false;

            if (!DA.GetDataList(0, geometryInputs)) return;
            DA.GetData(1, ref referencePlane);
            bool distanceProvided = DA.GetData(2, ref distance);
            DA.GetData(3, ref tolerance);
            DA.GetData(4, ref useHMax);
            DA.GetData(5, ref useHMin);

            // Validate hMax and hMin
            if (useHMax && useHMin)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Cannot use both h_max and h_min at the same time. Set one to False.");
                return;
            }

            // Validate plane
            if (!referencePlane.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid reference plane");
                return;
            }

            // Extract geometries from wrappers
            List<GeometryBase> geometries = new List<GeometryBase>();
            List<Curve> directCurves = new List<Curve>();

            foreach (object input in geometryInputs)
            {
                if (input == null) continue;

                GeometryBase geometry = ExtractGeometryFromWrapper(input);

                if (geometry == null || !geometry.IsValid) continue;

                // If it's a Curve, add to directCurves
                if (geometry is Curve curve)
                {
                    directCurves.Add(curve);
                }
                else
                {
                    geometries.Add(geometry);
                }
            }

            if (geometries.Count == 0 && directCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No valid geometries provided");
                return;
            }

            if (tolerance <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Invalid tolerance, using {DEFAULT_TOLERANCE}");
                tolerance = DEFAULT_TOLERANCE;
            }

            try
            {
                List<Curve> allCurves = new List<Curve>();
                double actualDistance = distance;

                // Only slice if there are 3D geometries (Brep, Mesh, Surface)
                if (geometries.Count > 0)
                {
                    // Calculate height bounds along plane normal
                    double[] hBounds = CalculateHeightBounds(geometries, referencePlane);
                    double h_min = hBounds[0];
                    double h_max = hBounds[1];

                    if (double.IsInfinity(h_min) || double.IsInfinity(h_max))
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            "Cannot calculate height bounds for provided geometry");
                        return;
                    }

                    // Calculate actual distance
                    if (useHMax)
                    {
                        actualDistance = h_max - 0.1;  // ← SỬA: Trừ tolerance
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Slicing at h_max - tolerance = {actualDistance:F3} (h_max={h_max:F3})");
                    }
                    else if (useHMin)
                    {
                        actualDistance = h_min + 0.1;  // ← SỬA: Cộng tolerance
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Slicing at h_min + tolerance = {actualDistance:F3} (h_min={h_min:F3})");
                    }
                    else if (!distanceProvided || distance == 0)
                    {
                        // Auto-calculate distance as midpoint
                        actualDistance = (h_min + h_max) / 2.0;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Auto distance = {actualDistance:F3} (midpoint between h_min={h_min:F3} and h_max={h_max:F3})");
                    }

                    // Slice 3D geometries
                    List<Curve> slicedCurves = SliceGeometries(geometries, referencePlane, actualDistance, tolerance);
                    allCurves.AddRange(slicedCurves);
                }

                // Add direct curves (project to reference plane)
                if (directCurves.Count > 0)
                {
                    foreach (Curve curve in directCurves)
                    {
                        Curve projectedCurve = Curve.ProjectToPlane(curve, referencePlane);
                        if (projectedCurve != null && projectedCurve.IsValid)
                        {
                            allCurves.Add(projectedCurve);
                        }
                    }
                }

                if (allCurves.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "No curves found");
                    return;
                }

                // Clean and classify curves
                List<Curve> cleanedCurves = ProjectAndClean(allCurves, referencePlane, tolerance);

                if (cleanedCurves.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "No closed curves after cleaning");
                    return;
                }

                cleanedCurves = cleanedCurves.OrderByDescending(GetCurveArea).ToList();

                List<Curve> boundaries = GetBoundaries(cleanedCurves, tolerance);

                // Process boundaries for concave detection
                DataTree<Line> boundariesConcaveCurves = new DataTree<Line>();
                DataTree<Point3d> boundariesConcavePoints = new DataTree<Point3d>();
                ProcessBoundariesForConcave(boundaries, referencePlane, tolerance,
                    ref boundariesConcaveCurves, ref boundariesConcavePoints);

                // Post-process: merge collinear lines and remove duplicates
                PostProcessConcaveData(ref boundariesConcaveCurves, ref boundariesConcavePoints, tolerance);

                // Set outputs
                DA.SetDataTree(0, boundariesConcaveCurves);
                DA.SetDataTree(1, boundariesConcavePoints);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Post-process concave data: merge collinear lines and remove duplicates
        /// </summary>
        private void PostProcessConcaveData(ref DataTree<Line> curves, ref DataTree<Point3d> points, double tolerance)
        {
            DataTree<Line> processedCurves = new DataTree<Line>();
            DataTree<Point3d> processedPoints = new DataTree<Point3d>();

            // Process each branch independently
            foreach (GH_Path path in curves.Paths)
            {
                List<Line> branchLines = curves.Branch(path);
                List<Point3d> branchPoints = points.Branch(path);

                if (branchLines.Count == 0) continue;

                // Merge collinear lines within this branch
                List<Line> mergedLines = new List<Line>();
                List<Point3d> mergedPoints = new List<Point3d>();

                if (branchLines.Count == 2)
                {
                    // Check if the 2 lines are collinear
                    Line line1 = branchLines[0];
                    Line line2 = branchLines[1];

                    if (AreCollinear(line1, line2, tolerance))
                    {
                        // Merge into single line from first point to last point
                        Line mergedLine = new Line(line1.From, line2.To);
                        mergedLines.Add(mergedLine);

                        // Points become [A, C] instead of [A, B, B, C]
                        mergedPoints.Add(line1.From);
                        mergedPoints.Add(line2.To);
                    }
                    else
                    {
                        // Keep both lines
                        mergedLines.AddRange(branchLines);
                        mergedPoints.AddRange(branchPoints);
                    }
                }
                else
                {
                    // Keep as is if not exactly 2 lines
                    mergedLines.AddRange(branchLines);
                    mergedPoints.AddRange(branchPoints);
                }

                // Remove duplicate lines within branch
                List<Line> uniqueLines = RemoveDuplicateLines(mergedLines, tolerance);

                // Remove duplicate points within branch
                List<Point3d> uniquePoints = RemoveDuplicatePoints(mergedPoints, tolerance);

                // Add to processed trees
                foreach (Line line in uniqueLines)
                {
                    processedCurves.Add(line, path);
                }

                foreach (Point3d point in uniquePoints)
                {
                    processedPoints.Add(point, path);
                }
            }

            curves = processedCurves;
            points = processedPoints;
        }

        /// <summary>
        /// Check if two lines are collinear (same direction)
        /// </summary>
        private bool AreCollinear(Line line1, Line line2, double tolerance)
        {
            // Get direction vectors
            Vector3d v1 = line1.Direction;
            Vector3d v2 = line2.Direction;

            if (!v1.Unitize() || !v2.Unitize())
                return false;

            // Dot product of normalized vectors
            double dot = v1 * v2;

            // Check if dot ≈ 1 (same direction)
            return Math.Abs(dot - 1.0) < tolerance;
        }

        /// <summary>
        /// Remove duplicate lines from list
        /// </summary>
        private List<Line> RemoveDuplicateLines(List<Line> lines, double tolerance)
        {
            List<Line> uniqueLines = new List<Line>();

            foreach (Line line in lines)
            {
                bool isDuplicate = false;

                foreach (Line existing in uniqueLines)
                {
                    // Check if lines are the same (endpoints within tolerance)
                    bool sameFrom = line.From.DistanceTo(existing.From) < tolerance;
                    bool sameTo = line.To.DistanceTo(existing.To) < tolerance;

                    // Also check reversed direction
                    bool sameReversed = line.From.DistanceTo(existing.To) < tolerance &&
                                       line.To.DistanceTo(existing.From) < tolerance;

                    if ((sameFrom && sameTo) || sameReversed)
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    uniqueLines.Add(line);
                }
            }

            return uniqueLines;
        }

        /// <summary>
        /// Remove duplicate points from list
        /// </summary>
        private List<Point3d> RemoveDuplicatePoints(List<Point3d> points, double tolerance)
        {
            List<Point3d> uniquePoints = new List<Point3d>();

            foreach (Point3d point in points)
            {
                bool isDuplicate = false;

                foreach (Point3d existing in uniquePoints)
                {
                    if (point.DistanceTo(existing) < tolerance)
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    uniquePoints.Add(point);
                }
            }

            return uniquePoints;
        }

        /// <summary>
        /// Extract geometry from Grasshopper wrapper
        /// </summary>
        private GeometryBase ExtractGeometryFromWrapper(object geometryInput)
        {
            if (geometryInput == null)
                return null;

            // Try to cast directly to GeometryBase first
            if (geometryInput is GeometryBase geometryBase)
            {
                return geometryBase;
            }

            // Extract Curve (including PolylineCurve) from GH_Curve wrapper
            if (geometryInput is GH_Curve ghCurve)
            {
                if (ghCurve.Value != null)
                    return ghCurve.Value;
            }

            // Extract Brep from GH_Brep wrapper
            if (geometryInput is GH_Brep ghBrep)
            {
                if (ghBrep.Value != null)
                    return ghBrep.Value;
            }

            // Extract Mesh from GH_Mesh wrapper
            if (geometryInput is GH_Mesh ghMesh)
            {
                if (ghMesh.Value != null)
                    return ghMesh.Value;
            }

            // Extract Surface from GH_Surface wrapper
            if (geometryInput is GH_Surface ghSurface)
            {
                if (ghSurface.Value != null)
                    return ghSurface.Value;
            }

            // Handle IGH_GeometricGoo (generic geometry wrapper)
            if (geometryInput is IGH_GeometricGoo goo)
            {
                if (goo.ScriptVariable() is GeometryBase geo)
                    return geo;
            }

            return null;
        }

        /// <summary>
        /// Calculate height bounds of all geometries along plane normal
        /// Returns array [h_min, h_max]
        /// </summary>
        private double[] CalculateHeightBounds(List<GeometryBase> geometries, Plane referencePlane)
        {
            double h_min = double.PositiveInfinity;
            double h_max = double.NegativeInfinity;

            foreach (GeometryBase geometry in geometries)
            {
                if (geometry == null || !geometry.IsValid) continue;

                BoundingBox bbox = geometry.GetBoundingBox(true);

                if (bbox.IsValid)
                {
                    // Test all 8 corners of bounding box
                    Point3d[] corners = bbox.GetCorners();

                    foreach (Point3d corner in corners)
                    {
                        // Height = projection onto plane normal (dot product)
                        Vector3d fromPlaneOrigin = corner - referencePlane.Origin;
                        double height = fromPlaneOrigin * referencePlane.Normal;

                        h_min = Math.Min(h_min, height);
                        h_max = Math.Max(h_max, height);
                    }
                }
            }

            return new double[] { h_min, h_max };
        }

        /// <summary>
        /// Slice all geometries and collect curves
        /// </summary>
        private List<Curve> SliceGeometries(List<GeometryBase> geometries, Plane referencePlane, double distance, double tolerance)
        {
            List<Curve> allCurves = new List<Curve>();

            // Create slice plane by offsetting reference plane along normal
            Plane slicePlane = new Plane(referencePlane);
            slicePlane.Origin = referencePlane.Origin + referencePlane.Normal * distance;

            foreach (GeometryBase geometry in geometries)
            {
                if (geometry == null || !geometry.IsValid) continue;

                List<Curve> curves = SliceSingleGeometry(geometry, slicePlane, tolerance);
                if (curves != null && curves.Count > 0)
                    allCurves.AddRange(curves);
            }

            return allCurves;
        }

        /// <summary>
        /// Slice a single geometry based on type
        /// </summary>
        private List<Curve> SliceSingleGeometry(GeometryBase geometry, Plane slicePlane, double tolerance)
        {
            List<Curve> curves = new List<Curve>();

            if (geometry is Brep brep)
            {
                Curve[] intersectionCurves;
                Point3d[] intersectionPoints;

                if (Rhino.Geometry.Intersect.Intersection.BrepPlane(brep, slicePlane, tolerance,
                    out intersectionCurves, out intersectionPoints) && intersectionCurves != null)
                {
                    curves.AddRange(intersectionCurves);
                }
            }
            else if (geometry is Mesh mesh)
            {
                Polyline[] polylines = Rhino.Geometry.Intersect.Intersection.MeshPlane(mesh, slicePlane);

                if (polylines != null)
                {
                    foreach (var polyline in polylines)
                    {
                        if (polyline != null)
                        {
                            Curve curve = polyline.ToNurbsCurve();
                            if (curve != null) curves.Add(curve);
                        }
                    }
                }
            }
            else if (geometry is Surface surface)
            {
                Curve[] intersectionCurves;
                Point3d[] intersectionPoints;

                if (Rhino.Geometry.Intersect.Intersection.BrepPlane(surface.ToBrep(), slicePlane, tolerance,
                    out intersectionCurves, out intersectionPoints) && intersectionCurves != null)
                {
                    curves.AddRange(intersectionCurves);
                }
            }

            return curves;
        }

        /// <summary>
        /// Project curves to reference plane and keep only closed curves
        /// </summary>
        private List<Curve> ProjectAndClean(List<Curve> curves, Plane referencePlane, double tolerance)
        {
            List<Curve> cleanedCurves = new List<Curve>();

            foreach (Curve curve in curves)
            {
                if (curve == null || !curve.IsValid) continue;

                Curve projectedCurve = Curve.ProjectToPlane(curve, referencePlane);
                if (projectedCurve == null || !projectedCurve.IsValid || !projectedCurve.IsClosed) continue;

                Curve simplified = projectedCurve.Simplify(CurveSimplifyOptions.All, tolerance, 0.01);

                cleanedCurves.Add(simplified != null && simplified.IsValid && simplified.IsClosed
                    ? simplified
                    : projectedCurve);
            }

            return cleanedCurves;
        }

        /// <summary>
        /// Calculate area of closed curve
        /// </summary>
        private double GetCurveArea(Curve curve)
        {
            if (curve == null || !curve.IsClosed) return 0.0;

            AreaMassProperties amp = AreaMassProperties.Compute(curve);
            return amp != null ? Math.Abs(amp.Area) : 0.0;
        }

        /// <summary>
        /// Get boundaries (outermost curves)
        /// </summary>
        private List<Curve> GetBoundaries(List<Curve> sortedCurves, double tolerance)
        {
            List<Curve> boundaries = new List<Curve>();

            for (int i = 0; i < sortedCurves.Count; i++)
            {
                Curve currentCurve = sortedCurves[i];
                bool isContained = false;

                for (int j = 0; j < i; j++)
                {
                    if (IsContainedIn(currentCurve, sortedCurves[j], tolerance))
                    {
                        isContained = true;
                        break;
                    }
                }

                if (!isContained)
                    boundaries.Add(currentCurve);
            }

            return boundaries;
        }

        /// <summary>
        /// Check if inner curve is fully contained within outer curve
        /// </summary>
        private bool IsContainedIn(Curve innerCurve, Curve outerCurve, double tolerance)
        {
            if (innerCurve == null || outerCurve == null) return false;
            if (!innerCurve.IsClosed || !outerCurve.IsClosed) return false;

            // Get plane from outer curve for containment test
            Plane testPlane;
            if (!outerCurve.TryGetPlane(out testPlane, tolerance))
            {
                testPlane = Plane.WorldXY;
            }

            double[] tParams = innerCurve.DivideByCount(5, true);
            if (tParams == null || tParams.Length == 0) return false;

            return tParams.All(t => outerCurve.Contains(innerCurve.PointAt(t), testPlane, tolerance) != PointContainment.Outside);
        }

        /// <summary>
        /// Process boundaries to detect concave angles
        /// </summary>
        private void ProcessBoundariesForConcave(List<Curve> boundaries, Plane referencePlane, double tolerance,
            ref DataTree<Line> concaveCurves, ref DataTree<Point3d> concavePoints)
        {
            int branchIndex = 0;

            foreach (Curve boundary in boundaries)
            {
                if (boundary == null || !boundary.IsValid || !boundary.IsClosed) continue;

                Polyline polyline = ConvertToPolyline(boundary);
                if (polyline == null || polyline.Count < 3) continue;

                EnsureClockwise(ref polyline, referencePlane);

                List<Point3d> points = GetCleanedPoints(polyline);
                if (points.Count < 3) continue;

                DetectConcaveAngles(points, boundary, referencePlane, tolerance, ref concaveCurves, ref concavePoints, ref branchIndex);
            }
        }

        /// <summary>
        /// Detect concave angles in a polyline
        /// </summary>
        private void DetectConcaveAngles(List<Point3d> points, Curve boundary, Plane referencePlane, double tolerance,
            ref DataTree<Line> concaveCurves, ref DataTree<Point3d> concavePoints, ref int branchIndex)
        {
            int n = points.Count;

            for (int i = 0; i < n; i++)
            {
                Point3d pPrev = points[(i - 1 + n) % n];
                Point3d pCurr = points[i];
                Point3d pNext = points[(i + 1) % n];

                if (IsConcaveAngle(pPrev, pCurr, pNext, boundary, referencePlane, tolerance))
                {
                    GH_Path path = new GH_Path(branchIndex);

                    // Add 2 lines
                    concaveCurves.Add(new Line(pPrev, pCurr), path);
                    concaveCurves.Add(new Line(pCurr, pNext), path);

                    // Add 4 points (will be processed later for merging/deduplication)
                    concavePoints.Add(pPrev, path);
                    concavePoints.Add(pCurr, path);
                    concavePoints.Add(pCurr, path);
                    concavePoints.Add(pNext, path);

                    branchIndex++;
                }
            }
        }

        /// <summary>
        /// Convert curve to polyline
        /// </summary>
        private Polyline ConvertToPolyline(Curve curve)
        {
            if (curve == null || !curve.IsValid) return null;

            Polyline polyline;

            // Try direct conversion
            if (curve.TryGetPolyline(out polyline))
                return polyline;

            // Try ToPolyline
            PolylineCurve plCurve = curve.ToPolyline(0, 0, 0.01, 0.1, 0, 0, 0, 0, true);
            if (plCurve != null && plCurve.TryGetPolyline(out polyline))
                return polyline;

            // Divide by length
            double length = curve.GetLength();
            int divisions = Math.Max(10, (int)(length / 10.0));

            double[] tParams = curve.DivideByCount(divisions, true);
            if (tParams != null && tParams.Length > 0)
            {
                Point3d[] divPoints = tParams.Select(t => curve.PointAt(t)).ToArray();
                return new Polyline(divPoints);
            }

            return null;
        }

        /// <summary>
        /// Get cleaned points from polyline (remove duplicates)
        /// </summary>
        private List<Point3d> GetCleanedPoints(Polyline polyline)
        {
            List<Point3d> points = new List<Point3d>(polyline);

            if (points.Count > 0 && points[0].DistanceTo(points[points.Count - 1]) < MIN_POINT_DISTANCE)
                points.RemoveAt(points.Count - 1);

            return points;
        }

        /// <summary>
        /// Check if angle at pCurr is concave
        /// </summary>
        private bool IsConcaveAngle(Point3d pPrev, Point3d pCurr, Point3d pNext, Curve boundary, Plane referencePlane, double tolerance)
        {
            Point3d midpoint = new Point3d(
                (pPrev.X + pNext.X) / 2.0,
                (pPrev.Y + pNext.Y) / 2.0,
                (pPrev.Z + pNext.Z) / 2.0
            );

            Vector3d vector = pCurr - midpoint;
            double length = vector.Length;

            if (length < MIN_CURVE_LENGTH)
                return false;

            vector.Unitize();
            Point3d testPoint = midpoint + vector * tolerance;

            PointContainment containment = boundary.Contains(testPoint, referencePlane, tolerance);

            return containment != PointContainment.Inside;
        }

        /// <summary>
        /// Ensure polyline is clockwise (looking from plane normal direction)
        /// </summary>
        private void EnsureClockwise(ref Polyline polyline, Plane referencePlane)
        {
            if (CalculateSignedArea(polyline, referencePlane) > 0)
            {
                List<Point3d> points = new List<Point3d>(polyline);
                points.Reverse();
                polyline = new Polyline(points);
            }
        }

        /// <summary>
        /// Calculate signed area of polyline (2D projection on reference plane)
        /// Positive = counter-clockwise, Negative = clockwise
        /// </summary>
        private double CalculateSignedArea(Polyline polyline, Plane referencePlane)
        {
            double area = 0;
            int n = polyline.Count;

            // Get plane coordinate system
            Vector3d xAxis = referencePlane.XAxis;
            Vector3d yAxis = referencePlane.YAxis;

            for (int i = 0; i < n - 1; i++)
            {
                // Project points onto plane's 2D coordinate system
                Vector3d v1 = polyline[i] - referencePlane.Origin;
                Vector3d v2 = polyline[i + 1] - referencePlane.Origin;

                double x1 = v1 * xAxis;
                double y1 = v1 * yAxis;
                double x2 = v2 * xAxis;
                double y2 = v2 * yAxis;

                area += (x2 - x1) * (y2 + y1);
            }

            return area / 2.0;
        }
    }
}