using System;
using System.Collections.Generic;
using System.Linq;

namespace SaltExtraDebug;

// Pure layout/state logic; Unity supplies actual TMP text measurements.
internal sealed class DebugMenuPageLayout
{
    internal const float Gap = 10f;
    internal sealed class Cell
    {
        internal int Item, Page;
        internal float X, Y, Width, Height;
    }

    internal int Columns;
    internal readonly List<Cell> Cells = new();
    internal readonly List<int> PageStarts = new() { 0 };

    internal static DebugMenuPageLayout Build(float width, float height,
        IReadOnlyList<float> naturalWidths, Func<int, float, float> measureHeight)
    {
        if (width <= 0 || height <= 0 || float.IsNaN(width) || float.IsNaN(height))
            throw new ArgumentOutOfRangeException(nameof(width));

        // A long outlier can span columns; it must not make every button huge.
        float[] sorted = naturalWidths.OrderBy(w => w).ToArray();
        float typical = sorted.Length == 0 ? 360f : sorted[(sorted.Length - 1) * 3 / 4];
        float desired = Math.Max(360f, Math.Min(520f, typical / 1.2f));
        int columns = Math.Max(1, Math.Min(3, (int)((width + Gap) / (desired + Gap))));
        float columnWidth = (width - Gap * (columns - 1)) / columns;
        var layout = new DebugMenuPageLayout { Columns = columns };
        int item = 0, page = 0;
        float y = 0;
        while (item < naturalWidths.Count)
        {
            var row = new List<Cell>();
            int used = 0;
            float rowHeight = 0;
            while (item < naturalWidths.Count && used < columns)
            {
                int span = naturalWidths[item] > columnWidth * 1.65f
                    ? Math.Min(columns, (int)Math.Ceiling((naturalWidths[item] + Gap) / (columnWidth + Gap))) : 1;
                float cellWidth = columnWidth * span + Gap * (span - 1);
                float measured = Math.Max(44f, measureHeight(item, cellWidth));
                // Very long inspection values get the full width before any
                // emergency auto-sizing is considered by the UI renderer.
                if (measured > height && span < columns)
                {
                    span = columns;
                    cellWidth = width;
                    measured = Math.Max(44f, measureHeight(item, cellWidth));
                }
                if (used + span > columns) break;
                row.Add(new Cell { Item = item++, X = used * (columnWidth + Gap), Width = cellWidth });
                rowHeight = Math.Max(rowHeight, Math.Min(height, measured));
                used += span;
            }
            if (y > 0 && y + rowHeight > height + 0.01f)
            {
                page++;
                layout.PageStarts.Add(row[0].Item);
                y = 0;
            }
            foreach (Cell cell in row)
            {
                cell.Page = page;
                cell.Y = y;
                cell.Height = rowHeight;
                layout.Cells.Add(cell);
            }
            y += rowHeight + Gap;
        }
        return layout;
    }

    internal int PageForItem(int item)
    {
        for (int page = PageStarts.Count - 1; page > 0; page--)
            if (PageStarts[page] <= item) return page;
        return 0;
    }
}

internal sealed class DebugMenuPageState
{
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);
    internal string Screen { get; private set; }
    internal int FirstItem => Screen != null && _positions.TryGetValue(Screen, out int item) ? item : 0;
    internal void Begin(string screen) => Screen = screen;
    internal void Save(int firstItem)
    {
        if (Screen != null) _positions[Screen] = Math.Max(0, firstItem);
    }
}
