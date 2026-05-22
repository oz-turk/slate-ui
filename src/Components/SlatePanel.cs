using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Slate.Bridge;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Slate.Components;

public class SlatePanel : GH_Component
{
    public SlatePanel()
        : base("Slate", "Slate",
               "Modern parametric UI panel. Select sliders on canvas then Capture.",
               "Slate", "UI")
    { }

    public override Guid ComponentGuid => new Guid("F1E2D3C4-B5A6-7890-FEDC-BA9876543210");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon
    {
        get
        {
            var bmp = new Bitmap(24, 24);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var pen   = new Pen(Color.FromArgb(100, 160, 255), 1.5f);
            using var brush = new SolidBrush(Color.FromArgb(100, 160, 255));

            // "S" lettermark, minimal
            g.DrawLine(pen, 6, 7, 15, 7);
            g.DrawLine(pen, 6, 7, 6, 12);
            g.DrawLine(pen, 6, 12, 15, 12);
            g.DrawLine(pen, 15, 12, 15, 17);
            g.DrawLine(pen, 6, 17, 15, 17);

            return bmp;
        }
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter("Show",    "S", "True = show window, False = hide",           GH_ParamAccess.item, true);
        pManager.AddTextParameter   ("Tab",     "T", "Tab path (use / for nesting)",               GH_ParamAccess.item, "Main");
        pManager.AddBooleanParameter("Capture", "C", "Capture selected sliders into the tab",      GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Clear",   "X", "Clear all sliders from the panel",           GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Log", "L", "Status", GH_ParamAccess.item);
    }

    public override bool Write(GH_IWriter writer)
    {
        var state = SlateWindow.GetSerializedState();
        if (state != null) writer.SetString("ui_state", state);
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        if (reader.ItemExists("ui_state"))
        {
            SlateWindow.PendingFileState = reader.GetString("ui_state");
            SlateWindow.NeedsFileRestore = true;
        }
        return base.Read(reader);
    }

    public override void AddedToDocument(GH_Document document)
    {
        SlateWindow.HostComponent = this;
        SlateWindow.HostDocument  = document;
        base.AddedToDocument(document);
    }

    public override void RemovedFromDocument(GH_Document document)
    {
        SlateWindow.HideIfOpen();
        SlateWindow.HostDocument  = null;
        SlateWindow.HostComponent = null;
        base.RemovedFromDocument(document);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        bool   show    = true;
        string tab     = "Main";
        bool   capture = false;
        bool   clear   = false;

        DA.GetData(0, ref show);
        DA.GetData(1, ref tab);
        DA.GetData(2, ref capture);
        DA.GetData(3, ref clear);

        // Drain log messages queued by SlateWindow
        var logLines = new System.Text.StringBuilder();
        while (SlateWindow.LogQueue.TryDequeue(out var line))
            logLines.AppendLine(line);
        string log = logLines.ToString();

        void UiWork()
        {
            if (clear)
            {
                SlateWindow.GetOrCreate().ClearAll();
                log += "Cleared. ";
            }

            if (capture)
            {
                var doc = OnPingDocument();
                var sliders = doc?.Objects
                    .Where(o => o.Attributes?.Selected == true)
                    .OfType<GH_NumberSlider>()
                    .ToList() ?? new List<GH_NumberSlider>();

                if (sliders.Count == 0)
                    log += "No sliders selected.";
                else
                {
                    var win = SlateWindow.GetOrCreate();
                    foreach (var s in sliders)
                        win.AddSlider(tab, s);
                    log += $"Captured {sliders.Count} slider(s) → \"{tab}\".";
                }
            }

            if (show) SlateWindow.EnsureVisible();
            else      SlateWindow.HideIfOpen();
        }

        var canvas = Grasshopper.Instances.ActiveCanvas;
        if (canvas != null && canvas.InvokeRequired)
            canvas.Invoke((Action)UiWork);
        else
            UiWork();

        DA.SetData(0, log.TrimEnd());
    }
}
