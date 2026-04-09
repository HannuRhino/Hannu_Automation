using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GroupPoint_XY.Properties;
using Rhino.Geometry;

namespace SortedLineByAxis
{
    /// <summary>
    /// LINE AXIS CLASSIFIER COMPONENT
    /// 
    /// Phân loại các đường thẳng (Line) theo hướng song song với trục X, Y, Z hoặc Diagonal.
    /// Sử dụng thuật toán tối ưu không cần căn bậc hai để đạt hiệu năng cao.
    /// 
    /// THUẬT TOÁN:
    /// - Tính dot product bình phương (squared) để tránh sqrt
    /// - Chọn trục có dot product cao nhất
    /// - Skip line null hoặc line quá ngắn (tránh division by zero)
    /// - Giữ nguyên cấu trúc path từ input sang output
    /// </summary>
    public class SortLine : GH_Component
    {
        #region 

        // Tolerance bình phương (0.999² = 0.998001)
        // Sử dụng bình phương để tránh phép tính sqrt tốn kém
        private const double TOLERANCE_SQ = 0.998001;

        // Ngưỡng độ dài bình phương tối thiểu (1e-12)
        // Line có lengthSq < MIN_LENGTH_SQ sẽ bị bỏ qua để tránh chia cho 0
        private const double MIN_LENGTH_SQ = 1e-12;

        #endregion

        #region METADATA & CONSTRUCTOR

        /// <summary>
        /// Constructor - Định nghĩa thông tin component hiển thị trong Grasshopper UI
        /// </summary>
        public SortLine()
          : base(
              "SortLineByAxis",       
              "SortLineByAxis",                         
              "Sort Line by Axis",
              "Hannu Automation",                        
              "Curves"                      
          )
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        #endregion

        #region INPUT PARAMETERS

        /// <summary>
        /// Đăng ký các tham số INPUT
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // INPUT: DataTree chứa các Line cần phân loại
            pManager.AddLineParameter(
                "Lines",                        
                "Ln",                          
                "List of Lines",
                GH_ParamAccess.tree            
            );
        }

        #endregion

        #region OUTPUT PARAMETERS

        /// <summary>
        /// Đăng ký các tham số OUTPUT
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // OUTPUT 1: Lines song song với trục X
            pManager.AddLineParameter(
                "toFX",
                "X",
                "Sorted Lines by X",
                GH_ParamAccess.tree
            );

            // OUTPUT 2: Lines song song với trục Y
            pManager.AddLineParameter(
                "toFY",
                "Y",
                "Sorted Lines by Y",
                GH_ParamAccess.tree
            );

            // OUTPUT 3: Lines song song với trục Z
            pManager.AddLineParameter(
                "toFZ",
                "Z",
                "Sorted Lines by Z",
                GH_ParamAccess.tree
            );

            // OUTPUT 4: Lines không song song với trục nào (Diagonal)
            pManager.AddLineParameter(
                "Diagonal",
                "Dg",
                "Diagonal Lines",
                GH_ParamAccess.tree
            );
        }

        #endregion

        #region MAIN EXECUTION

        /// <summary>
        /// PHƯƠNG THỨC THỰC THI CHÍNH - Được gọi mỗi khi input thay đổi
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════
            // BƯỚC 1: KHAI BÁO BIẾN VÀ LẤY INPUT
            // ═══════════════════════════════════════════════════════

            // Lấy DataTree input từ parameter Lines
            GH_Structure<GH_Line> inputTree;
            if (!DA.GetDataTree(0, out inputTree))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Empty Lines");
                return;
            }

            // ═══════════════════════════════════════════════════════
            // BƯỚC 2: KHỞI TẠO CÁC OUTPUT TREE RỖNG
            // ═══════════════════════════════════════════════════════

            // Khởi tạo 4 DataTree để chứa kết quả phân loại
            DataTree<Line> treeX = new DataTree<Line>();
            DataTree<Line> treeY = new DataTree<Line>();
            DataTree<Line> treeZ = new DataTree<Line>();
            DataTree<Line> treeDiagonal = new DataTree<Line>();

            // Biến đếm số line bị skip (để hiển thị warning)
            int skippedCount = 0;

            // ═══════════════════════════════════════════════════════
            // BƯỚC 3: DUYỆT QUA TỪNG BRANCH TRONG INPUT TREE
            // ═══════════════════════════════════════════════════════

            foreach (GH_Path path in inputTree.Paths)
            {
                // Lấy danh sách GH_Line từ branch hiện tại
                List<GH_Line> ghLines = inputTree.get_Branch(path) as List<GH_Line>;

                if (ghLines == null) continue;

                // ═══════════════════════════════════════════════════════
                // BƯỚC 4: XỬ LÝ TỪNG LINE TRONG BRANCH
                // ═══════════════════════════════════════════════════════

                foreach (GH_Line ghLine in ghLines)
                {
                    // Kiểm tra null
                    if (ghLine == null || ghLine.Value == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    Line line = ghLine.Value;

                    // Lấy điểm đầu và điểm cuối
                    Point3d start = line.From;
                    Point3d end = line.To;

                    // ═══════════════════════════════════════════════════════
                    // BƯỚC 5: TÍNH VECTOR DIRECTION (KHÔNG CẦN UNITIZE)
                    // ═══════════════════════════════════════════════════════

                    double dx = end.X - start.X;
                    double dy = end.Y - start.Y;
                    double dz = end.Z - start.Z;

                    // Tính độ dài bình phương (tránh sqrt)
                    double lengthSq = dx * dx + dy * dy + dz * dz;

                    // Kiểm tra line có quá ngắn không (tránh chia cho 0)
                    if (lengthSq < MIN_LENGTH_SQ)
                    {
                        skippedCount++;
                        continue;
                    }

                    // ═══════════════════════════════════════════════════════
                    // BƯỚC 6: TÍNH DOT PRODUCT BÌNH PHƯƠNG CHO MỖI TRỤC
                    // ═══════════════════════════════════════════════════════
                    // 
                    // Thay vì: dot = |direction.X| / length (cần sqrt)
                    // Ta dùng: dotSq = direction.X² / lengthSq (không cần sqrt)
                    //
                    // Lý do: dot² = (direction.X / length)² = direction.X² / length²
                    // ═══════════════════════════════════════════════════════

                    double dotXSq = (dx * dx) / lengthSq;  // Dot product bình phương với trục X
                    double dotYSq = (dy * dy) / lengthSq;  // Dot product bình phương với trục Y
                    double dotZSq = (dz * dz) / lengthSq;  // Dot product bình phương với trục Z

                    // ═══════════════════════════════════════════════════════
                    // BƯỚC 7: PHÂN LOẠI THÔNG MINH - CHỌN TRỤC GẦN NHẤT
                    // ═══════════════════════════════════════════════════════
                    //
                    // Tìm trục có dot product cao nhất (gần song song nhất)
                    // Nếu dot product < TOLERANCE_SQ → line là diagonal
                    // Ngược lại → gán vào trục có dot product cao nhất
                    // ═══════════════════════════════════════════════════════

                    double maxDotSq = Math.Max(dotXSq, Math.Max(dotYSq, dotZSq));

                    // Nếu dot product cao nhất vẫn nhỏ hơn tolerance → Diagonal
                    if (maxDotSq < TOLERANCE_SQ)
                    {
                        treeDiagonal.Add(line, path);
                    }
                    else
                    {
                        // Gán vào trục có dot product cao nhất
                        if (maxDotSq == dotXSq)
                        {
                            treeX.Add(line, path);
                        }
                        else if (maxDotSq == dotYSq)
                        {
                            treeY.Add(line, path);
                        }
                        else // maxDotSq == dotZSq
                        {
                            treeZ.Add(line, path);
                        }
                    }
                }
            }

            // ═══════════════════════════════════════════════════════
            // BƯỚC 8: HIỂN THỊ WARNING NẾU CÓ LINE BỊ SKIP
            // ═══════════════════════════════════════════════════════

            if (skippedCount > 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Skiped {skippedCount} line "
                );
            }

            // ═══════════════════════════════════════════════════════
            // BƯỚC 9: GÁN KẾT QUẢ CHO CÁC OUTPUT
            // ═══════════════════════════════════════════════════════

            DA.SetDataTree(0, treeX);         // Output: ToX
            DA.SetDataTree(1, treeY);         // Output: ToY
            DA.SetDataTree(2, treeZ);         // Output: ToZ
            DA.SetDataTree(3, treeDiagonal);  // Output: Diagonal
        }

        #endregion


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Resources.sortedline;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("6CB18079-334C-4532-882E-91BFE5A903FE"); }
        }
    }
}