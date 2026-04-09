using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;

namespace YourNamespace
{
    /// <summary>
    /// SORT BY CENTROID COMPONENT
    /// 
    /// Sorts geometric objects by their centroid coordinates on X, Y, and Z axes.
    /// 
    /// SUPPORTED TYPES:
    /// - Surface
    /// - Point
    /// - Line
    /// - Polyline
    /// - Rectangle
    /// - Vector
    /// - Curve (all types)
    /// - Brep (solid and surface)
    /// - Mesh
    /// - Extrusion
    /// 
    /// FEATURES:
    /// - Accurate centroid calculation using MassProperties
    /// - Independent sorting by each axis (X, Y, Z)
    /// - Outputs coordinate values (double) instead of Point3d
    /// - Handles DataTree structures with multiple branches
    /// </summary>
    public class SortByCentroidComponent : GH_Component
    {
        #region METADATA & CONSTRUCTOR

        /// <summary>
        /// Component constructor
        /// </summary>
        public SortByCentroidComponent()
          : base(
              "Sort XYZ",
              "SrtXYZ",
              "Sort geometry objects by their centroid coordinates on X, Y, and Z axes",
              "Hannu Automation",
              "Others"
          )
        {
        }

        /// <summary>
        /// Component GUID - UNIQUE IDENTIFIER
        /// </summary>
        public override Guid ComponentGuid => new Guid("217E719C-5BDA-4076-AEDB-559917278A13");

        /// <summary>
        /// Component icon (null = default icon)
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.Sort_XYZ;

        /// <summary>
        /// Exposure level in Grasshopper UI
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;

        #endregion

        #region INPUT PARAMETERS

        /// <summary>
        /// Register all INPUT parameters
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Input 0: Keys (Geometry objects to sort)
            pManager.AddGeometryParameter(
                "Keys",
                "K",
                "Geometry objects to sort: Surface, Point, Line, Polyline, Rectangle, Vector, Curve, Brep, Mesh, Extrusion",
                GH_ParamAccess.tree
            );

            // Input 1: Values (Associated values)
            pManager.AddGenericParameter(
                "Values",
                "V",
                "Values associated with each geometry object (optional - if not provided, Keys will be used as Values)",
                GH_ParamAccess.tree
            );

            // Make Values optional
            pManager[1].Optional = true;
        }

        #endregion

        #region OUTPUT PARAMETERS

        /// <summary>
        /// Register all OUTPUT parameters
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // X-axis outputs
            pManager.AddNumberParameter(
                "KeysX",
                "KX",
                "X coordinates of centroids sorted along X axis",
                GH_ParamAccess.tree
            );

            pManager.AddGenericParameter(
                "ValuesX",
                "VX",
                "Values sorted by X axis",
                GH_ParamAccess.tree
            );

            // Y-axis outputs
            pManager.AddNumberParameter(
                "KeysY",
                "KY",
                "Y coordinates of centroids sorted along Y axis",
                GH_ParamAccess.tree
            );

            pManager.AddGenericParameter(
                "ValuesY",
                "VY",
                "Values sorted by Y axis",
                GH_ParamAccess.tree
            );

            // Z-axis outputs
            pManager.AddNumberParameter(
                "KeysZ",
                "KZ",
                "Z coordinates of centroids sorted along Z axis",
                GH_ParamAccess.tree
            );

            pManager.AddGenericParameter(
                "ValuesZ",
                "VZ",
                "Values sorted by Z axis",
                GH_ParamAccess.tree
            );
        }

        #endregion

        #region MAIN EXECUTION

        /// <summary>
        /// Main execution method - called when inputs change
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════
            // STEP 1: GET INPUT DATA
            // ═══════════════════════════════════════════════════════

            GH_Structure<IGH_GeometricGoo> keysTree;
            GH_Structure<IGH_Goo> valuesTree;

            if (!DA.GetDataTree(0, out keysTree))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to retrieve Keys data");
                return;
            }

            if (!DA.GetDataTree(1, out valuesTree))
            {
                // Values not provided - will use Keys as Values
                valuesTree = null;
            }

            // ═══════════════════════════════════════════════════════
            // STEP 2: VALIDATE INPUT
            // ═══════════════════════════════════════════════════════

            if (keysTree == null || keysTree.IsEmpty)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Keys tree is empty");
                return;
            }

            // ═══════════════════════════════════════════════════════
            // STEP 3: INITIALIZE OUTPUT TREES
            // ═══════════════════════════════════════════════════════

            var keysXTree = new DataTree<double>();
            var valuesXTree = new DataTree<object>();
            var keysYTree = new DataTree<double>();
            var valuesYTree = new DataTree<object>();
            var keysZTree = new DataTree<double>();
            var valuesZTree = new DataTree<object>();

            // ═══════════════════════════════════════════════════════
            // STEP 4: PROCESS EACH BRANCH
            // ═══════════════════════════════════════════════════════

            for (int i = 0; i < keysTree.PathCount; i++)
            {
                GH_Path path = keysTree.Paths[i];
                List<IGH_GeometricGoo> keysBranch = keysTree.get_Branch(path) as List<IGH_GeometricGoo>;

                if (keysBranch == null || keysBranch.Count == 0)
                {
                    continue;
                }

                // Get corresponding values branch
                List<object> valuesBranch = GetValuesBranch(valuesTree, path, keysBranch);

                // Handle size mismatch
                if (keysBranch.Count != valuesBranch.Count)
                {
                    int minCount = Math.Min(keysBranch.Count, valuesBranch.Count);
                    keysBranch = keysBranch.GetRange(0, minCount);
                    valuesBranch = valuesBranch.GetRange(0, minCount);

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Branch {path}: Keys and Values have different sizes - trimmed to {minCount} items");
                }

                // Extract centroids from geometries
                var (centroids, validValues) = ExtractCentroids(keysBranch, valuesBranch);

                if (centroids.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Branch {path}: No valid geometry found");
                    continue;
                }

                // Sort and output for each axis
                ProcessAxisSorting(centroids, validValues, path,
                    keysXTree, valuesXTree, keysYTree, valuesYTree, keysZTree, valuesZTree);
            }

            // ═══════════════════════════════════════════════════════
            // STEP 5: SET OUTPUT DATA
            // ═══════════════════════════════════════════════════════

            DA.SetDataTree(0, keysXTree);
            DA.SetDataTree(1, valuesXTree);
            DA.SetDataTree(2, keysYTree);
            DA.SetDataTree(3, valuesYTree);
            DA.SetDataTree(4, keysZTree);
            DA.SetDataTree(5, valuesZTree);
        }

        #endregion

        #region HELPER METHODS

        /// <summary>
        /// Get values branch corresponding to keys branch
        /// </summary>
        private List<object> GetValuesBranch(
            GH_Structure<IGH_Goo> valuesTree,
            GH_Path path,
            List<IGH_GeometricGoo> keysBranch)
        {
            // If no values tree provided, use keys as values
            if (valuesTree == null || !valuesTree.PathExists(path))
            {
                return keysBranch.Cast<object>().ToList();
            }

            List<IGH_Goo> valuesBranch = valuesTree.get_Branch(path) as List<IGH_Goo>;

            if (valuesBranch == null || valuesBranch.Count == 0)
            {
                return keysBranch.Cast<object>().ToList();
            }

            // Convert IGH_Goo to object using ScriptVariable
            return valuesBranch.Select(v => v != null ? v.ScriptVariable() : null).ToList();
        }

        /// <summary>
        /// Extract valid centroids from geometry list.
        /// Centroids and Values are ROUNDED TO 2 DECIMAL PLACES before being added to the list,
        /// so sorting is always performed on rounded values.
        /// </summary>
        private (List<Point3d> centroids, List<object> values) ExtractCentroids(
            List<IGH_GeometricGoo> geometries,
            List<object> values)
        {
            List<Point3d> centroids = new List<Point3d>();
            List<object> validValues = new List<object>();

            for (int i = 0; i < geometries.Count; i++)
            {
                if (geometries[i] == null)
                {
                    continue;
                }

                Point3d centroid = Point3d.Unset;

                // Try multiple methods to extract Point3d

                // Method 1: Direct GH_Point check
                if (geometries[i] is GH_Point)
                {
                    centroid = ((GH_Point)geometries[i]).Value;
                }
                // Method 2: Check for GH_Line
                else if (geometries[i] is GH_Line)
                {
                    Line line = ((GH_Line)geometries[i]).Value;
                    centroid = line.PointAt(0.5); // Midpoint of line
                }
                // Method 3: Check for GH_Rectangle
                else if (geometries[i] is GH_Rectangle)
                {
                    Rectangle3d rect = ((GH_Rectangle)geometries[i]).Value;
                    centroid = rect.Center; // Rectangle center point
                }
                // Method 4: Check for GH_Curve (includes polylines)
                else if (geometries[i] is GH_Curve)
                {
                    Curve curve = ((GH_Curve)geometries[i]).Value;
                    if (curve != null && curve.IsValid)
                    {
                        centroid = GetAccurateCentroid(curve);
                    }
                }
                // Method 5: Check for GH_Vector
                else if (geometries[i] is GH_Vector)
                {
                    Vector3d vector = ((GH_Vector)geometries[i]).Value;
                    // Vector doesn't have position, use endpoint from origin
                    centroid = new Point3d(vector.X / 2, vector.Y / 2, vector.Z / 2);
                }
                else
                {
                    // Method 6: Try casting to GH_Point
                    GH_Point ghPoint;
                    if (geometries[i].CastTo(out ghPoint))
                    {
                        centroid = ghPoint.Value;
                    }
                    // Method 7: Try casting to GH_Rectangle
                    else
                    {
                        GH_Rectangle ghRect;
                        if (geometries[i].CastTo(out ghRect))
                        {
                            Rectangle3d rect = ghRect.Value;
                            centroid = rect.Center;
                        }
                        // Method 8: Try casting to GH_Curve
                        else
                        {
                            GH_Curve ghCurve;
                            if (geometries[i].CastTo(out ghCurve) && ghCurve != null)
                            {
                                Curve curve = ghCurve.Value;
                                if (curve != null && curve.IsValid)
                                {
                                    centroid = GetAccurateCentroid(curve);
                                }
                            }
                            // Method 9: Try casting to GH_Vector
                            else
                            {
                                GH_Vector ghVector;
                                if (geometries[i].CastTo(out ghVector))
                                {
                                    Vector3d vector = ghVector.Value;
                                    centroid = new Point3d(vector.X / 2, vector.Y / 2, vector.Z / 2);
                                }
                                else
                                {
                                    // Method 10: Try ScriptVariable for other geometry types
                                    object scriptVar = geometries[i].ScriptVariable();

                                    if (scriptVar is Point3d)
                                    {
                                        centroid = (Point3d)scriptVar;
                                    }
                                    else if (scriptVar is Line)
                                    {
                                        Line line = (Line)scriptVar;
                                        centroid = line.PointAt(0.5);
                                    }
                                    else if (scriptVar is Rectangle3d)
                                    {
                                        Rectangle3d rect = (Rectangle3d)scriptVar;
                                        centroid = rect.Center;
                                    }
                                    else if (scriptVar is Polyline)
                                    {
                                        Polyline polyline = (Polyline)scriptVar;
                                        Curve curve = polyline.ToNurbsCurve();
                                        if (curve != null && curve.IsValid)
                                        {
                                            centroid = GetAccurateCentroid(curve);
                                        }
                                    }
                                    else if (scriptVar is Vector3d)
                                    {
                                        Vector3d vector = (Vector3d)scriptVar;
                                        centroid = new Point3d(vector.X / 2, vector.Y / 2, vector.Z / 2);
                                    }
                                    else if (scriptVar is Curve)
                                    {
                                        Curve curve = scriptVar as Curve;
                                        if (curve != null && curve.IsValid)
                                        {
                                            centroid = GetAccurateCentroid(curve);
                                        }
                                    }
                                    else if (scriptVar is GeometryBase)
                                    {
                                        GeometryBase geo = scriptVar as GeometryBase;
                                        centroid = GetAccurateCentroid(geo);
                                    }
                                }
                            }
                        }
                    }
                }

                if (centroid.IsValid)
                {
                    centroid = new Point3d(
                        Math.Round(centroid.X, 2, MidpointRounding.AwayFromZero),
                        Math.Round(centroid.Y, 2, MidpointRounding.AwayFromZero),
                        Math.Round(centroid.Z, 2, MidpointRounding.AwayFromZero)
                    );

                    centroids.Add(centroid);

                    object val = values[i];
                    if (val is Point3d pt)
                    {
                        val = new Point3d(
                            Math.Round(pt.X, 2, MidpointRounding.AwayFromZero),
                            Math.Round(pt.Y, 2, MidpointRounding.AwayFromZero),
                            Math.Round(pt.Z, 2, MidpointRounding.AwayFromZero)
                        );
                    }

                    validValues.Add(val);
                }
                // ─────────────────────────────────────────────────────
            }

            return (centroids, validValues);
        }

        /// <summary>
        /// Process sorting for all three axes and populate output trees
        /// </summary>
        private void ProcessAxisSorting(
            List<Point3d> centroids,
            List<object> values,
            GH_Path path,
            DataTree<double> keysXTree,
            DataTree<object> valuesXTree,
            DataTree<double> keysYTree,
            DataTree<object> valuesYTree,
            DataTree<double> keysZTree,
            DataTree<object> valuesZTree)
        {
            // Sort by X axis
            var sortedX = SortByAxis(centroids, values, Axis.X);
            keysXTree.AddRange(sortedX.coordinates, path);
            valuesXTree.AddRange(sortedX.sortedValues, path);

            // Sort by Y axis
            var sortedY = SortByAxis(centroids, values, Axis.Y);
            keysYTree.AddRange(sortedY.coordinates, path);
            valuesYTree.AddRange(sortedY.sortedValues, path);

            // Sort by Z axis
            var sortedZ = SortByAxis(centroids, values, Axis.Z);
            keysZTree.AddRange(sortedZ.coordinates, path);
            valuesZTree.AddRange(sortedZ.sortedValues, path);
        }

        /// <summary>
        /// Calculate accurate centroid for any geometry type using MassProperties
        /// </summary>
        private Point3d GetAccurateCentroid(GeometryBase geo)
        {
            if (geo == null)
                return Point3d.Unset;

            try
            {
                // ========== POINT ==========
                if (geo is Rhino.Geometry.Point)
                {
                    var pt = ((Rhino.Geometry.Point)geo).Location;
                    return pt.IsValid ? pt : Point3d.Unset;
                }

                // ========== LINE ==========
                if (geo is LineCurve)
                {
                    LineCurve lineCurve = (LineCurve)geo;
                    return lineCurve.Line.PointAt(0.5); // Midpoint
                }

                // ========== POLYLINE ==========
                if (geo is PolylineCurve)
                {
                    PolylineCurve polyCurve = (PolylineCurve)geo;
                    if (!polyCurve.IsValid) return Point3d.Unset;

                    // For closed polylines, use AreaMassProperties
                    if (polyCurve.IsClosed)
                    {
                        AreaMassProperties amp = AreaMassProperties.Compute(polyCurve);
                        if (amp != null && amp.Centroid.IsValid)
                        {
                            return amp.Centroid;
                        }
                    }

                    // For open polylines, use mid-point
                    return polyCurve.PointAt(polyCurve.Domain.Mid);
                }

                // ========== CURVE (General) ==========
                if (geo is Curve)
                {
                    Curve crv = (Curve)geo;
                    if (!crv.IsValid) return Point3d.Unset;

                    // For closed curves, use AreaMassProperties (more accurate)
                    if (crv.IsClosed)
                    {
                        AreaMassProperties amp = AreaMassProperties.Compute(crv);
                        if (amp != null && amp.Centroid.IsValid)
                        {
                            return amp.Centroid;
                        }
                    }

                    // Fallback: use curve mid-point
                    return crv.PointAt(crv.Domain.Mid);
                }

                // ========== BREP ==========
                if (geo is Brep)
                {
                    Brep brep = (Brep)geo;
                    if (!brep.IsValid) return Point3d.Unset;

                    // For solid breps, use VolumeMassProperties
                    if (brep.IsSolid)
                    {
                        VolumeMassProperties vmp = VolumeMassProperties.Compute(brep);
                        if (vmp != null && vmp.Centroid.IsValid)
                        {
                            return vmp.Centroid;
                        }
                    }

                    // For surface breps, use AreaMassProperties
                    AreaMassProperties amp = AreaMassProperties.Compute(brep);
                    if (amp != null && amp.Centroid.IsValid)
                    {
                        return amp.Centroid;
                    }

                    // Fallback: use bounding box center
                    return brep.GetBoundingBox(true).Center;
                }

                // ========== SURFACE ==========
                if (geo is Surface)
                {
                    Surface srf = (Surface)geo;
                    if (!srf.IsValid) return Point3d.Unset;

                    AreaMassProperties amp = AreaMassProperties.Compute(srf);
                    if (amp != null && amp.Centroid.IsValid)
                    {
                        return amp.Centroid;
                    }

                    // Fallback: use surface domain mid-point
                    return srf.PointAt(srf.Domain(0).Mid, srf.Domain(1).Mid);
                }

                // ========== MESH ==========
                if (geo is Mesh)
                {
                    Mesh mesh = (Mesh)geo;
                    if (!mesh.IsValid) return Point3d.Unset;

                    // For closed meshes, use VolumeMassProperties
                    if (mesh.IsClosed)
                    {
                        VolumeMassProperties vmp = VolumeMassProperties.Compute(mesh);
                        if (vmp != null && vmp.Centroid.IsValid)
                        {
                            return vmp.Centroid;
                        }
                    }

                    // Fallback: use AreaMassProperties
                    AreaMassProperties amp = AreaMassProperties.Compute(mesh);
                    if (amp != null && amp.Centroid.IsValid)
                    {
                        return amp.Centroid;
                    }

                    // Final fallback: use bounding box center
                    return mesh.GetBoundingBox(true).Center;
                }

                // ========== EXTRUSION ==========
                if (geo is Extrusion)
                {
                    Extrusion ext = (Extrusion)geo;
                    Brep brep = ext.ToBrep();
                    if (brep != null)
                    {
                        return GetAccurateCentroid(brep); // Recursive call
                    }
                }

                // ========== FALLBACK: BOUNDING BOX CENTER ==========
                BoundingBox bbox = geo.GetBoundingBox(true);
                if (bbox.IsValid)
                {
                    return bbox.Center;
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Error calculating centroid: {ex.Message}");
            }

            return Point3d.Unset;
        }

        /// <summary>
        /// Sort centroids and their associated values by specified axis.
        /// Centroids are already rounded at this point (done in ExtractCentroids).
        /// When values on the sort axis are equal, original input order is preserved (OriginalIndex tiebreaker).
        /// </summary>
        private (List<double> coordinates, List<object> sortedValues) SortByAxis(
            List<Point3d> centroids,
            List<object> values,
            Axis axis)
        {
            // Handle empty or null input
            if (centroids == null || centroids.Count == 0)
            {
                return (new List<double>(), new List<object>());
            }

            // Single item - no sorting needed
            if (centroids.Count == 1)
            {
                double coord = GetCoordinate(centroids[0], axis);
                return (new List<double> { coord }, values);
            }

            // Pair centroids with their values and original index
            List<CentroidValuePair> pairs = new List<CentroidValuePair>(centroids.Count);

            for (int i = 0; i < centroids.Count; i++)
            {
                pairs.Add(new CentroidValuePair
                {
                    Centroid = centroids[i],
                    Value = values[i],
                    OriginalIndex = i
                });
            }

            // Sort pairs by specified axis
            List<CentroidValuePair> sortedPairs;

            switch (axis)
            {
                case Axis.X:
                    sortedPairs = pairs
                        .OrderBy(p => p.Centroid.X)
                        .ThenBy(p => p.OriginalIndex)
                        .ToList();
                    break;
                case Axis.Y:
                    sortedPairs = pairs
                        .OrderBy(p => p.Centroid.Y)
                        .ThenBy(p => p.OriginalIndex)
                        .ToList();
                    break;
                case Axis.Z:
                    sortedPairs = pairs
                        .OrderBy(p => p.Centroid.Z)
                        .ThenBy(p => p.OriginalIndex)
                        .ToList();
                    break;
                default:
                    sortedPairs = pairs;
                    break;
            }

            // Extract coordinates and values from sorted pairs
            List<double> coordinates = new List<double>(sortedPairs.Count);
            List<object> sortedValues = new List<object>(sortedPairs.Count);

            foreach (var pair in sortedPairs)
            {
                coordinates.Add(GetCoordinate(pair.Centroid, axis));
                sortedValues.Add(pair.Value);
            }

            return (coordinates, sortedValues);
        }

        /// <summary>
        /// Extract coordinate value from point based on specified axis.
        /// Note: centroid is already rounded before reaching this method.
        /// </summary>
        private double GetCoordinate(Point3d point, Axis axis)
        {
            switch (axis)
            {
                case Axis.X: return point.X;
                case Axis.Y: return point.Y;
                case Axis.Z: return point.Z;
                default: return 0.0;
            }
        }

        #endregion

        #region HELPER CLASSES

        /// <summary>
        /// Enumeration for coordinate axes
        /// </summary>
        private enum Axis
        {
            X = 0,
            Y = 1,
            Z = 2
        }

        /// <summary>
        /// Helper class to pair centroid with its associated value during sorting
        /// </summary>
        private class CentroidValuePair
        {
            public Point3d Centroid { get; set; }
            public object Value { get; set; }
            public int OriginalIndex { get; set; }
        }

        #endregion
    }
}