using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Grasshopper.GUI.Canvas;
using Rhino.Geometry;

using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.Geometry3d;
using TeklaPoint = Tekla.Structures.Geometry3d.Point;
using TeklaVector = Tekla.Structures.Geometry3d.Vector;
using RhinoLine = Rhino.Geometry.Line;
using GroupPoint_XY.Properties;

namespace TeklaGrasshopperTools
{
    #region Custom Attributes for Selection Sync

    public class TeklaDimR3ComponentAttributes : GH_ComponentAttributes
    {
        public TeklaDimR3ComponentAttributes(TeklaDimensionR3Component owner)
            : base(owner)
        {
        }

        public override bool Selected
        {
            get => base.Selected;
            set
            {
                bool previouslySelected = base.Selected;
                base.Selected = value;

                if (value != previouslySelected)
                {
                    var dimComponent = Owner as TeklaDimensionR3Component;
                    if (dimComponent != null)
                    {
                        if (value)
                            dimComponent.HighlightDimensionsInTekla();
                        else
                            dimComponent.UnhighlightDimensionsInTekla();
                    }
                }
            }
        }
    }

    #endregion

    public class TeklaDimensionR3Component : GH_Component
    {
        // Store previous state to detect changes
        private List<StraightDimensionSet> _previousDimSets = new List<StraightDimensionSet>();
        private List<double> _previousSpaceList = null;
        private double _previousViewScale = double.NaN;
        private string _previousAttributes = null;
        private Vector3d _previousVector = Vector3d.Unset;
        private List<List<Point3d>> _previousPoints = null;
        private List<Curve> _previousCurves = null;

        // DrawingHandler for selection sync
        private DrawingHandler _drawingHandler;

        public TeklaDimensionR3Component()
          : base("Tekla Straight Dimension", "TDim",
              "Creates straight dimension sets in Tekla drawings with automatic scale conversion.\n" +
              "Points are auto-sorted along vector direction.\n" +
              "Select this component on canvas to highlight its dimensions in Tekla.",
              "Hannu Automation", "Others")
        {
            try
            {
                _drawingHandler = new DrawingHandler();
            }
            catch
            {
                _drawingHandler = null;
            }
        }

        public override Guid ComponentGuid => new Guid("D4E5F6A7-B8C9-4D2E-A1F3-072B6C8E3D5A");
        protected override System.Drawing.Bitmap Icon => Resources.Dimension_1_;
        public override GH_Exposure Exposure => GH_Exposure.primary;

        public override void CreateAttributes()
        {
            m_attributes = new TeklaDimR3ComponentAttributes(this);
        }

        public IReadOnlyList<StraightDimensionSet> CreatedDimensionSets => _previousDimSets.AsReadOnly();

        #region Tekla Selection Sync

        public void HighlightDimensionsInTekla()
        {
            if (_drawingHandler == null || _previousDimSets == null || _previousDimSets.Count == 0)
                return;

            try
            {
                var selector = _drawingHandler.GetDrawingObjectSelector();
                if (selector == null) return;

                var objectsToSelect = new ArrayList();
                foreach (var dimSet in _previousDimSets)
                {
                    if (dimSet != null)
                    {
                        try
                        {
                            if (dimSet.Select())
                                objectsToSelect.Add(dimSet);
                        }
                        catch { }
                    }
                }

                if (objectsToSelect.Count > 0)
                    selector.SelectObjects(objectsToSelect, false);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Could not highlight in Tekla: {ex.Message}");
            }
        }

        public void UnhighlightDimensionsInTekla()
        {
            if (_drawingHandler == null || _previousDimSets == null || _previousDimSets.Count == 0)
                return;

            try
            {
                var selector = _drawingHandler.GetDrawingObjectSelector();
                if (selector == null) return;

                var objectsToUnselect = new ArrayList();
                foreach (var dimSet in _previousDimSets)
                {
                    if (dimSet != null)
                    {
                        try
                        {
                            if (dimSet.Select())
                                objectsToUnselect.Add(dimSet);
                        }
                        catch { }
                    }
                }

                if (objectsToUnselect.Count > 0)
                    selector.UnselectObjects(objectsToUnselect);
            }
            catch { }
        }

        #endregion

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("View", "V", "Tekla View object", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "P", "Points to dimension (tree). Auto-sorted along vector direction.", GH_ParamAccess.tree);
            pManager.AddVectorParameter("Vector", "Vec", "Direction vector (perpendicular to reference curve)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Space", "S",
                "Distance from points to dimension line (model mm).\n" +
                "Single value = same for all branches.\n" +
                "List = per-branch spacing (last value repeats for extra branches).",
                GH_ParamAccess.list, new List<double> { 1.0 });
            pManager.AddTextParameter("Attributes", "Attr", "Dimension attributes name", GH_ParamAccess.item, "standard");
            pManager.AddCurveParameter("Std.Curves", "RC", "Reference curves (tree or single)", GH_ParamAccess.tree);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Dimension Lines", "Dims", "Created dimension sets", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get inputs
            object viewObject = null;
            GH_Structure<GH_Point> pointTree = new GH_Structure<GH_Point>();
            Vector3d vector = Vector3d.Unset;
            List<double> spaceList = new List<double>();
            string attributes = "standard";
            GH_Structure<GH_Curve> curveTree = new GH_Structure<GH_Curve>();

            if (!DA.GetData(0, ref viewObject)) return;
            if (!DA.GetDataTree(1, out pointTree)) return;
            if (!DA.GetData(2, ref vector)) return;
            if (!DA.GetDataList(3, spaceList)) return;
            DA.GetData(4, ref attributes);
            if (!DA.GetDataTree(5, out curveTree)) return;

            // Validate inputs
            if (!ValidateInputs(viewObject, pointTree, vector, spaceList, curveTree))
                return;

            // Unwrap Tekla View
            Tekla.Structures.Drawing.View teklaView = UnwrapView(viewObject);
            if (teklaView == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid View object");
                return;
            }

            // Ensure DrawingHandler is initialized
            if (_drawingHandler == null)
            {
                try { _drawingHandler = new DrawingHandler(); }
                catch { }
            }

            // Get view scale
            double viewScale = GetViewScale(teklaView);

            // Normalize vector
            Vector3d normalizedVector = vector;
            normalizedVector.Unitize();

            // Extract current input data
            List<List<Point3d>> rawPoints = ExtractPoints(pointTree);
            List<Curve> rawCurves = ExtractCurves(curveTree);

            // Match curves to point branches
            List<Curve> matchedCurves = MatchCurvesToBranches(rawCurves, rawPoints.Count);

            // Sort points along VECTOR direction for each branch
            List<List<Point3d>> sortedPoints = SortAllPointsAlongVector(rawPoints, normalizedVector);

            // Match space list to branches
            List<double> matchedSpaces = MatchSpaceToBranches(spaceList, sortedPoints.Count);

            // Detect what changed
            ChangeType changeType = DetectChanges(sortedPoints, rawCurves, vector, spaceList, viewScale, attributes);

            if (!AreDimensionsValid())
            {
                changeType = ChangeType.Structure;
                _previousDimSets.Clear();
            }

            List<StraightDimensionSet> resultDimSets = new List<StraightDimensionSet>();

            // Handle changes based on type
            if (changeType == ChangeType.None)
            {
                resultDimSets = new List<StraightDimensionSet>(_previousDimSets);
            }
            else if (changeType == ChangeType.SpaceOnly)
            {
                try
                {
                    List<double> newMatchedSpaces = MatchSpaceToBranches(spaceList, _previousDimSets.Count);
                    ModifyExisting(newMatchedSpaces, sortedPoints, matchedCurves, viewScale);
                    resultDimSets = new List<StraightDimensionSet>(_previousDimSets);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Modify failed, recreating: {ex.Message}");
                    DeletePrevious();
                    CreateDimensions(teklaView, sortedPoints, matchedCurves, normalizedVector,
                        matchedSpaces, viewScale, attributes, ref resultDimSets);
                }
            }
            else
            {
                DeletePrevious();
                try
                {
                    CreateDimensions(teklaView, sortedPoints, matchedCurves, normalizedVector,
                        matchedSpaces, viewScale, attributes, ref resultDimSets);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
                    return;
                }
            }

            // Commit changes to Tekla
            if (resultDimSets.Count > 0 && changeType != ChangeType.None)
            {
                try
                {
                    var drawing = teklaView.GetDrawing();
                    drawing?.CommitChanges();
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Commit failed: {ex.Message}");
                }
            }

            // Store state for next run
            _previousSpaceList = new List<double>(spaceList);
            _previousViewScale = viewScale;
            _previousAttributes = attributes;
            _previousVector = vector;
            _previousPoints = sortedPoints;
            _previousCurves = rawCurves;
            _previousDimSets = new List<StraightDimensionSet>(resultDimSets);

            Message = $"{resultDimSets.Count} dim(s)";

            DA.SetDataList(0, resultDimSets);
        }

        #region Enums

        private enum ChangeType
        {
            None,
            SpaceOnly,
            Structure
        }

        #endregion

        #region Point Sorting (along VECTOR direction)

        /// <summary>
        /// Sort points based on input vector:
        /// Vector X (dominant) → sort by Y descending (top to bottom)
        /// Vector Y (dominant) → sort by X ascending  (left to right)
        /// Vector Z (dominant) → sort by Z ascending   (low to high)
        /// Dominant axis = largest absolute component of vector.
        /// </summary>
        private List<Point3d> SortPointsAlongVector(List<Point3d> points, Vector3d normalizedVector)
        {
            if (points == null || points.Count <= 1)
                return points;

            double absX = Math.Abs(normalizedVector.X);
            double absY = Math.Abs(normalizedVector.Y);
            double absZ = Math.Abs(normalizedVector.Z);

            if (absX >= absY && absX >= absZ)
            {
                // Vector X dominant → sort by Y descending (top to bottom)
                return points.OrderByDescending(pt => pt.Y).ToList();
            }
            else if (absY >= absX && absY >= absZ)
            {
                // Vector Y dominant → sort by X ascending (left to right)
                return points.OrderBy(pt => pt.X).ToList();
            }
            else
            {
                // Vector Z dominant → sort by Z ascending (low to high)
                return points.OrderBy(pt => pt.Z).ToList();
            }
        }

        private List<List<Point3d>> SortAllPointsAlongVector(
            List<List<Point3d>> allPoints,
            Vector3d normalizedVector)
        {
            var result = new List<List<Point3d>>();
            foreach (var branch in allPoints)
            {
                result.Add(SortPointsAlongVector(branch, normalizedVector));
            }
            return result;
        }

        #endregion

        #region Input Extraction & Matching

        private List<List<Point3d>> ExtractPoints(GH_Structure<GH_Point> pointTree)
        {
            var result = new List<List<Point3d>>();
            foreach (var branch in pointTree.Branches)
            {
                result.Add(branch.Select(pt => pt.Value).ToList());
            }
            return result;
        }

        private List<Curve> ExtractCurves(GH_Structure<GH_Curve> curveTree)
        {
            var result = new List<Curve>();
            foreach (var branch in curveTree.Branches)
            {
                if (branch.Count > 0)
                    result.Add(branch[0].Value);
            }
            return result;
        }

        /// <summary>
        /// Match curves to point branches:
        /// - 1 curve + N branches → reuse for all
        /// - Counts match → pair 1:1
        /// - Mismatch → Min and warn
        /// </summary>
        private List<Curve> MatchCurvesToBranches(List<Curve> curves, int pointBranchCount)
        {
            var matched = new List<Curve>();

            if (curves.Count == 1 && pointBranchCount > 1)
            {
                for (int i = 0; i < pointBranchCount; i++)
                    matched.Add(curves[0]);
            }
            else if (curves.Count == pointBranchCount)
            {
                matched.AddRange(curves);
            }
            else
            {
                int count = Math.Min(curves.Count, pointBranchCount);
                for (int i = 0; i < count; i++)
                    matched.Add(curves[i]);

                if (curves.Count != pointBranchCount)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Branch count mismatch: {pointBranchCount} point branches vs {curves.Count} curves. " +
                        $"Only {count} dimensions created.");
                }
            }

            return matched;
        }

        /// <summary>
        /// Match space list to branches (last-item-repeats):
        /// [1]       → all branches get 1
        /// [1, 2]    → branch0=1, branch1+=2
        /// [1, 3, 5] → branch0=1, branch1=3, branch2+=5
        /// </summary>
        private List<double> MatchSpaceToBranches(List<double> spaceList, int branchCount)
        {
            var matched = new List<double>();
            for (int i = 0; i < branchCount; i++)
            {
                if (i < spaceList.Count)
                    matched.Add(spaceList[i]);
                else
                    matched.Add(spaceList[spaceList.Count - 1]);
            }
            return matched;
        }

        #endregion

        #region Change Detection

        private ChangeType DetectChanges(
            List<List<Point3d>> currentPoints,
            List<Curve> currentCurves,
            Vector3d currentVector,
            List<double> currentSpaceList,
            double currentViewScale,
            string currentAttributes)
        {
            if (_previousDimSets == null || _previousDimSets.Count == 0)
                return ChangeType.Structure;

            if (_previousAttributes != currentAttributes)
                return ChangeType.Structure;

            if (!VectorsEqual(_previousVector, currentVector))
                return ChangeType.Structure;

            if (!PointsEqual(_previousPoints, currentPoints))
                return ChangeType.Structure;

            if (!CurvesEqual(_previousCurves, currentCurves))
                return ChangeType.Structure;

            if (!SpaceListEqual(_previousSpaceList, currentSpaceList) ||
                Math.Abs(_previousViewScale - currentViewScale) > 0.001)
                return ChangeType.SpaceOnly;

            return ChangeType.None;
        }

        private bool VectorsEqual(Vector3d v1, Vector3d v2)
        {
            const double tolerance = 0.001;
            return Math.Abs(v1.X - v2.X) < tolerance &&
                   Math.Abs(v1.Y - v2.Y) < tolerance &&
                   Math.Abs(v1.Z - v2.Z) < tolerance;
        }

        private bool PointsEqual(List<List<Point3d>> pts1, List<List<Point3d>> pts2)
        {
            if (pts1 == null || pts2 == null) return false;
            if (pts1.Count != pts2.Count) return false;

            const double tolerance = 0.001;
            for (int i = 0; i < pts1.Count; i++)
            {
                if (pts1[i].Count != pts2[i].Count) return false;
                for (int j = 0; j < pts1[i].Count; j++)
                {
                    if (pts1[i][j].DistanceTo(pts2[i][j]) > tolerance)
                        return false;
                }
            }
            return true;
        }

        private bool CurvesEqual(List<Curve> curves1, List<Curve> curves2)
        {
            if (curves1 == null || curves2 == null) return false;
            if (curves1.Count != curves2.Count) return false;

            const double tolerance = 0.001;
            for (int i = 0; i < curves1.Count; i++)
            {
                if (curves1[i].PointAtStart.DistanceTo(curves2[i].PointAtStart) > tolerance ||
                    curves1[i].PointAtEnd.DistanceTo(curves2[i].PointAtEnd) > tolerance)
                    return false;
            }
            return true;
        }

        private bool SpaceListEqual(List<double> list1, List<double> list2)
        {
            if (list1 == null || list2 == null) return false;
            if (list1.Count != list2.Count) return false;

            const double tolerance = 0.001;
            for (int i = 0; i < list1.Count; i++)
            {
                if (Math.Abs(list1[i] - list2[i]) > tolerance)
                    return false;
            }
            return true;
        }

        #endregion

        #region Distance Calculation

        /// <summary>
        /// Projects sorted points onto the reference curve.
        /// Takes projected point [0], measures perpendicular distance from sortedPoints[0] to it,
        /// then adds the user-defined space offset.
        /// distance = perpendicular distance from point[0] to curve + space
        /// </summary>
        private double CalcDistanceFromProjection(List<Point3d> sortedPoints, Curve referenceCurve, double space, double viewScale)
        {
            double t;
            referenceCurve.ClosestPoint(sortedPoints[0], out t);
            Point3d projectedPoint = referenceCurve.PointAt(t);

            double perpDist = sortedPoints[0].DistanceTo(projectedPoint);
            double spaceNew = 1000.0 * (space / viewScale);
            return perpDist + spaceNew;
        }

        #endregion

        #region Modify Existing

        private void ModifyExisting(List<double> matchedSpaces, List<List<Point3d>> sortedPoints, List<Curve> matchedCurves, double viewScale)
        {
            int count = Math.Min(_previousDimSets.Count, matchedSpaces.Count);

            for (int i = 0; i < count; i++)
            {
                var dimSet = _previousDimSets[i];
                if (dimSet != null)
                {
                    try
                    {
                        double newDistance = CalcDistanceFromProjection(sortedPoints[i], matchedCurves[i], matchedSpaces[i], viewScale);
                        if (dimSet.Select())
                        {
                            dimSet.Distance = newDistance;
                            dimSet.Modify();
                        }
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            $"Modify branch {i} failed: {ex.Message}");
                    }
                }
            }
        }

        #endregion

        #region Validation

        private bool ValidateInputs(
            object viewObject,
            GH_Structure<GH_Point> pointTree,
            Vector3d vector,
            List<double> spaceList,
            GH_Structure<GH_Curve> curveTree)
        {
            if (viewObject == null || !IsValidView(viewObject))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid View");
                return false;
            }

            if (pointTree == null || pointTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Points tree is empty");
                return false;
            }

            if (!vector.IsValid || vector.Length < 0.001)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid vector");
                return false;
            }

            if (spaceList == null || spaceList.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Space list is empty");
                return false;
            }

            if (spaceList.Any(s => s <= 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Space values should be positive. Negative/zero values may cause unexpected results.");
            }

            if (curveTree == null || curveTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curves tree is empty");
                return false;
            }

            return true;
        }

        private bool IsValidView(object obj)
        {
            if (obj == null) return false;

            object unwrapped = obj;
            if (obj is GH_ObjectWrapper wrapper)
                unwrapped = wrapper.Value;

            if (unwrapped == null) return false;

            var valueProperty = unwrapped.GetType().GetProperty("Value");
            if (valueProperty != null)
            {
                var innerValue = valueProperty.GetValue(unwrapped);
                if (innerValue != null)
                    unwrapped = innerValue;
            }

            return unwrapped is Tekla.Structures.Drawing.View || unwrapped is ViewBase;
        }

        private Tekla.Structures.Drawing.View UnwrapView(object viewObject)
        {
            if (viewObject == null) return null;

            object unwrapped = viewObject;
            if (viewObject is GH_ObjectWrapper wrapper)
                unwrapped = wrapper.Value;

            if (unwrapped == null) return null;

            var valueProperty = unwrapped.GetType().GetProperty("Value");
            if (valueProperty != null)
            {
                var innerValue = valueProperty.GetValue(unwrapped);
                if (innerValue != null)
                    unwrapped = innerValue;
            }

            if (unwrapped is Tekla.Structures.Drawing.View teklaView)
                return teklaView;

            if (unwrapped is ViewBase viewBase)
                return viewBase as Tekla.Structures.Drawing.View;

            return null;
        }

        private double GetViewScale(Tekla.Structures.Drawing.View teklaView)
        {
            try
            {
                if (!teklaView.Select())
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to select view, using 1:1");
                    return 1.0;
                }

                double scale = teklaView.Attributes.Scale;
                if (scale <= 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid scale, using 1:1");
                    return 1.0;
                }

                return scale;
            }
            catch
            {
                return 1.0;
            }
        }

        private bool IsVectorPerpendicular(Vector3d inputVector, Curve curve, int branchIndex)
        {
            Vector3d curveDirection = curve.PointAtEnd - curve.PointAtStart;

            if (curveDirection.Length < 0.001)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Branch {branchIndex}: Curve too short");
                return false;
            }

            curveDirection.Unitize();
            double dotProduct = Math.Abs(inputVector * curveDirection);

            if (dotProduct > 0.99)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Branch {branchIndex}: Vector parallel to curve");
                return false;
            }

            if (dotProduct > 0.01)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Branch {branchIndex}: Vector not perfectly perpendicular (dot={dotProduct:F3})");
            }

            return true;
        }

        #endregion

        #region Delete Previous

        private void DeletePrevious()
        {
            if (_previousDimSets == null || _previousDimSets.Count == 0)
                return;

            foreach (var dimSet in _previousDimSets)
            {
                try
                {
                    dimSet?.Delete();
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Delete failed: {ex.Message}");
                }
            }

            _previousDimSets.Clear();
        }

        #endregion

        #region Create Dimensions

        private void CreateDimensions(
            Tekla.Structures.Drawing.View teklaView,
            List<List<Point3d>> sortedPoints,
            List<Curve> matchedCurves,
            Vector3d normalizedVector,
            List<double> matchedSpaces,
            double viewScale,
            string attributesName,
            ref List<StraightDimensionSet> resultDimSets)
        {
            var handler = new StraightDimensionSetHandler();

            TeklaVector teklaVector = new TeklaVector(normalizedVector.X, normalizedVector.Y, normalizedVector.Z);

            int branchCount = Math.Min(sortedPoints.Count, matchedCurves.Count);

            for (int i = 0; i < branchCount; i++)
            {
                try
                {
                    List<Point3d> branchPoints = sortedPoints[i];
                    Curve branchCurve = matchedCurves[i];
                    double branchSpace = matchedSpaces[i];

                    if (branchPoints.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Branch {i}: No points");
                        continue;
                    }

                    if (!IsVectorPerpendicular(normalizedVector, branchCurve, i))
                        continue;

                    double paperSpace = CalcDistanceFromProjection(branchPoints, branchCurve, branchSpace, viewScale);
                    Vector3d offsetVector = normalizedVector * paperSpace;

                    CreateSingleDimension(
                        teklaView,
                        branchPoints,
                        branchCurve,
                        offsetVector,
                        teklaVector,
                        paperSpace,
                        attributesName,
                        handler,
                        ref resultDimSets
                    );
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Branch {i} failed: {ex.Message}");
                }
            }

            _previousDimSets = new List<StraightDimensionSet>(resultDimSets);
        }

        private bool CreateSingleDimension(
            Tekla.Structures.Drawing.View teklaView,
            List<Point3d> points,
            Curve referenceCurve,
            Vector3d offsetVector,
            TeklaVector teklaVector,
            double distance,
            string attributesName,
            StraightDimensionSetHandler handler,
            ref List<StraightDimensionSet> resultList)
        {
            RhinoLine refLine = new RhinoLine(referenceCurve.PointAtStart, referenceCurve.PointAtEnd);
            Vector3d direction = refLine.Direction;
            direction.Unitize();

            RhinoLine extendedLine = new RhinoLine(
                refLine.From - direction * 2000,
                refLine.To + direction * 2000
            );

            Point3d referencePoint = FindReferencePoint(points, extendedLine, offsetVector);
            if (!referencePoint.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Could not find valid reference point");
                return false;
            }

            var teklaPoints = new Tekla.Structures.Drawing.PointList();
            foreach (var point in points)
                teklaPoints.Add(new TeklaPoint(point.X, point.Y, point.Z));

            var dimAttributes = new StraightDimensionSet.StraightDimensionSetAttributes(null, attributesName);

            var dimSet = handler.CreateDimensionSet(teklaView, teklaPoints, teklaVector, distance, dimAttributes);

            if (dimSet != null)
            {
                try
                {
                    var drawing = teklaView.GetDrawing();
                    drawing?.CommitChanges();

                    if (dimSet.Select())
                    {
                        dimSet.Distance = distance;
                        dimSet.Modify();
                        drawing?.CommitChanges();
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Set distance failed: {ex.Message}");
                }

                resultList.Add(dimSet);
                return true;
            }

            return false;
        }

        private Point3d FindReferencePoint(List<Point3d> points, RhinoLine extendedLine, Vector3d offsetVector)
        {
            Point3d closestPoint = Point3d.Unset;
            double minDistance = double.MaxValue;

            Vector3d rayDirection = offsetVector;
            rayDirection.Unitize();

            foreach (var point in points)
            {
                RhinoLine ray = new RhinoLine(point, point + rayDirection * 10000);

                double lineParam, rayParam;
                if (Rhino.Geometry.Intersect.Intersection.LineLine(
                    extendedLine, ray, out lineParam, out rayParam, 0.01, false))
                {
                    Point3d intersectionPoint = extendedLine.PointAt(lineParam);
                    double dist = point.DistanceTo(intersectionPoint);

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestPoint = point;
                    }
                }
            }

            return closestPoint;
        }

        #endregion

        #region Dimension Validity Check

        private bool AreDimensionsValid()
        {
            if (_previousDimSets == null || _previousDimSets.Count == 0)
                return false;

            foreach (var dimSet in _previousDimSets)
            {
                if (dimSet == null)
                    return false;

                try
                {
                    if (!dimSet.Select())
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
