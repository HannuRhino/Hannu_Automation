using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;

namespace Slicinggeofast
{
    public class SliceBoundaryVoidComponent : GH_Component
    {
        private const double MIN_VECTOR_LENGTH = 0.001;
        private const double DEFAULT_TOLERANCE = 0.01;

        public SliceBoundaryVoidComponent()
          : base(
              "Get Boundary (FAST)",
              "Boundary",
              "Get boundaries and holes from geometry slice",
              "Hannu Automation",
              "Geometry"
          )
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("3DC0AF70-A72C-468F-9ECC-D49FE6A34B29");
        protected override System.Drawing.Bitmap Icon => null;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Geometry to slice (supports Brep, Mesh, Surface, Curve)", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "P", "Reference plane for slicing", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddNumberParameter("Distance", "D", "Distance offset along plane normal (default = geometry midpoint)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tolerance", "T", "Tolerance for curve processing", GH_ParamAccess.item, DEFAULT_TOLERANCE);
            pManager.AddBooleanParameter("Use h_max", "hMax", "Slice at maximum height along plane normal", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Use h_min", "hMin", "Slice at minimum height along plane normal", GH_ParamAccess.item, false);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;

            pManager[0].DataMapping = GH_DataMapping.Flatten;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddRectangleParameter("Rectangle", "R", "Union bounding rectangle", GH_ParamAccess.item);
            pManager.AddCurveParameter("Holes", "H", "Hole boundaries", GH_ParamAccess.list);
            pManager.AddCurveParameter("Boundaries", "B", "Solid boundaries (from mesh shadow)", GH_ParamAccess.list);
            pManager.AddCurveParameter("Slice Geometry", "SG", "Boundaries from slice at distance", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
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

            if (tolerance <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Invalid tolerance, using {DEFAULT_TOLERANCE}");
                tolerance = DEFAULT_TOLERANCE;
            }

            try
            {
                List<Curve> allRectangles = new List<Curve>();
                List<Curve> allMeshBoundaries = new List<Curve>();
                List<Curve> sliceBoundaries = new List<Curve>();
                List<Curve> holes = new List<Curve>();

                foreach (GeometryBase geometry in geometries)
                {
                    if (geometry == null || !geometry.IsValid) continue;

                    List<GeometryBase> singleGeometry = new List<GeometryBase> { geometry };

                    Curve meshBoundary = GetBoundaryFromMeshShadow(singleGeometry, referencePlane, tolerance);

                    if (meshBoundary != null && meshBoundary.IsValid && meshBoundary.IsClosed)
                    {
                        allMeshBoundaries.Add(meshBoundary);

                        Curve rectangleBoundary = CreateRectangleBoundary(meshBoundary, referencePlane);
                        if (rectangleBoundary != null && rectangleBoundary.IsValid)
                        {
                            allRectangles.Add(rectangleBoundary);
                        }

                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"✓ Created boundary and rectangle for geometry");
                    }
                    else
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            "Failed to create boundary from one geometry");
                    }
                }

                foreach (Curve curve in directCurves)
                {
                    if (curve == null || !curve.IsValid) continue;

                    Curve projectedCurve = Curve.ProjectToPlane(curve, referencePlane);

                    if (projectedCurve != null && projectedCurve.IsValid && projectedCurve.IsClosed)
                    {
                        allMeshBoundaries.Add(projectedCurve);

                        Curve rectangleBoundary = CreateRectangleBoundary(projectedCurve, referencePlane);
                        if (rectangleBoundary != null && rectangleBoundary.IsValid)
                        {
                            allRectangles.Add(rectangleBoundary);
                        }

                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"✓ Created boundary and rectangle for curve");
                    }
                }

                if (geometries.Count > 0)
                {
                    double actualDistance = distance;

                    double[] hBounds = CalculateHeightBounds(geometries, referencePlane);
                    double h_min = hBounds[0];
                    double h_max = hBounds[1];

                    if (!double.IsInfinity(h_min) && !double.IsInfinity(h_max))
                    {
                        if (useHMax)
                        {
                            actualDistance = h_max - 0.1;
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                $"Slicing at h_max - tolerance = {actualDistance:F3} (h_max={h_max:F3})");
                        }
                        else if (useHMin)
                        {
                            actualDistance = h_min + 0.1;
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                $"Slicing at h_min + tolerance = {actualDistance:F3} (h_min={h_min:F3})");
                        }
                        else if (!distanceProvided || distance == 0)
                        {
                            actualDistance = (h_min + h_max) / 2.0;
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                $"Auto distance = {actualDistance:F3} (midpoint between h_min={h_min:F3} and h_max={h_max:F3})");
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                $"Using manual distance = {actualDistance:F3}");
                        }

                        var slicedCurvesWithIndex = SliceGeometriesWithIndex(geometries, referencePlane, actualDistance, tolerance);
                        var cleanedCurvesWithIndex = ProjectAndCleanWithIndex(slicedCurvesWithIndex, referencePlane, tolerance);

                        if (cleanedCurvesWithIndex.Count > 0)
                        {
                            List<Curve> tempSliceBoundaries = new List<Curve>();
                            List<Curve> tempSliceHoles = new List<Curve>();
                            ClassifyBoundariesAndHolesWithOrder(cleanedCurvesWithIndex, referencePlane,
                                out tempSliceBoundaries, out tempSliceHoles, tolerance);

                            sliceBoundaries.AddRange(tempSliceBoundaries);
                            holes.AddRange(tempSliceHoles);

                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                $"✓ Slice at distance {actualDistance:F3}: {tempSliceBoundaries.Count} boundary/boundaries, {tempSliceHoles.Count} hole(s)");
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                                $"No closed curves found from slice at distance {actualDistance:F3}");
                        }
                    }
                }

                if (allMeshBoundaries.Count > 0 && holes.Count > 0)
                {
                    List<Curve> filteredHoles = new List<Curve>();
                    foreach (Curve boundary in allMeshBoundaries)
                    {
                        List<Curve> holesForThisBoundary = FilterHolesCoincidentWithBoundary(
                            holes, boundary, referencePlane, tolerance);
                        filteredHoles.AddRange(holesForThisBoundary);
                    }

                    int removedCount = holes.Count - filteredHoles.Count;
                    if (removedCount > 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Filtered out {removedCount} hole(s) coincident with boundary");
                    }

                    holes = filteredHoles;
                }

                DA.SetDataList(0, allRectangles);
                DA.SetDataList(1, holes);
                DA.SetDataList(2, allMeshBoundaries);
                DA.SetDataList(3, sliceBoundaries);

                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"✓ Outputs: {allRectangles.Count} rectangle(s), {holes.Count} hole(s), {allMeshBoundaries.Count} boundary/boundaries, {sliceBoundaries.Count} slice boundary/boundaries");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
            }
        }

        private Curve GetBoundaryFromMeshShadow(List<GeometryBase> geometries, Plane plane, double tolerance)
        {
            try
            {
                Mesh unifiedMesh = CreateUnifiedMesh(geometries, tolerance);

                if (unifiedMesh == null || !unifiedMesh.IsValid || unifiedMesh.Vertices.Count == 0)
                {
                    return null;
                }

                Polyline[] outlines = unifiedMesh.GetOutlines(plane);

                if (outlines == null || outlines.Length == 0)
                {
                    return null;
                }

                List<Curve> outlineCurves = new List<Curve>();

                foreach (Polyline outline in outlines)
                {
                    if (outline == null || !outline.IsValid) continue;

                    if (!outline.IsClosed && outline.Count > 2)
                    {
                        double dist = outline[0].DistanceTo(outline[outline.Count - 1]);
                        if (dist > tolerance && dist < tolerance * 100)
                        {
                            outline.Add(outline[0]);
                        }
                    }

                    if (!outline.IsClosed) continue;

                    Curve outlineCurve = outline.ToNurbsCurve();
                    if (outlineCurve == null || !outlineCurve.IsValid || !outlineCurve.IsClosed) continue;

                    Curve simplified = outlineCurve.Simplify(CurveSimplifyOptions.All, tolerance, 0.01);
                    if (simplified != null && simplified.IsValid && simplified.IsClosed)
                        outlineCurve = simplified;

                    if (!outlineCurve.IsPlanar(tolerance))
                    {
                        outlineCurve = Curve.ProjectToPlane(outlineCurve, plane);
                        if (outlineCurve == null || !outlineCurve.IsValid || !outlineCurve.IsClosed)
                            continue;
                    }

                    outlineCurves.Add(outlineCurve);
                }

                if (outlineCurves.Count == 0)
                {
                    return null;
                }

                if (outlineCurves.Count == 1)
                {
                    return outlineCurves[0];
                }
                else
                {
                    try
                    {
                        Curve[] unionResult = Curve.CreateBooleanUnion(outlineCurves, tolerance);

                        if (unionResult != null && unionResult.Length > 0)
                        {
                            if (unionResult.Length == 1)
                            {
                                return unionResult[0];
                            }
                            else
                            {
                                List<Curve> sorted = unionResult
                                    .Where(c => c != null && c.IsValid && c.IsClosed)
                                    .OrderByDescending(c => GetCurveArea(c))
                                    .ToList();

                                if (sorted.Count > 0)
                                {
                                    return sorted[0];
                                }
                            }
                        }

                        return outlineCurves.OrderByDescending(c => GetCurveArea(c)).First();
                    }
                    catch
                    {
                        return outlineCurves.OrderByDescending(c => GetCurveArea(c)).First();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private Mesh CreateUnifiedMesh(List<GeometryBase> geometries, double tolerance)
        {
            MeshingParameters meshParams = new MeshingParameters();
            meshParams.MinimumEdgeLength = 0.0001;
            meshParams.MaximumEdgeLength = 0.0;
            meshParams.GridAmplification = 1.0;
            meshParams.GridAngle = 20.0;
            meshParams.GridAspectRatio = 6.0;
            meshParams.RefineGrid = true;
            meshParams.SimplePlanes = false;
            meshParams.JaggedSeams = false;

            List<Mesh> allMeshes = new List<Mesh>();

            foreach (GeometryBase geom in geometries)
            {
                if (geom == null || !geom.IsValid) continue;

                try
                {
                    if (geom is Mesh mesh)
                    {
                        if (mesh.IsValid && mesh.Vertices.Count > 0)
                        {
                            allMeshes.Add(mesh);
                        }
                    }
                    else if (geom is Brep brep)
                    {
                        Mesh[] meshes = Mesh.CreateFromBrep(brep, meshParams);
                        if (meshes != null && meshes.Length > 0)
                        {
                            foreach (Mesh m in meshes)
                            {
                                if (m != null && m.IsValid && m.Vertices.Count > 0)
                                {
                                    allMeshes.Add(m);
                                }
                            }
                        }
                    }
                    else if (geom is Surface surface)
                    {
                        Brep surfaceBrep = surface.ToBrep();
                        if (surfaceBrep != null && surfaceBrep.IsValid)
                        {
                            Mesh[] meshes = Mesh.CreateFromBrep(surfaceBrep, meshParams);
                            if (meshes != null && meshes.Length > 0)
                            {
                                foreach (Mesh m in meshes)
                                {
                                    if (m != null && m.IsValid && m.Vertices.Count > 0)
                                    {
                                        allMeshes.Add(m);
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            if (allMeshes.Count == 0)
                return null;

            Mesh joinedMesh = new Mesh();
            foreach (Mesh m in allMeshes)
            {
                if (m != null && m.IsValid && m.Vertices.Count > 0)
                {
                    joinedMesh.Append(m);
                }
            }

            if (joinedMesh.IsValid && joinedMesh.Vertices.Count > 0)
            {
                joinedMesh.Compact();
                joinedMesh.Normals.ComputeNormals();
                joinedMesh.FaceNormals.ComputeFaceNormals();
                return joinedMesh;
            }

            return null;
        }

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

        private List<Curve> FilterHolesCoincidentWithBoundary(List<Curve> holes, Curve boundary, Plane plane, double tolerance)
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
                    double distance = middlePoint.DistanceTo(closestPoint);

                    if (distance < coincidenceThreshold)
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

        private List<(Curve curve, int geometryIndex)> SliceGeometriesWithIndex(
            List<GeometryBase> geometries, Plane referencePlane, double distance, double tolerance)
        {
            List<(Curve, int)> allCurvesWithIndex = new List<(Curve, int)>();
            Plane slicePlane = new Plane(referencePlane);
            slicePlane.Origin = referencePlane.Origin + referencePlane.Normal * distance;

            for (int i = 0; i < geometries.Count; i++)
            {
                GeometryBase geometry = geometries[i];
                if (geometry == null || !geometry.IsValid) continue;

                List<Curve> curves = SliceSingleGeometry(geometry, slicePlane, tolerance);

                if (curves != null && curves.Count > 0)
                {
                    foreach (Curve curve in curves)
                    {
                        allCurvesWithIndex.Add((curve, i));
                    }
                }
            }

            return allCurvesWithIndex;
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

        private List<(Curve curve, int geometryIndex)> ProjectAndCleanWithIndex(
            List<(Curve curve, int geometryIndex)> curvesWithIndex,
            Plane referencePlane, double tolerance)
        {
            List<(Curve, int)> cleanedCurves = new List<(Curve, int)>();

            foreach (var (curve, index) in curvesWithIndex)
            {
                if (curve == null || !curve.IsValid) continue;

                Curve projectedCurve = Curve.ProjectToPlane(curve, referencePlane);
                if (projectedCurve == null || !projectedCurve.IsValid || !projectedCurve.IsClosed) continue;

                Curve simplified = projectedCurve.Simplify(CurveSimplifyOptions.All, tolerance, 0.01);

                if (simplified != null && simplified.IsValid && simplified.IsClosed)
                    cleanedCurves.Add((simplified, index));
                else
                    cleanedCurves.Add((projectedCurve, index));
            }

            return cleanedCurves;
        }

        private double GetCurveArea(Curve curve)
        {
            if (curve == null || !curve.IsClosed) return 0.0;
            AreaMassProperties amp = AreaMassProperties.Compute(curve);
            return amp != null ? Math.Abs(amp.Area) : 0.0;
        }

        private void ClassifyBoundariesAndHolesWithOrder(
            List<(Curve curve, int geometryIndex)> curvesWithIndex,
            Plane referencePlane,
            out List<Curve> boundaries,
            out List<Curve> holes,
            double tolerance)
        {
            boundaries = new List<Curve>();
            holes = new List<Curve>();

            if (curvesWithIndex.Count == 0) return;

            var sortedByArea = curvesWithIndex
                .OrderByDescending(pair => GetCurveArea(pair.curve))
                .ToList();

            List<(Curve curve, int geometryIndex)> boundariesWithIndex = new List<(Curve, int)>();
            List<(Curve curve, int geometryIndex)> holesWithIndex = new List<(Curve, int)>();

            for (int i = 0; i < sortedByArea.Count; i++)
            {
                var current = sortedByArea[i];
                bool isContained = false;

                for (int j = 0; j < i; j++)
                {
                    if (IsContainedIn(current.curve, sortedByArea[j].curve, referencePlane, tolerance))
                    {
                        isContained = true;
                        break;
                    }
                }

                if (isContained)
                    holesWithIndex.Add(current);
                else
                    boundariesWithIndex.Add(current);
            }

            boundaries = boundariesWithIndex
                .OrderBy(pair => pair.geometryIndex)
                .Select(pair => pair.curve)
                .ToList();

            holes = holesWithIndex
                .Select(pair => pair.curve)
                .ToList();
        }

        private void ClassifyBoundariesAndHoles(List<Curve> sortedCurves, Plane referencePlane,
            out List<Curve> boundaries, out List<Curve> holes, double tolerance)
        {
            boundaries = new List<Curve>();
            holes = new List<Curve>();

            for (int i = 0; i < sortedCurves.Count; i++)
            {
                Curve currentCurve = sortedCurves[i];
                bool isContained = false;

                for (int j = 0; j < i; j++)
                {
                    if (IsContainedIn(currentCurve, sortedCurves[j], referencePlane, tolerance))
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