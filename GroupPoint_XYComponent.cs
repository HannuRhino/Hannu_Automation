using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace GroupPoint_XY
{
    // <summary>
    /// Lọc các điểm trong danh sách theo giá trị tọa độ (X, Y hoặc Z) nhỏ nhất hoặc lớn nhất
    /// </summary>
    public class PointMinMaxFilterByCoordinate : GH_Component
    {
        /// <summary>
        /// Constructor - Định nghĩa thông tin component
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
        /// Đăng ký các tham số đầu vào
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Input 1: Danh sách điểm
            pManager.AddPointParameter(
                "PointsList",
                "Pts",
                "List of Point",
                GH_ParamAccess.list
            );

            // Input 2: Key - Trục tọa độ
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
        /// Đăng ký các tham số đầu ra
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Output 1: Danh sách điểm có giá trị min
            pManager.AddPointParameter(
                "Min Point List",
                "MinPts",
                "List of sorted min points",
                GH_ParamAccess.list
            );

            // Output 2: Danh sách điểm có giá trị max
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
        /// Phương thức xử lý chính - được gọi mỗi khi input thay đổi
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════
            // BƯỚC 1: KHAI BÁO BIẾN
            // ═══════════════════════════════════════════════════════

            List<Point3d> points = new List<Point3d>();
            string key = string.Empty;

            // ═══════════════════════════════════════════════════════
            // BƯỚC 2: LẤY DỮ LIỆU ĐẦU VÀO
            // ═══════════════════════════════════════════════════════

            if (!DA.GetDataList(0, points)) return;
            if (!DA.GetData(1, ref key)) return;

            // ═══════════════════════════════════════════════════════
            // BƯỚC 3: XỬ LÝ LOGIC
            // ═══════════════════════════════════════════════════════

            List<Point3d> minPoints = null;
            List<Point3d> maxPoints = null;

            FilterPointsByCoordinate(points, key, out minPoints, out maxPoints);

            // ═══════════════════════════════════════════════════════
            // BƯỚC 4: XUẤT DỮ LIỆU
            // ═══════════════════════════════════════════════════════

            DA.SetDataList(0, minPoints);
            DA.SetDataList(1, maxPoints);
        }

        #endregion

        #region HELPER METHODS

        /// <summary>
        /// Lọc điểm theo tọa độ Min/Max (Thuật toán tối ưu - 2 lần duyệt)
        /// </summary>
        /// <param name="points">Danh sách điểm đầu vào</param>
        /// <param name="key">Trục tọa độ: X, Y, hoặc Z</param>
        /// <param name="minPoints">Danh sách điểm có giá trị min</param>
        /// <param name="maxPoints">Danh sách điểm có giá trị max</param>
        private void FilterPointsByCoordinate(
            List<Point3d> points,
            string key,
            out List<Point3d> minPoints,
            out List<Point3d> maxPoints)
        {
            // Khởi tạo output
            minPoints = null;
            maxPoints = null;

            // ═══════════════════════════════════════════════════════
            // VALIDATE INPUT - Kiểm tra nhanh
            // ═══════════════════════════════════════════════════════

            // Kiểm tra danh sách điểm
            if (points == null || points.Count == 0)
                return;

            // Kiểm tra key và chuẩn hóa về chữ hoa
            if (string.IsNullOrWhiteSpace(key))
                return;

            key = key.Trim().ToUpper();

            if (key != "X" && key != "Y" && key != "Z")
                return;

            // ═══════════════════════════════════════════════════════
            // DUYỆT LẦN 1: Tìm Min/Max và lưu giá trị làm tròn
            // ═══════════════════════════════════════════════════════

            double minValue = double.MaxValue;
            double maxValue = double.MinValue;
            List<double> roundedValues = new List<double>(points.Count);

            for (int i = 0; i < points.Count; i++)
            {
                Point3d point = points[i];

                // Lấy giá trị tọa độ theo key
                double value = GetCoordinateValue(point, key);

                // Làm tròn 1 số thập phân
                double roundedValue = Math.Round(value, 1);

                // Lưu giá trị đã làm tròn
                roundedValues.Add(roundedValue);

                // Cập nhật min/max
                if (roundedValue < minValue)
                    minValue = roundedValue;

                if (roundedValue > maxValue)
                    maxValue = roundedValue;
            }

            // ═══════════════════════════════════════════════════════
            // DUYỆT LẦN 2: Lọc kết quả VÀ LÀM TRÒN ĐIỂM
            // ═══════════════════════════════════════════════════════

            minPoints = new List<Point3d>();
            maxPoints = new List<Point3d>();

            for (int i = 0; i < points.Count; i++)
            {
                double roundedValue = roundedValues[i];
                Point3d point = points[i];

                // Tạo điểm mới với tọa độ đã làm tròn (1 số thập phân)
                Point3d roundedPoint = new Point3d(
                    Math.Round(point.X, 1),
                    Math.Round(point.Y, 1),
                    Math.Round(point.Z, 1)
                );

                // So sánh với minValue
                if (roundedValue == minValue)
                    minPoints.Add(roundedPoint);

                // So sánh với maxValue
                if (roundedValue == maxValue)
                    maxPoints.Add(roundedPoint);
            }
        }

        /// <summary>
        /// Lấy giá trị tọa độ theo key
        /// </summary>
        /// <param name="point">Điểm cần lấy tọa độ</param>
        /// <param name="key">Trục tọa độ (X, Y, Z)</param>
        /// <returns>Giá trị tọa độ</returns>
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