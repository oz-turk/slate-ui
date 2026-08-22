using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Slate.Bridge;
using System.Drawing;

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

    protected override Bitmap Icon =>
        Bridge.SlateLogo.ToBitmap(24, Bridge.SlateLogo.CanvasOutline, Bridge.SlateLogo.CanvasFill);

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

        var size = SlateWindow.GetCurrentWindowSize();
        var loc  = SlateWindow.GetCurrentWindowLocation();
        if (size is System.Drawing.Size sz)
        {
            writer.SetInt32("win_w", sz.Width);
            writer.SetInt32("win_h", sz.Height);
        }
        if (loc is System.Drawing.Point pt)
        {
            writer.SetInt32("win_x", pt.X);
            writer.SetInt32("win_y", pt.Y);
        }

        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        if (reader.ItemExists("ui_state"))
        {
            SlateWindow.PendingFileState = reader.GetString("ui_state");
            SlateWindow.NeedsFileRestore = true;
        }
        if (reader.ItemExists("win_w") && reader.ItemExists("win_h"))
            SlateWindow.PendingWindowSize = new System.Drawing.Size(reader.GetInt32("win_w"), reader.GetInt32("win_h"));
        if (reader.ItemExists("win_x") && reader.ItemExists("win_y"))
            SlateWindow.PendingWindowLocation = new System.Drawing.Point(reader.GetInt32("win_x"), reader.GetInt32("win_y"));
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
                var selected = doc?.Objects
                    .Where(o => o.Attributes?.Selected == true)
                    .ToList() ?? new List<IGH_DocumentObject>();

                var sliders    = selected.OfType<GH_NumberSlider>().ToList();
                var toggles    = selected.OfType<GH_BooleanToggle>().ToList();
                var buttons    = selected.OfType<GH_ButtonObject>().ToList();
                var valueLists = selected.OfType<GH_ValueList>().ToList();
                var panels     = selected.OfType<GH_Panel>().ToList();
                var itemPickers = selected.OfType<GH_ItemPicker>().ToList();
                var humanLists  = selected.Where(SlateWindow.IsHumanValueList).OfType<IGH_Param>().ToList();
                var colourPickers = selected.OfType<GH_ColourSwatch>().ToList();
                var pancakeButtons = selected.Where(SlateWindow.IsPancakeTrueOnlyButton).OfType<IGH_Param>().ToList();

                if (sliders.Count == 0 && toggles.Count == 0 && buttons.Count == 0 && valueLists.Count == 0 && panels.Count == 0 && itemPickers.Count == 0 && humanLists.Count == 0 && colourPickers.Count == 0 && pancakeButtons.Count == 0)
                    log += "Nothing selected.";
                else
                {
                    var win = SlateWindow.GetOrCreate();
                    foreach (var s in sliders)
                        win.AddSlider(tab, s);
                    foreach (var t in toggles)
                        win.AddToggle(tab, null, t);
                    foreach (var b in buttons)
                        win.AddButton(tab, null, b);
                    foreach (var v in valueLists)
                        win.AddValueList(tab, null, v);
                    foreach (var p in panels)
                        win.AddPanel(tab, null, p);
                    foreach (var ip in itemPickers)
                        win.AddItemPicker(tab, null, ip);
                    foreach (var h in humanLists)
                        win.AddHumanValueList(tab, null, h);
                    foreach (var c in colourPickers)
                        win.AddColourPicker(tab, null, c);
                    foreach (var pb in pancakeButtons)
                        win.AddPancakeTrueOnlyButton(tab, null, pb);
                    log += $"Captured {sliders.Count} slider(s), {toggles.Count} toggle(s), {buttons.Count} button(s), {valueLists.Count} value list(s), {panels.Count} panel(s), {itemPickers.Count} item picker(s), {humanLists.Count} item selector(s), {colourPickers.Count} colour picker(s), {pancakeButtons.Count} true-only button(s) → \"{tab}\".";
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
