// F5a: Folia's own icon geometry, transcribed from the shipped package (nothing invented below).
//
// Provenance: app.bin /dist/assets/lucide-icons-Ctk4ZAxW.js holds one entry per icon as
//   {name:`<lucide name>`,size:24,node:[[`path`,{d:`<d>`,key:`<k>`}],[`circle`,{cx,cy,r[,fill]}],[`rect`,{x,y,width,height,rx}]]}
// and /dist/assets/dynamicIconImports-gHWa1dpU.js maps the remote's names onto those exports ("repeat-1" -> gn, "repeat-off" -> fn).
// The remote's own icon table is docs SPEC-remote-motion.md Q7 (7.1 row, 7.2 loop three glyphs, 7.4 the three tools).
// Every d / cx / cy / r / x / y / width / height / rx here is byte-identical to that package data.
//
// Rendering model = lucide: 24x24 user space, fill:none, stroke:currentColor, stroke-width:2, round caps + round joins.
// Only the transport family (skip-back / skip-forward / play / pause) is filled, plus the heart when liked, exactly as
// SPEC-remote-motion.md 7.1 writes it (`skip-back(fill=currentColor)`, `heart(liked 时 fill=currentColor)`).
// The palette's four r=0.5 dots carry fill:currentColor in the package itself, so they are a separate filled layer.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Mini
{
    internal static class Icons
    {
        public const double StrokeW = 2.0;          // lucide stroke-width, in the 24x24 user space

        private static readonly Dictionary<string, string[]> PathData = new Dictionary<string, string[]>
        {
            { "repeat-off", new string[] {
                "M11.656 6H21l-4-4", "M17.898 17.898A4 4 0 0 1 17 18H3l4-4", "m2 2 20 20", "M21 13v1a4 4 0 0 1-.171 1.159", "m21 6-4 4", "M3 11v-1a4 4 0 0 1 3.102-3.898", "m7 22-4-4"
            } },
            { "repeat-1", new string[] {
                "m17 2 4 4-4 4", "M3 11v-1a4 4 0 0 1 4-4h14", "m7 22-4-4 4-4", "M21 13v1a4 4 0 0 1-4 4H3", "M11 10h1v4"
            } },
            { "repeat", new string[] {
                "m17 2 4 4-4 4", "M3 11v-1a4 4 0 0 1 4-4h14", "m7 22-4-4 4-4", "M21 13v1a4 4 0 0 1-4 4H3"
            } },
            { "list-music", new string[] {
                "M16 5H3", "M11 12H3", "M11 19H3", "M21 16V5"
            } },
            { "ellipsis", new string[0] },
            { "palette", new string[] {
                "M12 22a1 1 0 0 1 0-20 10 9 0 0 1 10 9 5 5 0 0 1-5 5h-2.25a1.75 1.75 0 0 0-1.4 2.8l.3.4a1.75 1.75 0 0 1-1.4 2.8z"
            } },
            { "pin-off", new string[] {
                "M12 17v5", "M15 9.34V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H7.89", "m2 2 20 20", "M9 9v1.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h11"
            } },
            { "pin", new string[] {
                "M12 17v5", "M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z"
            } },
            { "x", new string[] {
                "M18 6 6 18", "m6 6 12 12"
            } },
            { "skip-back", new string[] {
                "M17.971 4.285A2 2 0 0 1 21 6v12a2 2 0 0 1-3.029 1.715l-9.997-5.998a2 2 0 0 1-.003-3.432z", "M3 20V4"
            } },
            { "skip-forward", new string[] {
                "M21 4v16", "M6.029 4.285A2 2 0 0 0 3 6v12a2 2 0 0 0 3.029 1.715l9.997-5.998a2 2 0 0 0 .003-3.432z"
            } },
            { "play", new string[] {
                "M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z"
            } },
            { "pause", new string[0] },
            { "heart", new string[] {
                "M2 9.5a5.5 5.5 0 0 1 9.591-3.676.56.56 0 0 0 .818 0A5.49 5.49 0 0 1 22 9.5c0 2.29-1.5 4-3 5.5l-5.492 5.313a2 2 0 0 1-3 .019L5 15c-1.5-1.5-3-3.2-3-5.5"
            } },
            { "shuffle", new string[] {
                "m18 14 4 4-4 4", "m18 2 4 4-4 4", "M2 18h1.973a4 4 0 0 0 3.3-1.7l5.454-8.6a4 4 0 0 1 3.3-1.7H22", "M2 6h1.972a4 4 0 0 1 3.6 2.2", "M22 18h-6.041a4 4 0 0 1-3.3-1.8l-.359-.45"
            } },
        };

        // stroked circles: [cx, cy, r]
        private static readonly Dictionary<string, double[][]> Circles = new Dictionary<string, double[][]>
        {
            { "list-music", new double[][] { new double[] { 18, 16, 3 } } },
            { "ellipsis", new double[][] { new double[] { 12, 12, 1 }, new double[] { 19, 12, 1 }, new double[] { 5, 12, 1 } } },
        };

        // filled dots (package says fill:currentColor): [cx, cy, r]
        private static readonly Dictionary<string, double[][]> Dots = new Dictionary<string, double[][]>
        {
            { "palette", new double[][] { new double[] { 13.5, 6.5, 0.5 }, new double[] { 17.5, 10.5, 0.5 }, new double[] { 6.5, 12.5, 0.5 }, new double[] { 8.5, 7.5, 0.5 } } },
        };

        // stroked rounded rects: [x, y, width, height, rx]
        private static readonly Dictionary<string, double[][]> Rects = new Dictionary<string, double[][]>
        {
            { "pause", new double[][] { new double[] { 14, 3, 5, 18, 1 }, new double[] { 5, 3, 5, 18, 1 } } },
        };

        // Folia fills these (SPEC 7.1); everything else is pure stroke.
        private static readonly HashSet<string> Filled = new HashSet<string> { "skip-back", "skip-forward", "play", "pause" };

        public static bool Known(string name) { return PathData.ContainsKey(name); }
        public static bool FilledByFolia(string name) { return Filled.Contains(name); }

        private static Geometry Parse(string d)
        {
            Geometry g = Geometry.Parse(d);
            g.Freeze();
            return g;
        }

        // The stroked layer: every path figure + every stroked circle / rounded rect, in the 24x24 user space.
        public static Geometry StrokeGeometry(string name)
        {
            GeometryGroup gg = new GeometryGroup();
            gg.FillRule = FillRule.Nonzero;
            string[] ds;
            if (name != null && PathData.TryGetValue(name, out ds))
                for (int i = 0; i < ds.Length; i++) gg.Children.Add(Parse(ds[i]));
            double[][] cs;
            if (name != null && Circles.TryGetValue(name, out cs))
                for (int i = 0; i < cs.Length; i++) gg.Children.Add(new EllipseGeometry(new Point(cs[i][0], cs[i][1]), cs[i][2], cs[i][2]));
            double[][] rs;
            if (name != null && Rects.TryGetValue(name, out rs))
                for (int i = 0; i < rs.Length; i++)
                {
                    RectangleGeometry rg = new RectangleGeometry(new Rect(rs[i][0], rs[i][1], rs[i][2], rs[i][3]));
                    rg.RadiusX = rs[i][4];
                    rg.RadiusY = rs[i][4];
                    gg.Children.Add(rg);
                }
            if (gg.Children.Count == 0) return null;
            gg.Freeze();
            return gg;
        }

        // The filled-dot layer (palette); null when the glyph has none.
        public static Geometry DotGeometry(string name)
        {
            double[][] ds;
            if (name == null || !Dots.TryGetValue(name, out ds) || ds.Length == 0) return null;
            GeometryGroup gg = new GeometryGroup();
            for (int i = 0; i < ds.Length; i++) gg.Children.Add(new EllipseGeometry(new Point(ds[i][0], ds[i][1]), ds[i][2], ds[i][2]));
            gg.Freeze();
            return gg;
        }
    }
}
