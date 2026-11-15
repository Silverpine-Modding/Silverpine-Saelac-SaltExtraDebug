using System;
using System.Linq;
using HarmonyLib;
using SaltExtraDebug;

internal static class PageLayoutChecks
{
    private static int checks;
    internal static int Run()
    {
        CheckState();
        var empty = Build(1000, 500, Array.Empty<float>());
        Check(empty.Cells.Count == 0 && empty.PageStarts.Count == 1, "Empty list remains a valid page.");
        var ordinary = Build(1300, 580, Enumerable.Repeat(280f, 51).ToArray());
        Check(ordinary.Columns == 3 && ordinary.PageStarts.Count >= 2, "Main menu uses columns and pages.");
        Check(Build(900, 600, new[] { 300f }).Columns == 2, "Two columns on medium windows.");
        Check(Build(500, 600, new[] { 300f }).Columns == 1, "Narrow window falls back to one readable column.");
        var longLabels = Build(1300, 580, new[] { 200f, 300f, 220f, 1100f, 180f, 440f });
        Check(longLabels.Cells[3].Width > longLabels.Cells[0].Width, "Long outlier spans columns.");
        Check(longLabels.Cells.Any(c => c.Height > 44), "Wrapped text grows rows.");
        var giant = Build(1300, 580, new[] { 280f, 280f, 30000f });
        Check(giant.Cells[2].Width == 1300 && giant.Cells[2].Height == 580,
            "Extreme inspection values get full-width/full-height page before auto-sizing.");
        Check(giant.PageForItem(999) == giant.PageStarts.Count - 1, "Stale index clamps to final page.");
        Check(giant.PageForItem(-1) == 0, "Negative index maps to first page.");
        Check(Build(1300, 580, Enumerable.Repeat(600f, 10).ToArray()).Columns == 2,
            "Typically long labels favor wider columns.");
        Validate(1300, 580, ordinary);
        Validate(1300, 580, longLabels);
        Validate(1300, 580, giant);
        var random = new Random(2711);
        for (int iteration = 0; iteration < 300; iteration++)
        {
            float width = random.Next(320, 1601), height = random.Next(200, 801);
            float[] widths = Enumerable.Range(0, random.Next(1, 200)).Select(_ => (float)random.Next(70, 1900)).ToArray();
            var result = Build(width, height, widths);
            Check(result.Cells.Count == widths.Length, "No lost/duplicated entries.");
            Validate(width, height, result);
        }
        CheckWiring();
        Console.WriteLine($"Pagination: {checks} geometry/state/wiring checks (text heights modeled; runtime uses TMP metrics).");
        return checks;
    }

    private static DebugMenuPageLayout Build(float width, float height, float[] widths) =>
        DebugMenuPageLayout.Build(width, height, widths, (i, w) =>
            Math.Max(44, (float)Math.Ceiling(Math.Max(1, widths[i] - 60) / Math.Max(1, w - 60)) * 30 + 16));

    private static void Validate(float width, float height, DebugMenuPageLayout layout)
    {
        Check(layout.Columns >= 1 && layout.Columns <= 3, "Supported column count.");
        for (int i = 0; i < layout.Cells.Count; i++)
        {
            var cell = layout.Cells[i];
            Check(cell.Item == i, "Row-major ordering preserved.");
            Check(cell.Width > 0 && cell.Height > 0 && cell.X >= 0 && cell.Y >= 0
                && cell.X + cell.Width <= width + .1f && cell.Y + cell.Height <= height + .1f,
                "Every cell is within its page, with no scrollbar required.");
            Check(layout.PageForItem(i) == cell.Page, "Page lookup preserves an item's location after reflow.");
            if (i > 0) Check(cell.Page >= layout.Cells[i - 1].Page, "Pages are sequential.");
            foreach (var other in layout.Cells.Take(i).Where(c => c.Page == cell.Page))
                Check(cell.X + .1f >= other.X + other.Width || other.X + .1f >= cell.X + cell.Width
                    || cell.Y + .1f >= other.Y + other.Height || other.Y + .1f >= cell.Y + cell.Height,
                    "Visible buttons never overlap.");
        }
        foreach (int first in layout.PageStarts)
            Check(layout.Cells.Count == 0 || layout.Cells[first].Y == 0, "Every page starts at the top.");
    }

    private static void CheckState()
    {
        var state = new DebugMenuPageState();
        state.Begin("main");
        Check(state.FirstItem == 0, "New menu starts at first page.");
        state.Save(24);
        state.Begin("teleport");
        Check(state.FirstItem == 0, "Teleport does not inherit main's page.");
        state.Save(40);
        state.Begin("spectate"); state.Save(16);
        state.Begin("main");
        Check(state.FirstItem == 24, "Main remembers its own first visible item.");
        state.Begin("main");
        Check(state.FirstItem == 24, "Cheat redraw/rapid close-reopen preserves position.");
        state.Begin(null); state.Save(0);
        state.Begin("main");
        Check(state.FirstItem == 24, "Unrelated menus cannot overwrite memory.");
        state.Begin("teleport");
        Check(state.FirstItem == 40, "Teleport remembers its own position.");
        state.Begin("spectate");
        Check(state.FirstItem == 16, "Spectate remembers its own position.");
        state.Begin("custom-item:quality");
        Check(state.FirstItem == 0, "Wizard screen is independent.");
        state.Save(-10);
        Check(state.FirstItem == 0, "Negative positions clamped.");
        var replacement = new DebugMenuPageState(); replacement.Begin("main");
        Check(replacement.FirstItem == 0, "Replacement UI has clean session state.");
    }

    private static void CheckWiring()
    {
        var plugin = typeof(Plugin).Assembly;
        Type pages = plugin.GetType("SaltExtraDebug.DebugMenuPages", true);
        Check(AccessTools.DeclaredMethod(pages, "OnDestroy") == null, "No OnDestroy lifecycle hook.");
        Check(AccessTools.Field(typeof(PauseMenuManager), "closeButton")?.FieldType == typeof(UnityEngine.UI.Button),
            "Native inventory close-button source resolves on this game version.");
        var closeIL = PatchProcessor.GetOriginalInstructions(AccessTools.Method(pages, "RefreshCloseButton"));
        Check(closeIL.Any(i => i.Calls(AccessTools.PropertySetter(typeof(UnityEngine.UI.Button), "onClick"))),
            "Cloned close button replaces persistent and runtime inventory handlers.");
        Check(closeIL.Any(i => i.Calls(AccessTools.Method(typeof(ButtonSoundPlayer), "Subscribe"))),
            "Native button sound is restored after clearing callbacks.");
        Check(closeIL.Any(i => i.Calls(AccessTools.PropertySetter(typeof(UnityEngine.UI.Selectable), "navigation"))),
            "Cloned button cannot retain explicit navigation targets in the inventory.");
        var draw = plugin.GetType("SaltExtraDebug.DebugMenuDrawPatch", true);
        Check(PatchProcessor.GetOriginalInstructions(AccessTools.Method(draw, "Prefix"))
            .Any(i => i.Calls(AccessTools.Method(pages, "Begin"))), "All tagged Draw calls reach pagination.");
        Check(PatchProcessor.GetOriginalInstructions(AccessTools.Method(pages, "Begin"))
            .Any(i => i.Calls(AccessTools.Method(pages, "RestoreNative"))), "Non-debug screens restore native layout.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}
