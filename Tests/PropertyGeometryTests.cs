using System;
using System.Collections.Generic;
using System.Linq;
using SaltExtraDebug;
using Point = SaltExtraDebug.NpcPropertyGeometry.Point;
using Rectangle = SaltExtraDebug.NpcPropertyGeometry.Rectangle;

internal static class PropertyGeometryTests
{
    private static int assertions;
    private static int scenarios;

    internal static void Run()
    {
        var strips = Enumerable.Range(0, 8).Select(y => new Rectangle(0, y, 7, y + 1)).ToArray();
        var regions = Verify(strips);
        Check(regions.Count == 1 && regions[0].Contours.Count == 1
            && regions[0].Contours[0].Length == 4, "Eight strips become one four-corner outline/label.");
        Check(regions[0].LabelAnchor.Equals(new Point(0, 8)), "Label is at the combined upper-left corner.");

        var stepped = Enumerable.Range(0, 8)
            .Select(y => new Rectangle(0, y, y < 5 ? 10 : 7, y + 1)).ToArray();
        regions = Verify(stepped);
        Check(regions.Count == 1 && regions[0].Contours[0].Length == 6,
            "Screenshot-style stepped property follows the notch, not its bounding box.");

        regions = Verify(new Rectangle(0, 0, 3, 3), new Rectangle(2, 1, 5, 4),
            new Rectangle(0, 0, 3, 3), new Rectangle(1, 1, 2, 2));
        Check(regions.Count == 1, "Overlapping, duplicate, and nested plots union without internal lines.");

        regions = Verify(new Rectangle(0, 0, 3, 1), new Rectangle(2, 1, 4, 2));
        Check(regions.Count == 1 && regions[0].LabelAnchor.Equals(new Point(2, 2)),
            "Partial shared edges connect; anchor avoids the empty bounding-box corner.");
        Check(Verify(new Rectangle(0, 0, 1, 1), new Rectangle(1, 1, 2, 2)).Count == 2,
            "Corner-only touching plots remain separate regions.");
        Check(Verify(new Rectangle(-4, -3, -2, -1), new Rectangle(2, 2, 4, 4)).Count == 2,
            "Separated plots, including negative coordinates, keep separate labels.");
        Check(Verify(new Rectangle(0, 0, 0, 2), new Rectangle(0, 0, 2, 0),
            new Rectangle(2, 0, 1, 1)).Count == 0, "Invalid/empty rectangles are ignored.");
        Check(Verify().Count == 0, "Empty owner input clears all geometry.");

        regions = Verify(new Rectangle(0, 0, 5, 1), new Rectangle(0, 4, 5, 5),
            new Rectangle(0, 1, 1, 4), new Rectangle(4, 1, 5, 4));
        Check(regions.Count == 1 && regions[0].Contours.Count == 2,
            "Courtyard has one label and separate outer/hole outlines.");
        Check(regions[0].Contours.Count(loop => TwiceArea(loop) > 0) == 1
            && regions[0].Contours.Count(loop => TwiceArea(loop) < 0) == 1,
            "Outer/hole orientation keeps owned land on the left.");
        foreach (Point[] loop in regions[0].Contours)
        {
            double oldArea = Math.Abs(TwiceArea(loop));
            var inset = loop.Select((_, i) => NpcPropertyGeometry.InsetCorner(loop, i, 0.12f)).ToArray();
            double insetArea = Math.Abs(Enumerable.Range(0, inset.Length).Sum(i =>
                (double)inset[i].X * inset[(i + 1) % inset.Length].Y
                - (double)inset[(i + 1) % inset.Length].X * inset[i].Y));
            Check(TwiceArea(loop) > 0 ? insetArea < oldArea : insetArea > oldArea,
                "Shared-owner inset shrinks the outside and expands the hole, staying on owned land.");
        }

        // Shared footprints assembled differently must land at the same label
        // anchor, letting the renderer stack their labels and inset the lines.
        var whole = NpcPropertyGeometry.Build(new[] { new Rectangle(0, 0, 7, 8) });
        var split = NpcPropertyGeometry.Build(strips.Reverse());
        Check(whole[0].LabelAnchor.Equals(split[0].LabelAnchor), "Shared footprints use the same label slot.");
        Check(Edges(whole[0].Contours).SetEquals(Edges(split[0].Contours)),
            "Shared footprints have identical contours regardless of segmentation/order.");

        // No world-sized raster allocation: only the rectangle edge coordinates
        // are compressed, even across huge plots and remote islands.
        regions = NpcPropertyGeometry.Build(new[] {
            new Rectangle(-1000000000, -1000000000, 0, 0),
            new Rectangle(500000000, 500000000, 1000000000, 1000000000) });
        Check(regions.Count == 2 && regions.All(region => region.Contours[0].Length == 4),
            "Large coordinates/gaps do not expand into world tiles.");

        // All 3x3 tile arrangements exercise holes, diagonal contacts, thin
        // necks and pinched contours. Compare exact directed edges, not just area.
        for (int mask = 0; mask < 512; mask++)
        {
            var cells = new List<Rectangle>();
            for (int i = 0; i < 9; i++)
                if ((mask & (1 << i)) != 0)
                    cells.Add(new Rectangle(i % 3, i / 3, i % 3 + 1, i / 3 + 1));
            Verify(cells.ToArray());
        }

        var random = new Random(42817);
        for (int test = 0; test < 300; test++)
        {
            var rectangles = new List<Rectangle>();
            for (int i = 0, count = random.Next(1, 12); i < count; i++)
            {
                int left = random.Next(-5, 5), bottom = random.Next(-5, 5);
                rectangles.Add(new Rectangle(left, bottom, left + random.Next(1, 5), bottom + random.Next(1, 5)));
            }
            Verify(rectangles.ToArray());
        }
        Console.WriteLine($"PASS: {assertions} property-geometry assertions across {scenarios} exact-union scenarios.");
    }

    private static List<NpcPropertyGeometry.Region> Verify(params Rectangle[] rectangles)
    {
        scenarios++;
        var tiles = new HashSet<Point>();
        foreach (Rectangle rect in rectangles)
            for (int x = rect.Left; x < rect.Right; x++)
                for (int y = rect.Bottom; y < rect.Top; y++)
                    tiles.Add(new Point(x, y));
        var regions = NpcPropertyGeometry.Build(rectangles);
        var remaining = new HashSet<Point>(tiles);
        foreach (var region in regions)
        {
            Point seed = new Point(region.LabelAnchor.X, region.LabelAnchor.Y - 1);
            Check(remaining.Remove(seed), "One label per distinct connected occupied component.");
            var component = new HashSet<Point> { seed };
            var pending = new Queue<Point>();
            pending.Enqueue(seed);
            while (pending.Count > 0)
            {
                Point point = pending.Dequeue();
                foreach (Point neighbor in Neighbors(point))
                    if (remaining.Remove(neighbor)) { component.Add(neighbor); pending.Enqueue(neighbor); }
            }
            int top = component.Max(point => point.Y);
            int left = component.Where(point => point.Y == top).Min(point => point.X);
            Check(region.LabelAnchor.Equals(new Point(left, top + 1)), "Anchor is leftmost tile of the highest occupied row.");

            var expected = new HashSet<(Point, Point)>();
            foreach (Point tile in component)
            {
                int x = tile.X, y = tile.Y;
                if (!tiles.Contains(new Point(x, y - 1))) expected.Add((new Point(x, y), new Point(x + 1, y)));
                if (!tiles.Contains(new Point(x + 1, y))) expected.Add((new Point(x + 1, y), new Point(x + 1, y + 1)));
                if (!tiles.Contains(new Point(x, y + 1))) expected.Add((new Point(x + 1, y + 1), new Point(x, y + 1)));
                if (!tiles.Contains(new Point(x - 1, y))) expected.Add((new Point(x, y + 1), new Point(x, y)));
            }
            Check(Edges(region.Contours).SetEquals(expected), "Contours exactly match all and only exterior/hole edges.");
            Check(region.Contours.Sum(TwiceArea) == component.Count * 2L, "Signed contour area equals the owned area.");
        }
        Check(remaining.Count == 0, "All occupied cells appear in exactly one region.");
        return regions;
    }

    private static HashSet<(Point, Point)> Edges(IEnumerable<Point[]> contours)
    {
        var result = new HashSet<(Point, Point)>();
        foreach (Point[] loop in contours)
        {
            Check(loop.Length >= 4, "Closed orthogonal contour has at least four corners.");
            for (int i = 0; i < loop.Length; i++)
            {
                Point a = loop[i], b = loop[(i + 1) % loop.Length], c = loop[(i + 2) % loop.Length];
                Check((a.X == b.X) != (a.Y == b.Y), "Nonzero axis-aligned edge; no diagonal shortcut.");
                Check(!((a.X == b.X && b.X == c.X) || (a.Y == b.Y && b.Y == c.Y)),
                    "Collinear intermediate vertices are removed.");
                int dx = Math.Sign(b.X - a.X), dy = Math.Sign(b.Y - a.Y);
                for (Point p = a; !p.Equals(b); p = new Point(p.X + dx, p.Y + dy))
                    Check(result.Add((p, new Point(p.X + dx, p.Y + dy))), "No duplicate directed boundary edge.");
            }
        }
        return result;
    }

    private static IEnumerable<Point> Neighbors(Point point)
    {
        yield return new Point(point.X - 1, point.Y);
        yield return new Point(point.X + 1, point.Y);
        yield return new Point(point.X, point.Y - 1);
        yield return new Point(point.X, point.Y + 1);
    }

    private static long TwiceArea(Point[] loop) => Enumerable.Range(0, loop.Length).Sum(i =>
        (long)loop[i].X * loop[(i + 1) % loop.Length].Y - (long)loop[(i + 1) % loop.Length].X * loop[i].Y);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }
}
