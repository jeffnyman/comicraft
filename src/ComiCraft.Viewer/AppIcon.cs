namespace ComiCraft.Viewer;

/// <summary>
/// ComiCraft's own icon, drawn here rather than kept as a picture, so that the window and the
/// <c>.ico</c> the executable carries (<c>tools/icon</c> writes it) come from the same drawing: the
/// open comic book of the title art, its cream pages in heavy black outlines, with four panels, a
/// sunburst, a sun over clouds, a city's skyline and a caped hero on a rooftop.
/// </summary>
public static class AppIcon
{
    private const int Samples = 4;

    private const double PageOutline = 0.024, PanelOutline = 0.014, LineOutline = 0.006;

    private static readonly (double R, double G, double B) Ink = (0.06, 0.06, 0.08);
    private static readonly (double R, double G, double B) Paper = (0.99, 0.95, 0.85);
    private static readonly (double R, double G, double B) PageEdges = (0.88, 0.76, 0.54);
    private static readonly (double R, double G, double B) Navy = (0.07, 0.16, 0.32);

    /// <summary>A page: its corners at the top and bottom of its outer edge and of the spine, and how far its top and bottom bow up.</summary>
    private sealed record Page((double X, double Y) TopLeft, (double X, double Y) TopRight, (double X, double Y) BottomRight, (double X, double Y) BottomLeft, double Bow)
    {
        /// <summary>The point at <paramref name="s"/> across the page and <paramref name="t"/> down it, each from 0 to 1.</summary>
        public (double X, double Y) At(double s, double t)
        {
            double x = (1 - t) * ((1 - s) * TopLeft.X + s * TopRight.X) + t * ((1 - s) * BottomLeft.X + s * BottomRight.X);
            double y = (1 - t) * ((1 - s) * TopLeft.Y + s * TopRight.Y) + t * ((1 - s) * BottomLeft.Y + s * BottomRight.Y);
            return (x, y - Bow * 4 * s * (1 - s));
        }

        /// <summary>The outline of the part of the page from s0 to s1 across and t0 to t1 down.</summary>
        public (double X, double Y)[] Outline(double s0, double s1, double t0, double t1)
        {
            const int steps = 12;
            var points = new List<(double, double)>();
            for (int i = 0; i < steps; i++)
            {
                points.Add(At(s0 + (s1 - s0) * i / steps, t0));
            }

            for (int i = 0; i < steps; i++)
            {
                points.Add(At(s1, t0 + (t1 - t0) * i / steps));
            }

            for (int i = 0; i < steps; i++)
            {
                points.Add(At(s1 - (s1 - s0) * i / steps, t1));
            }

            for (int i = 0; i < steps; i++)
            {
                points.Add(At(s0, t1 - (t1 - t0) * i / steps));
            }

            return [.. points];
        }
    }

    // the left page leans out at the bottom, as the right does; both rise from the spine to their
    // outer corners, and meet at it, the right page's outline the line between them
    private static readonly Page Left = new((0.12, 0.15), (0.505, 0.29), (0.505, 0.85), (0.04, 0.74), 0.035);
    private static readonly Page Right = new((0.495, 0.29), (0.92, 0.20), (0.95, 0.75), (0.495, 0.85), 0.035);

    /// <summary>The pages under each page, showing at its outer edge and bottom.</summary>
    private static readonly (double X, double Y)[] LeftUnder = Shift(Left.Outline(0, 1, 0, 1), -0.022, 0.03);
    private static readonly (double X, double Y)[] RightUnder = Shift(Right.Outline(0, 1, 0, 1), 0.022, 0.03);
    private static readonly (double X, double Y)[] LeftPage = Left.Outline(0, 1, 0, 1);
    private static readonly (double X, double Y)[] RightPage = Right.Outline(0, 1, 0, 1);

    private enum Scene { Sunburst, Sky, City, Hero }

    private static readonly ((double X, double Y)[] Outline, Scene Scene, (double X0, double Y0, double X1, double Y1) Box)[] Panels =
    [
        Panel(Left.Outline(0.10, 0.92, 0.08, 0.48), Scene.Sunburst),
        Panel(Left.Outline(0.10, 0.92, 0.54, 0.92), Scene.City),
        Panel(Right.Outline(0.08, 0.90, 0.08, 0.44), Scene.Sky),
        Panel(Right.Outline(0.08, 0.90, 0.50, 0.92), Scene.Hero),
    ];

    /// <summary>The icon at a size, as rows of red, green, blue and alpha bytes, alpha not premultiplied.</summary>
    public static byte[] Render(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        var pixels = new byte[size * size * 4];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < Samples; sy++)
                {
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        double u = (px + (sx + 0.5) / Samples) / size, v = (py + (sy + 0.5) / Samples) / size;
                        var (cr, cg, cb, ca) = Shade(u, v);
                        r += cr * ca;
                        g += cg * ca;
                        b += cb * ca;
                        a += ca;
                    }
                }

                int i = (py * size + px) * 4;
                double n = Samples * Samples;
                pixels[i + 3] = ToByte(a / n);
                if (a > 0)
                {
                    pixels[i] = ToByte(r / a);
                    pixels[i + 1] = ToByte(g / a);
                    pixels[i + 2] = ToByte(b / a);
                }
            }
        }

        return pixels;
    }

    /// <summary>The colour and cover at a point of the icon, from 0 to 1 across and down.</summary>
    private static (double R, double G, double B, double A) Shade(double u, double v)
    {
        if (u < 0.0 || v < 0.1 || v > 0.93)
        {
            return (0, 0, 0, 0);
        }

        // the pages underneath, then each page, each in its outline; what is outside all of them is not drawn
        double under = Math.Min(PolygonDistance(u, v, LeftUnder), PolygonDistance(u, v, RightUnder));
        double alpha = Cover(PageOutline / 2 - under);
        var colour = Ink;
        colour = Mix(colour, PageEdges, Cover(-PageOutline / 2 - under));

        foreach (var page in new[] { LeftPage, RightPage })
        {
            double d = PolygonDistance(u, v, page);
            alpha = Math.Max(alpha, Cover(PageOutline / 2 - d));
            colour = Mix(colour, Ink, Cover(PageOutline / 2 - d));
            colour = Mix(colour, Paper, Cover(-PageOutline / 2 - d));
        }

        if (alpha <= 0)
        {
            return (0, 0, 0, 0);
        }

        foreach (var (outline, scene, box) in Panels)
        {
            double d = PolygonDistance(u, v, outline);
            double outer = Cover(PanelOutline / 2 - d);
            if (outer <= 0)
            {
                continue;
            }

            colour = Mix(colour, Ink, outer);
            colour = Mix(colour, Draw(scene, u, v, box), Cover(-PanelOutline / 2 - d));
        }

        return (colour.R, colour.G, colour.B, alpha);
    }

    /// <summary>A panel's picture at a point, with the panel's bounds to place it by.</summary>
    private static (double R, double G, double B) Draw(Scene scene, double u, double v, (double X0, double Y0, double X1, double Y1) box)
    {
        double w = box.X1 - box.X0, h = box.Y1 - box.Y0;
        double s = (u - box.X0) / w, t = (v - box.Y0) / h;
        (double X, double Y) At(double ps, double pt) => (box.X0 + ps * w, box.Y0 + pt * h);
        (double, double, double) colour;

        switch (scene)
        {
            case Scene.Sunburst:
            {
                // a twelve-pointed burst of yellow on orange
                var c = At(0.5, 0.58);
                double dx = u - c.X, dy = v - c.Y, angle = Math.Atan2(dy, dx), distance = Math.Sqrt(dx * dx + dy * dy);
                double point = 1 - 2 * Math.Abs((angle / (2 * Math.PI) * 12 % 1 + 1) % 1 - 0.5);
                double edge = h * (0.30 + 0.30 * point);
                double d = distance - edge;
                colour = Mix((0.97, 0.56, 0.08), (1.0, 0.70, 0.12), Math.Clamp(1 - distance / h, 0, 1));
                colour = Mix(colour, Ink, Cover(LineOutline - Math.Abs(d)));
                colour = Mix(colour, (1.0, 0.86, 0.16), Cover(-LineOutline - d));
                break;
            }

            case Scene.Sky:
            {
                // a pale sun in a blue sky, over a bank of dark clouds
                colour = Mix((0.12, 0.50, 0.90), (0.30, 0.66, 0.96), t);
                var sun = At(0.74, 0.36);
                double sunD = Distance(u, v, sun) - h * 0.20;
                colour = Mix(colour, (0.98, 0.91, 0.74), Cover(-sunD));
                double cloud = double.MaxValue;
                for (int i = 0; i <= 5; i++)
                {
                    var centre = At(i / 5.0, i % 2 == 0 ? 0.82 : 0.74);
                    cloud = Math.Min(cloud, Distance(u, v, centre) - h * 0.17);
                }

                cloud = Math.Min(cloud, box.Y0 + 0.86 * h - v);
                colour = Mix(colour, Ink, Cover(LineOutline - Math.Abs(cloud)));
                colour = Mix(colour, Navy, Cover(-LineOutline - cloud));
                break;
            }

            case Scene.City:
            {
                // a skyline against a blue sky, with spires on two of its towers
                colour = Mix((0.12, 0.52, 0.92), (0.28, 0.66, 0.96), t);
                (double S0, double S1, double Top)[] towers = [(-0.1, 0.16, 0.50), (0.16, 0.28, 0.24), (0.28, 0.42, 0.56), (0.42, 0.56, 0.38), (0.56, 0.68, 0.62), (0.68, 0.80, 0.30), (0.80, 1.1, 0.52)];
                double sky = double.MaxValue;
                foreach (var (s0, s1, top) in towers)
                {
                    if (s >= s0 && s < s1)
                    {
                        sky = (top - t) * h;
                    }
                }

                foreach (double spire in new[] { 0.22, 0.74 })
                {
                    double tip = (spire < 0.5 ? 0.24 : 0.30) - 0.16;
                    double half = 0.035 * (t - tip) / 0.16;
                    if (t > tip && Math.Abs(s - spire) * w < half * w)
                    {
                        sky = Math.Min(sky, -1.0 / 128);
                    }
                }

                colour = Mix(colour, Navy, Cover(-sky));
                break;
            }

            default:
            {
                // a caped figure in black on red, standing on a rooftop, the cape flying out behind
                colour = Mix((0.95, 0.30, 0.24), (0.86, 0.18, 0.18), t);
                double figure = Distance(u, v, At(0.36, 0.25)) - h * 0.08;
                figure = Math.Min(figure, SegmentDistance(u, v, At(0.36, 0.36), At(0.37, 0.56)) - h * 0.09);
                figure = Math.Min(figure, SegmentDistance(u, v, At(0.35, 0.56), At(0.28, 0.76)) - h * 0.05);
                figure = Math.Min(figure, SegmentDistance(u, v, At(0.39, 0.56), At(0.47, 0.76)) - h * 0.05);
                figure = Math.Min(figure, PolygonDistance(u, v,
                    [At(0.36, 0.33), At(0.55, 0.20), At(0.75, 0.24), At(0.93, 0.18), At(0.84, 0.34), At(0.90, 0.48), At(0.68, 0.46), At(0.56, 0.58), At(0.40, 0.52)]));
                double roof = Math.Max(Math.Max(box.Y0 + 0.76 * h - v, u - At(0.66, 0).X), At(0.06, 0).X - u);
                colour = Mix(colour, Navy, Cover(-roof));
                colour = Mix(colour, Ink, Cover(-figure));
                break;
            }
        }

        // halftone dots over the whole panel, as printed comics have
        const double pitch = 1.0 / 48;
        double gx = (u + v) / pitch, gy = (u - v) / pitch;
        double ddx = gx - Math.Round(gx), ddy = gy - Math.Round(gy);
        double dot = Math.Sqrt(ddx * ddx + ddy * ddy) * pitch / Math.Sqrt(2);
        return Mix(colour, (colour.Item1 * 0.82, colour.Item2 * 0.82, colour.Item3 * 0.82), Cover(pitch * 0.22 - dot));
    }

    private static ((double X, double Y)[] Outline, Scene Scene, (double X0, double Y0, double X1, double Y1) Box) Panel((double X, double Y)[] outline, Scene scene) =>
        (outline, scene, (outline.Min(p => p.X), outline.Min(p => p.Y), outline.Max(p => p.X), outline.Max(p => p.Y)));

    private static (double X, double Y)[] Shift((double X, double Y)[] points, double dx, double dy) =>
        [.. points.Select(p => (p.X + dx, p.Y + dy))];

    /// <summary>The signed distance from a polygon: negative inside.</summary>
    private static double PolygonDistance(double u, double v, (double X, double Y)[] points)
    {
        double d = double.MaxValue;
        bool inside = false;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            var (a, b) = (points[j], points[i]);
            d = Math.Min(d, SegmentDistance(u, v, a, b));
            if ((a.Y > v) != (b.Y > v) && u < a.X + (v - a.Y) * (b.X - a.X) / (b.Y - a.Y))
            {
                inside = !inside;
            }
        }

        return inside ? -d : d;
    }

    /// <summary>Cover from a signed distance in tile units: a 1/128 edge, about a pixel at the largest size.</summary>
    private static double Cover(double d) => Math.Clamp(d * 128 + 0.5, 0, 1);

    private static double Distance(double u, double v, (double X, double Y) p) => Math.Sqrt((u - p.X) * (u - p.X) + (v - p.Y) * (v - p.Y));

    private static double SegmentDistance(double u, double v, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t = Math.Clamp(((u - a.X) * dx + (v - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return Distance(u, v, (a.X + t * dx, a.Y + t * dy));
    }

    private static (double, double, double) Mix((double R, double G, double B) under, (double R, double G, double B) over, double amount)
        => (under.R + (over.R - under.R) * amount, under.G + (over.G - under.G) * amount, under.B + (over.B - under.B) * amount);

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
