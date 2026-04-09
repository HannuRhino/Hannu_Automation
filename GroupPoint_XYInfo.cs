using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace GroupPoint_XY
{
    public class GroupPoint_XYInfo : GH_AssemblyInfo
    {
        public override string Name => "Hannu Automation";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "Hannu Automation Grasshopper Plugin";

        public override Guid Id => new Guid("7bf2918b-b507-4fa0-b68e-e03e4dbadc3e");

        //Return a string identifying you or your company.
        public override string AuthorName => "Hannu Automation";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}