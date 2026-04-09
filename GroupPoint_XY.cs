using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace GroupPoint_XY
{
    // <summary>
    /// </summary>
    public class PointMinMaxFilterByCoordinate : GH_Component
    {
        /// <summary>
        /// </summary>
        public PointMinMaxFilterByCoordinate()
          : base(
              "GroupPoint_XY",
              "GroupPts",
              "Grouped and min or max sorted Points following Keygen",
              "Hannu Automation",
              "Points"
          )
        {
        }
        #region INPUT PARAMETERS

        /// <summary>
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter(
                "PointsList",
                "Pts",
                "List of Point",
                GH_ParamAccess.list
            );

            pManager.AddTextParameter(
                "Axis",
                "Axis",
                "",
                GH_ParamAccess.item
            );
        }

        #endregion

        #region OUTPUT PARAMETERS

        /// <summary>
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter(
                "Min Point List",
                "MinPts",
                "List of sorted min points",
                GH_ParamAccess.list
            );

            pManager.AddPointParameter(
                "Max Point List",
                "MaxPts",
                "List of sorted max points",
                GH_ParamAccess.list
            );
        }

        #endregion

        #region MAIN EXECUTION

        /// <summary>
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            List<Point3d> points = new List<Point3d>();
            string key = string.Empty;

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            if (!DA.GetDataList(0, points)) return;
            if (!DA.GetData(1, ref key)) return;

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            List<Point3d> minPoints = null;
            List<Point3d> maxPoints = null;

            FilterPointsByCoordinate(points, key, out minPoints, out maxPoints);

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            DA.SetDataList(0, minPoints);
            DA.SetDataList(1, maxPoints);
        }

        #endregion

        #region HELPER METHODS

        /// <summary>
        /// </summary>
        private void FilterPointsByCoordinate(
            List<Point3d> points,
            string key,
            out List<Point3d> minPoints,
            out List<Point3d> maxPoints)
        {
            minPoints = null;
            maxPoints = null;

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            if (points == null || points.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(key))
                return;

            key = key.Trim().ToUpper();

            if (key != "X" && key != "Y" && key != "Z")
                return;

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            double minValue = double.MaxValue;
            double maxValue = double.MinValue;
            List<double> roundedValues = new List<double>(points.Count);

            for (int i = 0; i < points.Count; i++)
            {
                Point3d point = points[i];

                double value = GetCoordinateValue(point, key);

                double roundedValue = Math.Round(value, 1);

                roundedValues.Add(roundedValue);

                if (roundedValue < minValue)
                    minValue = roundedValue;

                if (roundedValue > maxValue)
                    maxValue = roundedValue;
            }

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            minPoints = new List<Point3d>();
            maxPoints = new List<Point3d>();

            for (int i = 0; i < points.Count; i++)
            {
                double roundedValue = roundedValues[i];
                Point3d point = points[i];

                Point3d roundedPoint = new Point3d(
                    Math.Round(point.X, 1),
                    Math.Round(point.Y, 1),
                    Math.Round(point.Z, 1)
                );

                if (roundedValue == minValue)
                    minPoints.Add(roundedPoint);

                if (roundedValue == maxValue)
                    maxPoints.Add(roundedPoint);
            }
        }

        /// <summary>
        /// </summary>
        private double GetCoordinateValue(Point3d point, string key)
        {
            switch (key)
            {
                case "X":
                    return point.X;
                case "Y":
                    return point.Y;
                case "Z":
                    return point.Z;
                default:
                    return 0.0;
            }
        }

        #endregion

        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return GroupPoint_XY.Properties.Resources.GroupPOint;
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("911ac5e5-5093-4256-8a0d-1a1b032f300a");
    }
}
