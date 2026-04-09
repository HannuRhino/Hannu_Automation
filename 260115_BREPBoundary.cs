using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SlicingTools
{
    /// <summary>
    /// Multi-Slice Unified Boundary with Holes Extractor
    /// Uses mesh shadow projection to get outer boundary
    /// Uses frequency-based filtering to distinguish through-holes from blind holes
    /// FIXED: Works correctly with any plane, not just World XY
    /// </summary>
    public class MultiSliceBoundaryComponent : GH_Component
    {
        private const double MIN_CURVE_LENGTH = 1e-10;
        private const double MIN_POINT_DISTANCE = 0.001;

        public MultiSliceBoundaryComponent()
          : base(
              "BREPBoundary",
              "Boundary",
              "Extract Boundary from Geometry/Brep",
              "Hannu Automation",
              "Geometry"
          )
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("0AE2772D-FF6A-465E-AE28-5A97F7E5BFD5");
        protected override System.Drawing.Bitmap Icon => Resources.boundary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry/BREP", "G", "Geometry to slice", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "P", "Slice plane (default: World XY)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tolerance", "T", "Tolerance", GH_ParamAccess.item, 0.001);

            pManager[1].Optional = true;
            pManager[2].Optional = true;

            // AUTO FLATTEN INPUT GEOMETRY TREE
            pManager[0].DataMapping = GH_DataMapping.Flatten;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Rectangle", "R", "Bounding rectangle of boundary", GH_ParamAccess.item);
            pManager.AddCurveParameter("Boundary", "B", "Outer boundary from mesh shadow projection", GH_ParamAccess.item);
            pManager.AddCurveParameter("Through Holes", "TH", "Through holes (matched top & bottom)", GH_ParamAccess.list);
            pManager.AddCurveParameter("Blind Holes", "BH", "Blind holes (unmatched)", GH_ParamAccess.list);
        }

        // Enum for surface relationships
        private enum SurfaceRelationship
        {
            Nested,      // Small surface completely inside large surface
            Overlapping, // Surfaces partially overlap
            Separate     // Surfaces don't touch
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            List<GeometryBase> geometries = new List<GeometryBase>();
            Plane slicePlane = Plane.WorldXY;
            double tolerance = 0.001;

            if (!DA.GetDataList(0, geometries)) return;
            DA.GetData(1, ref slicePlane);
            DA.GetData(2, ref tolerance);

            // Validate inputs
            if (geometries == null || geometries.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No geometries provided");
                return;
            }

            if (!slicePlane.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid plane, using World XY");
                slicePlane = Plane.WorldXY;
            }

            if (tolerance <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid tolerance, using 0.001");
                tolerance = 0.001;
            }
            List<Brep> flattenedSurfaces = null;

            try
            {
                // ============================================
                // PART 1: MESH SHADOW → SURFACES → FLATTEN → BOUNDARIES → BOOLEAN UNION
                // ============================================

                Curve finalBoundary = null;

                try
                {
                    // Step 1: Create mesh from geometries
                    Mesh unifiedMesh = CreateUnifiedMesh(geometries, tolerance);

                    if (unifiedMesh == null || !unifiedMesh.IsValid || unifiedMesh.Vertices.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to create valid mesh");
                        return;
                    }

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Created mesh with {unifiedMesh.Vertices.Count} vertices");

                    // Step 2: Get shadow outlines (chiếu vuông góc theo vector pháp tuyến của plane)
                    Polyline[] outlines = unifiedMesh.GetOutlines(slicePlane);

                    if (outlines == null || outlines.Length == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            $"No outlines found from mesh shadow. Mesh vertices: {unifiedMesh.Vertices.Count}, Faces: {unifiedMesh.Faces.Count}");
                        return;
                    }

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Got {outlines.Length} outline(s) from mesh shadow projection (along plane normal)");

                    // Step 3: Create surfaces from outlines
                    List<Brep> surfaces = CreateSurfacesFromOutlines(outlines, slicePlane, tolerance);

                    if (surfaces.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No surfaces created from outlines");
                        return;
                    }

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Created {surfaces.Count} surface(s) from shadow outlines");

                    // Step 4: Flatten surfaces (nested + overlapping)
                    flattenedSurfaces = FlattenSurfacesList(surfaces, slicePlane, tolerance);

                    if (flattenedSurfaces == null || flattenedSurfaces.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to flatten surfaces");
                        return;
                    }

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Flattened to {flattenedSurfaces.Count} surface(s)");

                    // Step 5: Extract boundaries from ALL flattened surfaces
                    List<Curve> allBoundaries = new List<Curve>();

                    foreach (Brep surface in flattenedSurfaces)
                    {
                        Curve boundary = ExtractOuterBoundaryFromBrep(surface, tolerance);
                        if (boundary != null && boundary.IsValid && boundary.IsClosed)
                        {
                            allBoundaries.Add(boundary);
                        }
                    }

                    if (allBoundaries.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to extract any boundaries from surfaces");
                        return;
                    }

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Extracted {allBoundaries.Count} boundary curve(s) from flattened surfaces");

                    // Step 6: BOOLEAN UNION all boundaries to create single unified boundary
                    finalBoundary = FlattenBoundaryList(allBoundaries, tolerance);

                    if (finalBoundary == null || !finalBoundary.IsValid || !finalBoundary.IsClosed)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to create final unified boundary");
                        return;
                    }

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "✓ Successfully created single unified boundary from Boolean Union");
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Boundary extraction failed: {ex.Message}\nStack: {ex.StackTrace}");
                    return;
                }

                // ============================================
                // PART 1.5: CREATE RECTANGLE BOUNDARY
                // ============================================

                Curve rectangleBoundary = null;

                if (finalBoundary != null && finalBoundary.IsValid)
                {
                    try
                    {
                        rectangleBoundary = CreateRectangleBoundary(finalBoundary, slicePlane);

                        if (rectangleBoundary != null && rectangleBoundary.IsValid && rectangleBoundary.IsClosed)
                        {
                            BoundingBox bbox = finalBoundary.GetBoundingBox(slicePlane);
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                $"✓ Created rectangle boundary on plane: {bbox.Max.X - bbox.Min.X:F2} × {bbox.Max.Y - bbox.Min.Y:F2}");
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to create valid rectangle boundary");
                            rectangleBoundary = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            $"Rectangle creation failed: {ex.Message}");
                        rectangleBoundary = null;
                    }
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No boundary available for rectangle creation");
                }

                // ============================================
                // PART 2: SLICE AT Z_MAX AND Z_MIN FOR HOLES DETECTION
                // ============================================

                BoundingBox unionBBox = GetUnionBoundingBox(geometries, slicePlane);

                if (!unionBBox.IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cannot calculate bounding box");
                    return;
                }

                double zMin = unionBBox.Min.Z;
                double zMax = unionBBox.Max.Z;
                double totalHeight = zMax - zMin;

                List<Curve> throughHolesOutput = new List<Curve>();
                List<Curve> blindHolesOutput = new List<Curve>();

                if (totalHeight <= tolerance)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Geometry has no height, no holes detected");
                }
                else
                {
                    // Slice at Z_max (top)
                    Plane topPlane = new Plane(slicePlane);
                    topPlane.Origin = slicePlane.Origin + slicePlane.ZAxis * zMax;

                    List<Curve> topSliceCurves = SliceGeometries(geometries, topPlane, tolerance);
                    List<Curve> topCleanedCurves = ProjectAndClean(topSliceCurves, slicePlane, tolerance);
                    topCleanedCurves = topCleanedCurves.OrderByDescending(c => GetCurveArea(c)).ToList();

                    List<Curve> topBoundaries = new List<Curve>();
                    List<Curve> topHoles = new List<Curve>();
                    SeparateBoundariesAndHoles(topCleanedCurves, out topBoundaries, out topHoles, slicePlane, tolerance);

                    // Slice at Z_min (bottom)
                    Plane bottomPlane = new Plane(slicePlane);
                    bottomPlane.Origin = slicePlane.Origin + slicePlane.ZAxis * zMin;

                    List<Curve> bottomSliceCurves = SliceGeometries(geometries, bottomPlane, tolerance);
                    List<Curve> bottomCleanedCurves = ProjectAndClean(bottomSliceCurves, slicePlane, tolerance);
                    bottomCleanedCurves = bottomCleanedCurves.OrderByDescending(c => GetCurveArea(c)).ToList();

                    List<Curve> bottomBoundaries = new List<Curve>();
                    List<Curve> bottomHoles = new List<Curve>();
                    SeparateBoundariesAndHoles(bottomCleanedCurves, out bottomBoundaries, out bottomHoles, slicePlane, tolerance);

                    // Match holes between top and bottom
                    List<Curve> matchedThroughHoles = new List<Curve>();
                    List<bool> bottomHolesMatched = new List<bool>(new bool[bottomHoles.Count]);

                    foreach (Curve topHole in topHoles)
                    {
                        bool foundMatch = false;

                        for (int i = 0; i < bottomHoles.Count; i++)
                        {
                            if (AreHolesSimilarByPosition(topHole, bottomHoles[i], slicePlane, tolerance))
                            {
                                matchedThroughHoles.Add(topHole); // Use top hole as representative
                                bottomHolesMatched[i] = true;
                                foundMatch = true;
                                break;
                            }
                        }

                        if (!foundMatch)
                        {
                            blindHolesOutput.Add(topHole);
                        }
                    }

                    // Add unmatched bottom holes as blind holes
                    for (int i = 0; i < bottomHoles.Count; i++)
                    {
                        if (!bottomHolesMatched[i])
                        {
                            blindHolesOutput.Add(bottomHoles[i]);
                        }
                    }

                    // Filter holes that coincide with boundary
                    if (finalBoundary != null)
                    {
                        throughHolesOutput = FilterHolesCoincidentWithBoundary(matchedThroughHoles, finalBoundary, slicePlane, tolerance);
                        blindHolesOutput = FilterHolesCoincidentWithBoundary(blindHolesOutput, finalBoundary, slicePlane, tolerance);

                        // ============================================
                        // REMOVE DUPLICATE HOLES
                        // ============================================
                        throughHolesOutput = RemoveDuplicateHoles(throughHolesOutput, slicePlane, tolerance);
                        blindHolesOutput = RemoveDuplicateHoles(blindHolesOutput, slicePlane, tolerance);
                    }
                    else
                    {
                        throughHolesOutput = matchedThroughHoles;

                        // Remove duplicates even without boundary
                        throughHolesOutput = RemoveDuplicateHoles(throughHolesOutput, slicePlane, tolerance);
                        blindHolesOutput = RemoveDuplicateHoles(blindHolesOutput, slicePlane, tolerance);
                    }
                }

                // ============================================
                // SET OUTPUTS
                // ============================================

                DA.SetData(0, rectangleBoundary);           // [0] Rectangle
                DA.SetData(1, finalBoundary);               // [1] Boundary
                DA.SetDataList(2, throughHolesOutput);      // [2] Through Holes
                DA.SetDataList(3, blindHolesOutput);        // [3] Blind Holes

                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"✓ Created rectangle + boundary from mesh shadow + flatten + Boolean Union, " +
                    $"{throughHolesOutput.Count} through hole(s), " +
                    $"{blindHolesOutput.Count} blind hole(s), ");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
            }
        }

        // ============================================
        // MESH SHADOW → SURFACES → FLATTEN → BOUNDARY METHODS
        // ============================================

        /// <summary>
        /// Create unified mesh from all geometries
        /// </summary>
        private Mesh CreateUnifiedMesh(List<GeometryBase> geometries, double tolerance)
        {
            MeshingParameters meshParams = new MeshingParameters();

            // Use FAST meshing for better compatibility
            meshParams.MinimumEdgeLength = 0.0001;
            meshParams.MaximumEdgeLength = 0.0;
            meshParams.GridAmplification = 1.0;
            meshParams.GridAngle = 20.0;
            meshParams.GridAspectRatio = 6.0;
            meshParams.RefineGrid = true;
            meshParams.SimplePlanes = false;
            meshParams.JaggedSeams = false;
            meshParams.Tolerance = 0.0;
            meshParams.RelativeTolerance = 0.0;
            meshParams.MinimumTolerance = 0.0;

            List<Mesh> allMeshes = new List<Mesh>();
            int skippedCount = 0;

            foreach (GeometryBase geom in geometries)
            {
                if (geom == null || !geom.IsValid)
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    if (geom is Mesh mesh)
                    {
                        if (mesh.IsValid && mesh.Vertices.Count > 0)
                        {
                            allMeshes.Add(mesh);
                        }
                        else
                        {
                            skippedCount++;
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
                        else
                        {
                            skippedCount++;
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
                            else
                            {
                                skippedCount++;
                            }
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Failed to mesh geometry: {ex.Message}");
                    skippedCount++;
                }
            }

            if (skippedCount > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Skipped {skippedCount} geometry/geometries");
            }

            if (allMeshes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No valid meshes created from any geometry");
                return null;
            }

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Created {allMeshes.Count} mesh(es) from geometries");

            // Join all meshes
            Mesh joinedMesh = new Mesh();
            foreach (Mesh m in allMeshes)
            {
                if (m != null && m.IsValid && m.Vertices.Count > 0)
                {
                    joinedMesh.Append(m);
                }
            }

            // Compact and validate
            if (joinedMesh.IsValid && joinedMesh.Vertices.Count > 0)
            {
                joinedMesh.Compact();
                joinedMesh.Normals.ComputeNormals();
                joinedMesh.FaceNormals.ComputeFaceNormals();

                return joinedMesh;
            }

            return null;
        }

        /// <summary>
        /// Create surfaces from shadow outlines
        /// </summary>
        private List<Brep> CreateSurfacesFromOutlines(Polyline[] outlines, Plane plane, double tolerance)
        {
            List<Brep> surfaces = new List<Brep>();

            foreach (Polyline outline in outlines)
            {
                if (outline == null || !outline.IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid outline polyline");
                    continue;
                }

                if (!outline.IsClosed)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Outline is not closed, attempting to close");

                    // Try to close the polyline
                    if (outline.Count > 2)
                    {
                        double dist = outline[0].DistanceTo(outline[outline.Count - 1]);
                        if (dist > tolerance && dist < tolerance * 100)
                        {
                            outline.Add(outline[0]); // Close manually
                        }
                    }

                    if (!outline.IsClosed)
                        continue;
                }

                Curve outlineCurve = outline.ToNurbsCurve();
                if (outlineCurve == null || !outlineCurve.IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to convert polyline to curve");
                    continue;
                }

                if (!outlineCurve.IsClosed)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Outline curve is not closed");
                    continue;
                }

                // Simplify outline
                Curve simplified = outlineCurve.Simplify(CurveSimplifyOptions.All, tolerance, 0.01);
                if (simplified != null && simplified.IsValid && simplified.IsClosed)
                    outlineCurve = simplified;

                // Check if curve is planar
                if (!outlineCurve.IsPlanar(tolerance))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Outline curve is not planar, projecting to plane");
                    outlineCurve = Curve.ProjectToPlane(outlineCurve, plane);

                    if (outlineCurve == null || !outlineCurve.IsValid || !outlineCurve.IsClosed)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to project curve to plane");
                        continue;
                    }
                }

                // Create planar surface from outline
                Brep[] breps = Brep.CreatePlanarBreps(outlineCurve, tolerance);

                if (breps == null || breps.Length == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Failed to create planar Brep from outline (Length: {outlineCurve.GetLength():F2})");
                    continue;
                }

                surfaces.AddRange(breps);
            }

            return surfaces;
        }

        /// <summary>
        /// Flatten surfaces list - Remove nested surfaces and union overlapping surfaces
        /// Returns: List of flattened surfaces (may still be multiple if they're separate)
        /// </summary>
        private List<Brep> FlattenSurfacesList(List<Brep> surfaces, Plane plane, double tolerance)
        {
            List<Brep> result = new List<Brep>();

            if (surfaces == null || surfaces.Count == 0)
                return result;

            if (surfaces.Count == 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Only 1 surface, no flattening needed");
                result.Add(surfaces[0]);
                return result;
            }

            // Sort by area (largest first)
            List<Brep> sortedSurfaces = surfaces
                .OrderByDescending(s => GetBrepArea(s))
                .ToList();

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Flattening {sortedSurfaces.Count} surfaces...");

            // Track which surfaces have been processed
            List<bool> processed = new List<bool>(new bool[sortedSurfaces.Count]);

            // Process each surface
            for (int i = 0; i < sortedSurfaces.Count; i++)
            {
                if (processed[i]) continue;

                Brep currentSurface = sortedSurfaces[i];
                if (currentSurface == null || !currentSurface.IsValid) continue;

                // Check against remaining surfaces
                List<Brep> toUnion = new List<Brep> { currentSurface };
                processed[i] = true;

                for (int j = i + 1; j < sortedSurfaces.Count; j++)
                {
                    if (processed[j]) continue;

                    Brep candidateSurface = sortedSurfaces[j];
                    if (candidateSurface == null || !candidateSurface.IsValid) continue;

                    // Check relationship
                    SurfaceRelationship relationship = CheckSurfaceRelationship(candidateSurface, currentSurface, plane, tolerance);

                    if (relationship == SurfaceRelationship.Nested)
                    {
                        // Case 1: Nested → Skip (mark as processed)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Surface {j} is nested in surface {i} → Skipped");
                        processed[j] = true;
                    }
                    else if (relationship == SurfaceRelationship.Overlapping)
                    {
                        // Case 2: Overlapping → Add to union list
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Surface {j} overlaps surface {i} → Will union");
                        toUnion.Add(candidateSurface);
                        processed[j] = true;
                    }
                    // Case 3: Separate → Leave it for next iteration
                }

                // If we have surfaces to union, do it
                if (toUnion.Count == 1)
                {
                    // No union needed
                    result.Add(toUnion[0]);
                }
                else
                {
                    // Perform Boolean Union
                    try
                    {
                        Brep[] unionResult = Brep.CreateBooleanUnion(toUnion, tolerance);

                        if (unionResult != null && unionResult.Length > 0)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                $"Boolean Union successful: {toUnion.Count} surfaces → {unionResult.Length} surface(s)");
                            result.AddRange(unionResult);
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                                $"Boolean Union failed for {toUnion.Count} surfaces, keeping originals");
                            result.AddRange(toUnion);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            $"Boolean Union error: {ex.Message}, keeping originals");
                        result.AddRange(toUnion);
                    }
                }
            }

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Flattening complete: {surfaces.Count} → {result.Count} surface(s)");

            return result;
        }

        /// <summary>
        /// Check relationship between two surfaces (FIXED: uses plane parameter)
        /// </summary>
        private SurfaceRelationship CheckSurfaceRelationship(Brep smallSurface, Brep largeSurface, Plane plane, double tolerance)
        {
            if (smallSurface == null || largeSurface == null)
                return SurfaceRelationship.Separate;

            // Get boundaries
            Curve smallBoundary = ExtractOuterBoundaryFromBrep(smallSurface, tolerance);
            Curve largeBoundary = ExtractOuterBoundaryFromBrep(largeSurface, tolerance);

            if (smallBoundary == null || largeBoundary == null)
                return SurfaceRelationship.Separate;

            // Check if small is nested in large (FIXED: uses plane)
            if (IsContainedIn(smallBoundary, largeBoundary, plane, tolerance))
            {
                return SurfaceRelationship.Nested;
            }

            // Check if they overlap by testing intersection
            try
            {
                var intersectionEvents = Rhino.Geometry.Intersect.Intersection.CurveCurve(
                    smallBoundary,
                    largeBoundary,
                    tolerance,
                    tolerance);

                if (intersectionEvents != null && intersectionEvents.Count > 0)
                {
                    // They intersect → Overlapping
                    return SurfaceRelationship.Overlapping;
                }

                // No intersection - check if small boundary has any point inside large
                double[] tParams = smallBoundary.DivideByCount(10, true);
                if (tParams != null && tParams.Length > 0)
                {
                    Point3d testPoint = smallBoundary.PointAt(tParams[0]);
                    PointContainment containment = largeBoundary.Contains(testPoint, plane, tolerance);

                    if (containment == PointContainment.Inside)
                    {
                        // Inside but not nested → This means overlapping
                        return SurfaceRelationship.Overlapping;
                    }
                }

                // Completely separate
                return SurfaceRelationship.Separate;
            }
            catch
            {
                // If intersection fails, assume separate
                return SurfaceRelationship.Separate;
            }
        }

        /// <summary>
        /// Get area of Brep
        /// </summary>
        private double GetBrepArea(Brep brep)
        {
            if (brep == null || !brep.IsValid)
                return 0.0;

            AreaMassProperties amp = AreaMassProperties.Compute(brep);
            return amp != null ? Math.Abs(amp.Area) : 0.0;
        }

        /// <summary>
        /// Extract outer boundary curve from a single Brep
        /// </summary>
        private Curve ExtractOuterBoundaryFromBrep(Brep brep, double tolerance)
        {
            if (brep == null || !brep.IsValid)
                return null;

            BrepEdgeList edges = brep.Edges;
            if (edges == null || edges.Count == 0)
                return null;

            List<Curve> edgeCurves = new List<Curve>();

            foreach (BrepEdge edge in edges)
            {
                if (edge != null && edge.IsValid)
                {
                    Curve edgeCurve = edge.DuplicateCurve();
                    if (edgeCurve != null)
                    {
                        edgeCurves.Add(edgeCurve);
                    }
                }
            }

            if (edgeCurves.Count == 0)
                return null;

            // Join edge curves
            Curve[] joined = Curve.JoinCurves(edgeCurves, tolerance);

            if (joined == null || joined.Length == 0)
                return null;

            // Return the largest closed curve
            List<Curve> closedCurves = joined
                .Where(c => c != null && c.IsValid && c.IsClosed)
                .OrderByDescending(c => GetCurveArea(c))
                .ToList();

            return closedCurves.Count > 0 ? closedCurves[0] : null;
        }

        /// <summary>
        /// Flatten boundary list using Boolean Union to ensure single unified boundary
        /// Input: List of boundary curves (may be 1 or multiple)
        /// Output: Single unified boundary curve
        /// </summary>
        private Curve FlattenBoundaryList(List<Curve> boundaries, double tolerance)
        {
            if (boundaries == null || boundaries.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No boundaries to flatten");
                return null;
            }

            // Case 1: Only 1 boundary → Return directly
            if (boundaries.Count == 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Only 1 boundary curve, no union needed");
                return boundaries[0];
            }

            // Case 2: Multiple boundaries → Boolean Union
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Flattening {boundaries.Count} boundary curve(s) using Boolean Union...");

            try
            {
                // Perform Boolean Union on all boundary curves
                Curve[] unionResult = Curve.CreateBooleanUnion(boundaries, tolerance);

                if (unionResult == null || unionResult.Length == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "Boolean Union returned no results, using largest boundary");

                    // Fallback: Return the largest boundary
                    return boundaries.OrderByDescending(c => GetCurveArea(c)).First();
                }

                // Check result count
                if (unionResult.Length == 1)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "✓ Boolean Union successful → 1 unified boundary");
                    return unionResult[0];
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Boolean Union returned {unionResult.Length} curves, taking the largest");

                    // Take the largest curve (outer boundary)
                    List<Curve> sortedUnion = unionResult
                        .Where(c => c != null && c.IsValid && c.IsClosed)
                        .OrderByDescending(c => GetCurveArea(c))
                        .ToList();

                    if (sortedUnion.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            "No valid closed curves from Boolean Union");
                        return null;
                    }

                    return sortedUnion[0];
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Boolean Union failed: {ex.Message}, using largest boundary");

                // Fallback: Return the largest boundary
                if (boundaries.Count > 0)
                {
                    return boundaries.OrderByDescending(c => GetCurveArea(c)).First();
                }

                return null;
            }
        }

        /// <summary>
        /// Create rectangle boundary on arbitrary plane (FIXED: works with any plane)
        /// </summary>
        private Curve CreateRectangleBoundary(Curve boundary, Plane plane)
        {
            if (boundary == null || !boundary.IsValid)
                return null;

            // Get bounding box on the slice plane
            BoundingBox bbox = boundary.GetBoundingBox(plane);

            if (!bbox.IsValid)
                return null;

            // Transform bounding box corners to plane coordinates
            // bbox is in plane's local coordinate system
            Point3d corner1Local = new Point3d(bbox.Min.X, bbox.Min.Y, 0);
            Point3d corner2Local = new Point3d(bbox.Max.X, bbox.Min.Y, 0);
            Point3d corner3Local = new Point3d(bbox.Max.X, bbox.Max.Y, 0);
            Point3d corner4Local = new Point3d(bbox.Min.X, bbox.Max.Y, 0);

            // Transform from plane coordinates to world coordinates
            Transform toWorld = Transform.PlaneToPlane(Plane.WorldXY, plane);

            Point3d corner1 = corner1Local;
            Point3d corner2 = corner2Local;
            Point3d corner3 = corner3Local;
            Point3d corner4 = corner4Local;

            corner1.Transform(toWorld);
            corner2.Transform(toWorld);
            corner3.Transform(toWorld);
            corner4.Transform(toWorld);

            // Create rectangle polyline (closed, counter-clockwise)
            Polyline rectPolyline = new Polyline(new[] { corner1, corner2, corner3, corner4, corner1 });

            // Convert to curve
            Curve rectangleCurve = rectPolyline.ToNurbsCurve();

            return rectangleCurve;
        }

        // ============================================
        // HOLES DETECTION METHODS (FIXED: all use plane parameter)
        // ============================================

        /// <summary>
        /// Remove duplicate holes based on centroid position and area (FIXED: uses plane)
        /// </summary>
        private List<Curve> RemoveDuplicateHoles(List<Curve> holes, Plane plane, double tolerance)
        {
            if (holes == null || holes.Count <= 1)
                return holes;

            List<Curve> uniqueHoles = new List<Curve>();
            double duplicateThreshold = tolerance * 10; // 0.01 with tolerance = 0.001

            foreach (Curve hole in holes)
            {
                if (hole == null || !hole.IsValid || !hole.IsClosed)
                    continue;

                bool isDuplicate = false;

                // Check against already added unique holes
                foreach (Curve existingHole in uniqueHoles)
                {
                    if (AreHolesSimilar(hole, existingHole, plane, duplicateThreshold))
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    uniqueHoles.Add(hole);
                }
            }

            int removedCount = holes.Count - uniqueHoles.Count;
            if (removedCount > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Removed {removedCount} duplicate hole(s)");
            }

            return uniqueHoles;
        }

        /// <summary>
        /// Check if two holes are similar (for duplicate detection) - FIXED: uses plane
        /// Uses tighter threshold than position matching
        /// </summary>
        private bool AreHolesSimilar(Curve hole1, Curve hole2, Plane plane, double threshold)
        {
            if (hole1 == null || hole2 == null) return false;
            if (!hole1.IsValid || !hole2.IsValid) return false;
            if (!hole1.IsClosed || !hole2.IsClosed) return false;

            // Get areas
            double area1 = GetCurveArea(hole1);
            double area2 = GetCurveArea(hole2);

            // Check area difference (within 5% tolerance - stricter than 20%)
            double areaDiff = Math.Abs(area1 - area2);
            double areaThreshold = Math.Max(area1, area2) * 0.05;

            if (areaDiff > areaThreshold)
                return false;

            // Get centroids
            AreaMassProperties amp1 = AreaMassProperties.Compute(hole1);
            AreaMassProperties amp2 = AreaMassProperties.Compute(hole2);

            if (amp1 == null || amp2 == null)
                return false;

            Point3d centroid1 = amp1.Centroid;
            Point3d centroid2 = amp2.Centroid;

            // FIXED: Calculate distance in plane's 2D space
            double centroidDistance = DistanceInPlane(centroid1, centroid2, plane);

            // Holes are duplicates if centroids are very close
            return centroidDistance < threshold;
        }

        /// <summary>
        /// Filter holes that coincide with boundary (FIXED: uses plane)
        /// </summary>
        private List<Curve> FilterHolesCoincidentWithBoundary(List<Curve> holes, Curve boundary, Plane plane, double tolerance)
        {
            List<Curve> validHoles = new List<Curve>();

            if (boundary == null || !boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid boundary for hole filtering");
                return holes;
            }

            double coincidenceThreshold = 0.01;

            foreach (Curve hole in holes)
            {
                if (hole == null || !hole.IsValid || !hole.IsClosed)
                    continue;

                // Step 1: Simplify hole
                Curve simplifiedHole = hole.Simplify(CurveSimplifyOptions.All, tolerance, 0.01);
                if (simplifiedHole == null || !simplifiedHole.IsValid)
                    simplifiedHole = hole;

                // Step 2: Explode hole into segments (this is our group)
                List<Curve> holeSegments = new List<Curve>();

                Polyline polyline;
                if (simplifiedHole.TryGetPolyline(out polyline))
                {
                    // Convert polyline to segments
                    for (int i = 0; i < polyline.Count - 1; i++)
                    {
                        Line segment = new Line(polyline[i], polyline[i + 1]);
                        holeSegments.Add(segment.ToNurbsCurve());
                    }
                }
                else if (simplifiedHole is PolyCurve polyCurve)
                {
                    // Explode polycurve
                    Curve[] segments = polyCurve.Explode();
                    if (segments != null && segments.Length > 0)
                    {
                        holeSegments.AddRange(segments);
                    }
                }
                else
                {
                    // Cannot explode, use whole curve as single segment
                    holeSegments.Add(simplifiedHole);
                }

                if (holeSegments.Count == 0)
                    continue;

                // Step 3: Check if ANY segment's middle point is coincident with boundary
                bool isCoincidentWithBoundary = false;

                foreach (Curve segment in holeSegments)
                {
                    if (segment == null || !segment.IsValid)
                        continue;

                    // Get middle point of segment
                    double segmentLength = segment.GetLength();
                    if (segmentLength <= 0)
                        continue;

                    Point3d middlePoint = segment.PointAtLength(segmentLength / 2.0);
                    if (!middlePoint.IsValid)
                        continue;

                    // Find closest point on boundary
                    double t;
                    if (!boundary.ClosestPoint(middlePoint, out t))
                        continue;

                    Point3d closestPoint = boundary.PointAt(t);
                    double distance = middlePoint.DistanceTo(closestPoint);

                    // If distance < threshold → coincident
                    if (distance < coincidenceThreshold)
                    {
                        isCoincidentWithBoundary = true;
                        break; // Found one coincident segment, reject entire hole
                    }
                }

                // Step 4: Only keep holes that are NOT coincident with boundary
                if (!isCoincidentWithBoundary)
                {
                    validHoles.Add(hole); // Keep original hole (not simplified/exploded)
                }
            }

            return validHoles;
        }

        /// <summary>
        /// Check if two holes are similar using centroid and area (for through-hole matching)
        /// FIXED: uses plane parameter for 2D distance calculation
        /// </summary>
        private bool AreHolesSimilarByPosition(Curve hole1, Curve hole2, Plane plane, double tolerance)
        {
            if (hole1 == null || !hole1.IsValid || !hole1.IsClosed) return false;
            if (hole2 == null || !hole2.IsValid || !hole2.IsClosed) return false;

            // Get areas
            double area1 = GetCurveArea(hole1);
            double area2 = GetCurveArea(hole2);

            // Check area difference (within 20% tolerance)
            double areaDiff = Math.Abs(area1 - area2);
            double areaThreshold = Math.Max(area1, area2) * 0.2;

            if (areaDiff > areaThreshold)
                return false;

            // Get centroids
            AreaMassProperties amp1 = AreaMassProperties.Compute(hole1);
            AreaMassProperties amp2 = AreaMassProperties.Compute(hole2);

            if (amp1 == null || amp2 == null)
                return false;

            Point3d centroid1 = amp1.Centroid;
            Point3d centroid2 = amp2.Centroid;

            // FIXED: Calculate distance in plane's 2D space (not hardcoded XY)
            double centroidDistance = DistanceInPlane(centroid1, centroid2, plane);

            // Holes are similar if centroids are close (within tolerance * 50)
            return centroidDistance < tolerance * 50;
        }

        /// <summary>
        /// Calculate 2D distance between two points in plane's coordinate system
        /// NEW HELPER METHOD
        /// </summary>
        private double DistanceInPlane(Point3d pt1, Point3d pt2, Plane plane)
        {
            // Transform points to plane's local coordinate system
            Transform toPlane = Transform.PlaneToPlane(plane, Plane.WorldXY);

            Point3d localPt1 = new Point3d(pt1);
            Point3d localPt2 = new Point3d(pt2);

            localPt1.Transform(toPlane);
            localPt2.Transform(toPlane);

            // Calculate 2D distance (ignore Z)
            double dx = localPt1.X - localPt2.X;
            double dy = localPt1.Y - localPt2.Y;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Separate curves into outer boundaries and holes based on containment
        /// FIXED: uses plane parameter
        /// </summary>
        private void SeparateBoundariesAndHoles(List<Curve> sortedCurves, out List<Curve> boundaries,
            out List<Curve> holes, Plane plane, double tolerance)
        {
            boundaries = new List<Curve>();
            holes = new List<Curve>();

            if (sortedCurves.Count == 0) return;

            boundaries.Add(sortedCurves[0]);

            for (int i = 1; i < sortedCurves.Count; i++)
            {
                Curve currentCurve = sortedCurves[i];
                bool isContained = false;

                foreach (Curve boundary in boundaries)
                {
                    if (IsContainedIn(currentCurve, boundary, plane, tolerance))
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

        /// <summary>
        /// Check if inner curve is fully contained within outer curve
        /// FIXED: uses plane parameter instead of Plane.WorldXY
        /// </summary>
        private bool IsContainedIn(Curve innerCurve, Curve outerCurve, Plane plane, double tolerance)
        {
            if (innerCurve == null || outerCurve == null) return false;
            if (!innerCurve.IsClosed || !outerCurve.IsClosed) return false;

            double[] tParams = innerCurve.DivideByCount(10, true);
            if (tParams == null || tParams.Length == 0) return false;

            int insideCount = 0;
            foreach (double t in tParams)
            {
                Point3d testPoint = innerCurve.PointAt(t);
                // FIXED: Use the plane parameter instead of Plane.WorldXY
                PointContainment containment = outerCurve.Contains(testPoint, plane, tolerance);

                if (containment == PointContainment.Inside || containment == PointContainment.Coincident)
                    insideCount++;
            }

            return insideCount >= tParams.Length * 0.8;
        }

        /// <summary>
        /// Get union bounding box of all geometries relative to plane
        /// </summary>
        private BoundingBox GetUnionBoundingBox(List<GeometryBase> geometries, Plane plane)
        {
            BoundingBox bbox = BoundingBox.Empty;

            foreach (GeometryBase geometry in geometries)
            {
                if (geometry == null || !geometry.IsValid) continue;

                BoundingBox geomBox = geometry.GetBoundingBox(plane);
                bbox.Union(geomBox);
            }

            return bbox;
        }

        /// <summary>
        /// Slice all geometries and collect curves
        /// </summary>
        private List<Curve> SliceGeometries(List<GeometryBase> geometries, Plane slicePlane, double tolerance)
        {
            List<Curve> allCurves = new List<Curve>();

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
        /// Project curves to plane and keep only closed curves
        /// </summary>
        private List<Curve> ProjectAndClean(List<Curve> curves, Plane plane, double tolerance)
        {
            List<Curve> cleanedCurves = new List<Curve>();

            foreach (Curve curve in curves)
            {
                if (curve == null || !curve.IsValid) continue;

                Curve projectedCurve = Curve.ProjectToPlane(curve, plane);
                if (projectedCurve == null || !projectedCurve.IsValid || !projectedCurve.IsClosed) continue;

                Curve simplified = projectedCurve.Simplify(CurveSimplifyOptions.All, tolerance, 0.01);

                if (simplified != null && simplified.IsValid && simplified.IsClosed)
                    cleanedCurves.Add(simplified);
                else
                    cleanedCurves.Add(projectedCurve);
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
    }
}