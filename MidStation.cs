using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;
using GroupPoint_XY.Properties;

namespace YourNamespace
{
    /// <summary>
    /// Relay component - passes data without modification
    /// </summary>
    public class RelayComponent : GH_Component
    {
        public RelayComponent()
          : base(
              "Relay",
              "Relay",
              "Relay data without modification",
              "Hannu Automation",
              "Others"
          )
        {
        }

        public override Guid ComponentGuid => new Guid("A3C1354E-B952-47BF-A74E-6DF05A74FCA0");

        protected override System.Drawing.Bitmap Icon => Resources.Transformation;

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Input", "Input", "Data to relay", GH_ParamAccess.tree);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output", "Output", "Relayed data", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Structure<IGH_Goo> inputTree;

            if (!DA.GetDataTree(0, out inputTree)) return;

            DA.SetDataTree(0, inputTree);
        }
    }
}