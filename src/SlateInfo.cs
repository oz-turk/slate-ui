using Grasshopper.Kernel;

namespace Slate;

public class SlateInfo : GH_AssemblyInfo
{
    public override string Name        => "Slate";
    public override string Description => "Modern parametric UI panel for Grasshopper.";
    public override Guid   Id          => new Guid("C1D2E3F4-A5B6-7890-CDEF-012345678901");
    public override string AuthorName  => "Burak Öztürk";
    public override string AuthorContact => "";
}
