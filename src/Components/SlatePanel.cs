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
        pManager.AddBooleanParameter("Reset",   "R", "Full reset: clears tabs/panes/workspaces back to one blank tab and snaps the window back to its default size/position", GH_ParamAccess.item, false);
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
        bool   reset   = false;

        DA.GetData(0, ref show);
        DA.GetData(1, ref tab);
        DA.GetData(2, ref capture);
        DA.GetData(3, ref clear);
        DA.GetData(4, ref reset);

        // Drain log messages queued by SlateWindow
        var logLines = new System.Text.StringBuilder();
        while (SlateWindow.LogQueue.TryDequeue(out var line))
            logLines.AppendLine(line);
        string log = logLines.ToString();

        void UiWork()
        {
            if (reset)
            {
                SlateWindow.GetOrCreate().HardResetAll();
                log += "Reset. ";
            }
            else if (clear)
            {
                SlateWindow.GetOrCreate().ClearAll();
                log += "Cleared. ";
            }

            if (capture)
            {
                var doc = OnPingDocument();
                // Canvas order — top-to-bottom (Pivot.Y) then left-to-right (Pivot.X) —
                // rather than doc.Objects' internal (creation) order, and rather than
                // grouping by type first: mixed-type selections capture interleaved
                // exactly as they sit on the canvas.
                var selected = doc?.Objects
                    .Where(o => o.Attributes?.Selected == true)
                    .OrderBy(o => o.Attributes.Pivot.Y)
                    .ThenBy(o => o.Attributes.Pivot.X)
                    .ToList() ?? new List<IGH_DocumentObject>();

                int sliderCount = 0, toggleCount = 0, buttonCount = 0, valueListCount = 0, panelCount = 0,
                    itemPickerCount = 0, humanListCount = 0, colourPickerCount = 0, pancakeButtonCount = 0;

                if (selected.Count == 0)
                    log += "Nothing selected.";
                else
                {
                    var win = SlateWindow.GetOrCreate();
                    foreach (var o in selected)
                    {
                        if (o is GH_NumberSlider s)            { win.AddSlider(tab, s); sliderCount++; }
                        else if (o is GH_BooleanToggle t)       { win.AddToggle(tab, null, t); toggleCount++; }
                        else if (o is GH_ButtonObject b)        { win.AddButton(tab, null, b); buttonCount++; }
                        else if (o is GH_ValueList v)           { win.AddValueList(tab, null, v); valueListCount++; }
                        else if (o is GH_Panel p)               { win.AddPanel(tab, null, p); panelCount++; }
                        else if (o is GH_ItemPicker ip)         { win.AddItemPicker(tab, null, ip); itemPickerCount++; }
                        else if (SlateWindow.IsHumanValueList(o) && o is IGH_Param h)          { win.AddHumanValueList(tab, null, h); humanListCount++; }
                        else if (o is GH_ColourSwatch c)        { win.AddColourPicker(tab, null, c); colourPickerCount++; }
                        else if (SlateWindow.IsPancakeTrueOnlyButton(o) && o is IGH_Param pb)  { win.AddPancakeTrueOnlyButton(tab, null, pb); pancakeButtonCount++; }
                    }

                    if (sliderCount == 0 && toggleCount == 0 && buttonCount == 0 && valueListCount == 0 && panelCount == 0 && itemPickerCount == 0 && humanListCount == 0 && colourPickerCount == 0 && pancakeButtonCount == 0)
                        log += "Nothing selected.";
                    else
                        log += $"Captured {sliderCount} slider(s), {toggleCount} toggle(s), {buttonCount} button(s), {valueListCount} value list(s), {panelCount} panel(s), {itemPickerCount} item picker(s), {humanListCount} item selector(s), {colourPickerCount} colour picker(s), {pancakeButtonCount} true-only button(s) → \"{tab}\".";
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
