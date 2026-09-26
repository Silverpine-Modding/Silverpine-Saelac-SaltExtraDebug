using System;
using System.Collections.Generic;
using System.Linq;

namespace SaltExtraDebug;

// Rendering-only union of rectangular plots. Coordinates are tile edges;
// the renderer applies Silverpine's half-tile world-space offset afterward.
internal static class NpcPropertyGeometry
{
    internal readonly struct Point : IEquatable<Point>
    {
        internal readonly int X;
        internal readonly int Y;

        internal Point(int x, int y) { X = x; Y = y; }
        public bool Equals(Point other) => X == other.X && Y == other.Y;
        public override bool Equals(object value) => value is Point other && Equals(other);
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
    }

    internal readonly struct Rectangle
    {
        internal readonly int Left, Bottom, Right, Top;

        internal Rectangle(int left, int bottom, int right, int top)
        {
            Left = left; Bottom = bottom; Right = right; Top = top;
        }
    }

    internal sealed class Region
    {
        internal readonly Point LabelAnchor;
        // Outer contours are counterclockwise; hole contours are clockwise.
        // In both cases the owned land is on the left of each directed edge.
        internal readonly List<Point[]> Contours;

        internal Region(Point labelAnchor, List<Point[]> contours)
        {
            LabelAnchor = labelAnchor;
            Contours = contours;
        }
    }

    private readonly struct Edge
    {
        internal readonly Point Start, End;
        internal readonly int Direction;

        internal Edge(Point start, Point end)
        {
            Start = start;
            End = end;
            Direction = end.X > start.X ? 0 : end.Y > start.Y ? 1
                : end.X < start.X ? 2 : 3;
        }
    }

    internal static List<Region> Build(IEnumerable<Rectangle> source)
    {
        Rectangle[] rectangles = source
            .Where(rect => rect.Right > rect.Left && rect.Top > rect.Bottom)
            .ToArray();
        var regions = new List<Region>();
        if (rectangles.Length == 0) return regions;

        // Coordinate compression avoids expanding a large plot (or a large
        // gap between properties) into one entry per world tile.
        int[] xs = rectangles.SelectMany(rect => new[] { rect.Left, rect.Right })
            .Distinct().OrderBy(value => value).ToArray();
        int[] ys = rectangles.SelectMany(rect => new[] { rect.Bottom, rect.Top })
            .Distinct().OrderBy(value => value).ToArray();
        var xIndex = xs.Select((value, index) => (value, index))
            .ToDictionary(pair => pair.value, pair => pair.index);
        var yIndex = ys.Select((value, index) => (value, index))
            .ToDictionary(pair => pair.value, pair => pair.index);
        int width = xs.Length - 1, height = ys.Length - 1;
        var occupied = new bool[width, height];
        foreach (Rectangle rect in rectangles)
        {
            int right = xIndex[rect.Right], top = yIndex[rect.Top];
            for (int x = xIndex[rect.Left]; x < right; x++)
                for (int y = yIndex[rect.Bottom]; y < top; y++)
                    occupied[x, y] = true;
        }

        var visited = new bool[width, height];
        var pending = new Queue<Point>();
        // Top-to-bottom gives each component an anchor on its actual top
        // occupied row, not an empty corner of an L-shaped bounding box.
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                if (!occupied[x, y] || visited[x, y]) continue;
                Point anchor = new Point(xs[x], ys[y + 1]);
                var edges = new List<Edge>();
                visited[x, y] = true;
                pending.Enqueue(new Point(x, y));
                while (pending.Count > 0)
                {
                    Point cell = pending.Dequeue();
                    int cx = cell.X, cy = cell.Y;
                    Point bottomLeft = new Point(xs[cx], ys[cy]);
                    Point bottomRight = new Point(xs[cx + 1], ys[cy]);
                    Point topRight = new Point(xs[cx + 1], ys[cy + 1]);
                    Point topLeft = new Point(xs[cx], ys[cy + 1]);
                    VisitOrOutline(cx, cy - 1, bottomLeft, bottomRight);
                    VisitOrOutline(cx + 1, cy, bottomRight, topRight);
                    VisitOrOutline(cx, cy + 1, topRight, topLeft);
                    VisitOrOutline(cx - 1, cy, topLeft, bottomLeft);
                }
                regions.Add(new Region(anchor, TraceContours(edges)));

                void VisitOrOutline(int nx, int ny, Point start, Point end)
                {
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height || !occupied[nx, ny])
                    {
                        edges.Add(new Edge(start, end));
                    }
                    else if (!visited[nx, ny])
                    {
                        visited[nx, ny] = true;
                        pending.Enqueue(new Point(nx, ny));
                    }
                }
            }
        }
        return regions;
    }

    private static List<Point[]> TraceContours(List<Edge> edges)
    {
        var outgoing = new Dictionary<Point, List<int>>();
        for (int i = 0; i < edges.Count; i++)
        {
            if (!outgoing.TryGetValue(edges[i].Start, out List<int> choices))
                outgoing.Add(edges[i].Start, choices = new List<int>(2));
            choices.Add(i);
        }

        var used = new bool[edges.Count];
        var contours = new List<Point[]>();
        for (int first = 0; first < edges.Count; first++)
        {
            if (used[first]) continue;
            var points = new List<Point>();
            int current = first;
            while (true)
            {
                Edge edge = edges[current];
                used[current] = true;
                points.Add(edge.Start);
                if (edge.End.Equals(edges[first].Start)) break;

                int next = -1, bestRank = int.MaxValue;
                foreach (int candidate in outgoing[edge.End])
                {
                    if (used[candidate]) continue;
                    int turn = (edges[candidate].Direction - edge.Direction + 4) % 4;
                    // At a corner-only contact, hug the land to the left.
                    // Never draw a diagonal bridge across unowned space.
                    int rank = turn == 1 ? 0 : turn == 0 ? 1 : turn == 3 ? 2 : 3;
                    if (rank < bestRank) { next = candidate; bestRank = rank; }
                }
                if (next < 0) throw new InvalidOperationException("Unclosed NPC property contour.");
                current = next;
            }

            var corners = new List<Point>();
            for (int i = 0; i < points.Count; i++)
            {
                Point previous = points[(i + points.Count - 1) % points.Count];
                Point point = points[i];
                Point next = points[(i + 1) % points.Count];
                if ((previous.X == point.X && point.X == next.X)
                    || (previous.Y == point.Y && point.Y == next.Y)) continue;
                corners.Add(point);
            }
            contours.Add(corners.ToArray());
        }
        return contours;
    }

    internal static (float X, float Y) InsetCorner(Point[] contour, int index, float inset)
    {
        Point previous = contour[(index + contour.Length - 1) % contour.Length];
        Point point = contour[index];
        Point next = contour[(index + 1) % contour.Length];
        // Both normals face owned land, including around holes/concave corners.
        int normalX = -Math.Sign((long)point.Y - previous.Y) - Math.Sign((long)next.Y - point.Y);
        int normalY = Math.Sign((long)point.X - previous.X) + Math.Sign((long)next.X - point.X);
        return (point.X + normalX * inset, point.Y + normalY * inset);
    }
}
