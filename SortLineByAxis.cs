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
    /// 
    /// </summary>
    public class SortLine : GH_Component
    {
        #region 

        private const double TOLERANCE_SQ = 0.998001;

        private const double MIN_LENGTH_SQ = 1e-12;

        #endregion

        #region METADATA & CONSTRUCTOR

        /// <summary>
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
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
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
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter(
                "toFX",
                "X",
                "Sorted Lines by X",
                GH_ParamAccess.tree
            );

            pManager.AddLineParameter(
                "toFY",
                "Y",
                "Sorted Lines by Y",
                GH_ParamAccess.tree
            );

            pManager.AddLineParameter(
                "toFZ",
                "Z",
                "Sorted Lines by Z",
                GH_ParamAccess.tree
            );

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
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            GH_Structure<GH_Line> inputTree;
            if (!DA.GetDataTree(0, out inputTree))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Empty Lines");
                return;
            }

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            DataTree<Line> treeX = new DataTree<Line>();
            DataTree<Line> treeY = new DataTree<Line>();
            DataTree<Line> treeZ = new DataTree<Line>();
            DataTree<Line> treeDiagonal = new DataTree<Line>();

            int skippedCount = 0;

            // ═══════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════

            foreach (GH_Path path in inputTree.Paths)
            {
                List<GH_Line> ghLines = inputTree.get_Branch(path) as List<GH_Line>;

                if (ghLines == null) continue;

                // ═══════════════════════════════════════════════════════
                // ═══════════════════════════════════════════════════════

                foreach (GH_Line ghLine in ghLines)
                {
                    if (ghLine == null || ghLine.Value == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    Line line = ghLine.Value;

                    Point3d start = line.From;
                    Point3d end = line.To;

                    // ═══════════════════════════════════════════════════════
                    // ═══════════════════════════════════════════════════════

                    double dx = end.X - start.X;
                    double dy = end.Y - start.Y;
                    double dz = end.Z - start.Z;

                    double lengthSq = dx * dx + dy * dy + dz * dz;

                    if (lengthSq < MIN_LENGTH_SQ)
                    {
                        skippedCount++;
                        continue;
                    }

                    // ═══════════════════════════════════════════════════════
                    // ═══════════════════════════════════════════════════════
                    // 
                    //
                    // ═══════════════════════════════════════════════════════

                    double dotXSq = (dx * dx) / lengthSq;
                    double dotYSq = (dy * dy) / lengthSq;
                    double dotZSq = (dz * dz) / lengthSq;

                    // ═══════════════════════════════════════════════════════
                    // ═══════════════════════════════════════════════════════
                    //
                    // ═══════════════════════════════════════════════════════

                    double maxDotSq = Math.Max(dotXSq, Math.Max(dotYSq, dotZSq));

                    if (maxDotSq < TOLERANCE_SQ)
                    {
                        treeDiagonal.Add(line, path);
                    }
                    else
                    {
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
            // ═══════════════════════════════════════════════════════

            if (skippedCount > 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Skiped {skippedCount} line "
                );
            }

            // ═══════════════════════════════════════════════════════
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