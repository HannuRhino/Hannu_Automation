using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;

namespace SlicingTools
{
    public class SliceBoundaryVoidComponent : GH_Component
    {
        private const double MIN_VECTOR_LENGTH = 0.001;
        private const double DEFAULT_TOLERANCE = 0.01;
        private const int DEFAULT_SLICE_COUNT = 20;

        // Tree accumulators — built across SolveInstance calls, flushed in AfterSolveInstance
        private GH_Structure<GH_Curve> _boundaryTree;
        private GH_Structure<GH_Curve> _rectangleTree;
        private GH_Structure<GH_Curve> _sliceGeometryTree;

        public SliceBoundaryVoidComponent()
          : base(
              "Get Boundary",
              "Boundary",
              "Get boundaries and holes from geometry slice",
              "Hannu Automation",
              "Geometry"
          )
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("EC469E3A-ADB2-4EB0-B4E0-57D8B011FA46");
        protected override System.Drawing.Bitmap Icon => Resources.boundary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Geometry to slice (supports Brep, Mesh, Surface, Curve)", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "P", "Reference plane for slicing", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("Distance", "D", "Distance offset along plane normal (default = geometry midpoint)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tolerance", "T", "Tolerance for curve processing", GH_ParamAccess.item, DEFAULT_TOLERANCE);
            pManager.AddBooleanParameter("Use h_max", "hMax", "Slice at maximum height along plane normal", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Use h_min", "hMin", "Slice at minimum height along plane normal", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Slice Count", "N", "Number of slices for boundary detection", GH_ParamAccess.item, DEFAULT_SLICE_COUNT);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;

            // NO Flatten — preserve input tree structure
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Rectangle", "R", "Bounding rectangle per geometry", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Holes", "H", "Hole boundaries", GH_ParamAccess.list);
            pManager.AddCurveParameter("Boundaries", "B", "Solid boundaries from multi-slice union (tree per geometry)", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Slice Geometry", "SG", "Boundaries from slice at distance (tree per geometry)", GH_ParamAccess.tree);
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            _boundaryTree = new GH_Structure<GH_Curve>();
            _rectangleTree = new GH_Structure<GH_Curve>();
            _sliceGeometryTree = new GH_Structure<GH_Curve>();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<object> geometryInputs = new List<object>();
            Plane referencePlane = Plane.WorldXY;
            double distance = 0.0;
            double tolerance = DEFAULT_TOLERANCE;
            bool useHMax = false;
            bool useHMin = false;
            int sliceCount = DEFAULT_SLICE_COUNT;

            if (!DA.GetDataList(0, geometryInputs)) return;
            DA.GetData(1, ref referencePlane);
            bool distanceProvided = DA.GetData(2, ref distance);
            DA.GetData(3, ref tolerance);
            DA.GetData(4, ref useHMax);
            DA.GetData(5, ref useHMin);
            DA.GetData(6, ref sliceCount);

            if (useHMax && useHMin)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Cannot use both h_max and h_min at the same time. Set one to False.");
                return;
            }

            if (!referencePlane.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid reference plane");
                return;
            }

            if (sliceCount < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Invalid slice count ({sliceCount}), using default {DEFAULT_SLICE_COUNT}");
                sliceCount = DEFAULT_SLICE_COUNT;
            }

            if (tolerance <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Invalid tolerance, using {DEFAULT_TOLERANCE}");
                tolerance = DEFAULT_TOLERANCE;
            }

            int iteration = DA.Iteration;

            List<GeometryBase> geometries = new List<GeometryBase>();
            List<Curve> directCurves = new List<Curve>();

            foreach (object input in geometryInputs)
            {
                if (input == null) continue;

                GeometryBase geometry = ExtractGeometryFromWrapper(input);
                if (geometry == null || !geometry.IsValid) continue;

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

            try
            {
                List<Curve> holes = new List<Curve>();

                int subIndex = 0;

                // =============================================================
                // Process each geometry individually → tree output for B, R, SG
                // =============================================================
                foreach (GeometryBase geometry in geometries)
                {
                    if (geometry == null || !geometry.IsValid)
                    {
                        subIndex++;
                        continue;
                    }

                    GH_Path branchPath = new GH_Path(iteration, subIndex);

                    // --- BOUNDARY (multi-slice union) ---
                    Curve boundary = GetBoundaryFromMultiSlice(
                        geometry, referencePlane, sliceCount, tolerance);

                    if (boundary != null && boundary.IsValid && boundary.IsClosed)
                    {
                        _boundaryTree.Append(new GH_Curve(boundary), branchPath);

                        Curve rectangleCurve = CreateRectangleBoundary(boundary, referencePlane);
                        if (rectangleCurve != null && rectangleCurve.IsValid)
                        {
                            _rectangleTree.Append(new GH_Curve(rectangleCurve), branchPath);
                        }

                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"✓ Boundary [{iteration};{subIndex}] ({sliceCount} slices)");
                    }
                    else
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            $"Failed boundary [{iteration};{subIndex}]");
                    }

                    // --- SLICE GEOMETRY (single slice per geometry) ---
                    List<GeometryBase> singleGeometry = new List<GeometryBase> { geometry };
                    double[] hBounds = CalculateHeightBounds(singleGeometry, referencePlane);
                    double h_min = hBounds[0];
                    double h_max = hBounds[1];

                    if (!double.IsInfinity(h_min) && !double.IsInfinity(h_max))
                    {
                        double actualDistance = distance;

                        if (useHMax)
                        {
                            actualDistance = h_max - 0.1;
                        }
                        else if (useHMin)
                        {
                            actualDistance = h_min + 0.1;
                        }
                        else if (!distanceProvided || distance == 0)
                        {
                            actualDistance = (h_min + h_max) / 2.0;
                        }

                        Plane slicePlane = new Plane(referencePlane);
                        slicePlane.Origin = referencePlane.Origin + referencePlane.Normal * actualDistance;

                        List<Curve> slicedCurves = SliceSingleGeometry(geometry, slicePlane, tolerance);

                        if (slicedCurves != null && slicedCurves.Count > 0)
                        {
                            // Join, project, clean
                            Curve[] joinedCurves = Curve.JoinCurves(slicedCurves, tolerance);
                            if (joinedCurves != null)
                            {
                                List<Curve> closedCurves = new List<Curve>();

                                foreach (Curve joined in joinedCurves)
                                {
                                    if (joined == null || !joined.IsValid) continue;

                                    Curve projected = Curve.ProjectToPlane(joined, referencePlane);
                                    if (projected == null || !projected.IsValid) continue;

                                    if (!projected.IsClosed && projected.IsClosable(tolerance))
                                    {
                                        projected.MakeClosed(tolerance);
                                    }

                                    if (!projected.IsClosed) continue;

                                    Curve simplified = projected.Simplify(
                                        CurveSimplifyOptions.All, tolerance, 0.01);
                                    if (simplified != null && simplified.IsValid && simplified.IsClosed)
                                        closedCurves.Add(simplified);
                                    else
                                        closedCurves.Add(projected);
                                }

                                // Classify: boundaries vs holes for this geometry
                                if (closedCurves.Count > 0)
                                {
                                    List<Curve> sgBoundaries;
                                    List<Curve> sgHoles;
                                    ClassifyBoundariesAndHoles(closedCurves, referencePlane,
                                        out sgBoundaries, out sgHoles, tolerance);

                                    foreach (Curve sg in sgBoundaries)
                                    {
                                        _sliceGeometryTree.Append(new GH_Curve(sg), branchPath);
                                    }

                                    holes.AddRange(sgHoles);

                                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                        $"✓ SG [{iteration};{subIndex}] at {actualDistance:F3}: {sgBoundaries.Count} boundary(s), {sgHoles.Count} hole(s)");
                                }
                            }
                        }
                    }

                    subIndex++;
                }

                // --- Direct curves: project to plane ---
                foreach (Curve curve in directCurves)
                {
                    if (curve == null || !curve.IsValid)
                    {
                        subIndex++;
                        continue;
                    }

                    GH_Path branchPath = new GH_Path(iteration, subIndex);

                    Curve projectedCurve = Curve.ProjectToPlane(curve, referencePlane);
                    if (projectedCurve != null && projectedCurve.IsValid && projectedCurve.IsClosed)
                    {
                        _boundaryTree.Append(new GH_Curve(projectedCurve), branchPath);

                        Curve rectangleCurve = CreateRectangleBoundary(projectedCurve, referencePlane);
                        if (rectangleCurve != null && rectangleCurve.IsValid)
                        {
                            _rectangleTree.Append(new GH_Curve(rectangleCurve), branchPath);
                        }
                    }

                    subIndex++;
                }

                // --- Filter holes coincident with boundaries ---
                if (_boundaryTree.DataCount > 0 && holes.Count > 0)
                {
                    List<Curve> allBoundaryCurves = _boundaryTree.AllData(true)
                        .OfType<GH_Curve>()
                        .Select(gh => gh.Value)
                        .Where(c => c != null && c.IsValid && c.IsClosed)
                        .ToList();

                    List<Curve> filteredHoles = new List<Curve>();
                    foreach (Curve boundary in allBoundaryCurves)
                    {
                        List<Curve> valid = FilterHolesCoincidentWithBoundary(
                            holes, boundary, referencePlane, tolerance);
                        filteredHoles.AddRange(valid);
                    }

                    int removedCount = holes.Count - filteredHoles.Count;
                    if (removedCount > 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Filtered {removedCount} coincident hole(s)");
                    }

                    holes = filteredHoles;
                }

                // Flat list output: Holes
                DA.SetDataList(1, holes);

                // Tree outputs (R, B, SG) flushed in AfterSolveInstance
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
            }
        }

        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();

            // Flush Rectangle tree → output 0
            if (_rectangleTree != null && _rectangleTree.DataCount > 0)
            {
                var rectParam = Params.Output[0];
                rectParam.ClearData();
                foreach (GH_Path path in _rectangleTree.Paths)
                {
                    var branch = _rectangleTree.get_Branch(path);
                    for (int i = 0; i < branch.Count; i++)
                    {
                        rectParam.AddVolatileData(path, i, branch[i]);
                    }
                }
            }

            // Flush Boundary tree → output 2
            if (_boundaryTree != null && _boundaryTree.DataCount > 0)
            {
                var boundaryParam = Params.Output[2];
                boundaryParam.ClearData();
                foreach (GH_Path path in _boundaryTree.Paths)
                {
                    var branch = _boundaryTree.get_Branch(path);
                    for (int i = 0; i < branch.Count; i++)
                    {
                        boundaryParam.AddVolatileData(path, i, branch[i]);
                    }
                }
            }

            // Flush Slice Geometry tree → output 3
            if (_sliceGeometryTree != null && _sliceGeometryTree.DataCount > 0)
            {
                var sgParam = Params.Output[3];
                sgParam.ClearData();
                foreach (GH_Path path in _sliceGeometryTree.Paths)
                {
                    var branch = _sliceGeometryTree.get_Branch(path);
                    for (int i = 0; i < branch.Count; i++)
                    {
                        sgParam.AddVolatileData(path, i, branch[i]);
                    }
                }
            }
        }

        // =====================================================================
        // Multi-slice boundary (no mesh)
        // =====================================================================
        private Curve GetBoundaryFromMultiSlice(
            GeometryBase geometry, Plane referencePlane, int sliceCount, double tolerance)
        {
            try
            {
                List<GeometryBase> singleGeometry = new List<GeometryBase> { geometry };
                double[] hBounds = CalculateHeightBounds(singleGeometry, referencePlane);
                double h_min = hBounds[0];
                double h_max = hBounds[1];

                if (double.IsInfinity(h_min) || double.IsInfinity(h_max) || h_max <= h_min)
                    return null;

                double sliceMin = h_min + 0.1;
                double sliceMax = h_max - 0.1;

                if (sliceMin >= sliceMax)
                {
                    sliceMin = (h_min + h_max) / 2.0;
                    sliceMax = sliceMin;
                    sliceCount = 1;
                }

                List<Curve> allProjectedClosed = new List<Curve>();

                for (int i = 0; i < sliceCount; i++)
                {
                    double dist = (sliceCount == 1)
                        ? sliceMin
                        : sliceMin + (sliceMax - sliceMin) * i / (sliceCount - 1);

                    Plane slicePlane = new Plane(referencePlane);
                    slicePlane.Origin = referencePlane.Origin + referencePlane.Normal * dist;

                    List<Curve> slicedCurves = SliceSingleGeometry(geometry, slicePlane, tolerance);
                    if (slicedCurves == null || slicedCurves.Count == 0) continue;

                    Curve[] joinedCurves = Curve.JoinCurves(slicedCurves, tolerance);
                    if (joinedCurves == null || joinedCurves.Length == 0) continue;

                    foreach (Curve joined in joinedCurves)
                    {
                        if (joined == null || !joined.IsValid) continue;

                        Curve projected = Curve.ProjectToPlane(joined, referencePlane);
                        if (projected == null || !projected.IsValid) continue;

                        if (!projected.IsClosed && projected.IsClosable(tolerance))
                        {
                            projected.MakeClosed(tolerance);
                        }

                        if (projected.IsClosed)
                        {
                            allProjectedClosed.Add(projected);
                        }
                    }
                }

                if (allProjectedClosed.Count == 0) return null;
                if (allProjectedClosed.Count == 1) return allProjectedClosed[0];

                try
                {
                    Curve[] unionResult = Curve.CreateBooleanUnion(allProjectedClosed, tolerance);

                    if (unionResult != null && unionResult.Length > 0)
                    {
                        Curve largest = unionResult
                            .Where(c => c != null && c.IsValid && c.IsClosed)
                            .OrderByDescending(c => GetCurveArea(c))
                            .FirstOrDefault();

                        if (largest != null) return largest;
                    }
                }
                catch
                {
                    // Union failed
                }

                return allProjectedClosed
                    .Where(c => c != null && c.IsValid && c.IsClosed)
                    .OrderByDescending(c => GetCurveArea(c))
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // =====================================================================
        // Unchanged helper methods
        // =====================================================================

        private Curve CreateRectangleBoundary(Curve boundary, Plane plane)
        {
            if (boundary == null || !boundary.IsValid) return null;

            BoundingBox bbox = boundary.GetBoundingBox(plane);
            if (!bbox.IsValid) return null;

            Point3d corner1Local = new Point3d(bbox.Min.X, bbox.Min.Y, 0);
            Point3d corner2Local = new Point3d(bbox.Max.X, bbox.Min.Y, 0);
            Point3d corner3Local = new Point3d(bbox.Max.X, bbox.Max.Y, 0);
            Point3d corner4Local = new Point3d(bbox.Min.X, bbox.Max.Y, 0);

            Transform toWorld = Transform.PlaneToPlane(Plane.WorldXY, plane);

            Point3d corner1 = corner1Local; corner1.Transform(toWorld);
            Point3d corner2 = corner2Local; corner2.Transform(toWorld);
            Point3d corner3 = corner3Local; corner3.Transform(toWorld);
            Point3d corner4 = corner4Local; corner4.Transform(toWorld);

            Polyline rectPolyline = new Polyline(new[] { corner1, corner2, corner3, corner4, corner1 });
            return rectPolyline.ToNurbsCurve();
        }

        private List<Curve> FilterHolesCoincidentWithBoundary(
            List<Curve> holes, Curve boundary, Plane plane, double tolerance)
        {
            List<Curve> validHoles = new List<Curve>();
            double coincidenceThreshold = 0.01;

            foreach (Curve hole in holes)
            {
                if (hole == null || !hole.IsValid || !hole.IsClosed) continue;

                Curve simplifiedHole = hole.Simplify(CurveSimplifyOptions.All, tolerance, 0.01);
                if (simplifiedHole == null || !simplifiedHole.IsValid)
                    simplifiedHole = hole;

                List<Curve> holeSegments = new List<Curve>();
                Polyline polyline;

                if (simplifiedHole.TryGetPolyline(out polyline))
                {
                    for (int i = 0; i < polyline.Count - 1; i++)
                    {
                        Line segment = new Line(polyline[i], polyline[i + 1]);
                        holeSegments.Add(segment.ToNurbsCurve());
                    }
                }
                else if (simplifiedHole is PolyCurve polyCurve)
                {
                    Curve[] segments = polyCurve.Explode();
                    if (segments != null && segments.Length > 0)
                    {
                        holeSegments.AddRange(segments);
                    }
                }
                else
                {
                    holeSegments.Add(simplifiedHole);
                }

                bool isCoincidentWithBoundary = false;

                foreach (Curve segment in holeSegments)
                {
                    if (segment == null || !segment.IsValid) continue;

                    double segmentLength = segment.GetLength();
                    if (segmentLength <= 0) continue;

                    Point3d middlePoint = segment.PointAtLength(segmentLength / 2.0);
                    if (!middlePoint.IsValid) continue;

                    double t;
                    if (!boundary.ClosestPoint(middlePoint, out t))
                        continue;

                    Point3d closestPoint = boundary.PointAt(t);
                    double dist = middlePoint.DistanceTo(closestPoint);

                    if (dist < coincidenceThreshold)
                    {
                        isCoincidentWithBoundary = true;
                        break;
                    }
                }

                if (!isCoincidentWithBoundary)
                {
                    validHoles.Add(hole);
                }
            }

            return validHoles;
        }

        private GeometryBase ExtractGeometryFromWrapper(object geometryInput)
        {
            if (geometryInput == null) return null;

            if (geometryInput is GeometryBase geometryBase)
                return geometryBase;

            if (geometryInput is GH_Curve ghCurve && ghCurve.Value != null)
                return ghCurve.Value;

            if (geometryInput is GH_Brep ghBrep && ghBrep.Value != null)
                return ghBrep.Value;

            if (geometryInput is GH_Mesh ghMesh && ghMesh.Value != null)
                return ghMesh.Value;

            if (geometryInput is GH_Surface ghSurface && ghSurface.Value != null)
                return ghSurface.Value;

            if (geometryInput is IGH_GeometricGoo goo)
            {
                if (goo.ScriptVariable() is GeometryBase geo)
                    return geo;
            }

            return null;
        }

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
                    Point3d[] corners = bbox.GetCorners();
                    foreach (Point3d corner in corners)
                    {
                        Vector3d fromPlaneOrigin = corner - referencePlane.Origin;
                        double height = fromPlaneOrigin * referencePlane.Normal;
                        h_min = Math.Min(h_min, height);
                        h_max = Math.Max(h_max, height);
                    }
                }
            }

            return new double[] { h_min, h_max };
        }

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

        private double GetCurveArea(Curve curve)
        {
            if (curve == null || !curve.IsClosed) return 0.0;
            AreaMassProperties amp = AreaMassProperties.Compute(curve);
            return amp != null ? Math.Abs(amp.Area) : 0.0;
        }

        private void ClassifyBoundariesAndHoles(List<Curve> curves, Plane referencePlane,
            out List<Curve> boundaries, out List<Curve> holes, double tolerance)
        {
            boundaries = new List<Curve>();
            holes = new List<Curve>();

            // Sort by area descending
            var sorted = curves
                .Where(c => c != null && c.IsValid && c.IsClosed)
                .OrderByDescending(c => GetCurveArea(c))
                .ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                Curve currentCurve = sorted[i];
                bool isContained = false;

                for (int j = 0; j < i; j++)
                {
                    if (IsContainedIn(currentCurve, sorted[j], referencePlane, tolerance))
                    {
                        isContained = true;
                        break;
                    }
                }

                if (isContained)
                    holes.Add(currentCurve);
                else
                    boundaries.Add(currentCurve);
            }
        }

        private bool IsContainedIn(Curve innerCurve, Curve outerCurve, Plane referencePlane, double tolerance)
        {
            if (innerCurve == null || outerCurve == null) return false;
            if (!innerCurve.IsClosed || !outerCurve.IsClosed) return false;

            double[] tParams = innerCurve.DivideByCount(5, true);
            if (tParams == null || tParams.Length == 0) return false;

            foreach (double t in tParams)
            {
                Point3d testPoint = innerCurve.PointAt(t);
                PointContainment containment = outerCurve.Contains(testPoint, referencePlane, tolerance);

                if (containment == PointContainment.Outside)
                    return false;
            }

            return true;
        }
    }
}