using System;
using System.Windows;
using System.Windows.Media;

namespace WpfWebView2Poc
{
    /// <summary>
    /// Custom WPF Control implementing Chromium Blink's complete C++ border rendering engine.
    /// Handles solid, dashed, and dotted borders across all thickness levels and corner radii:
    /// - Dashed with rOuter < T (rInner == 0): 4 solid L-shaped corner caps (arm = 3.0T) + flat intermediate dashes, clipped by outer rounded border ring.
    /// - Dashed with rOuter >= T (rInner > 0): Skia-aligned perimeter dash scaling along Rmid path.
    /// - Dotted: Unified Chromium dotted architecture matching CSS Level 3 spec.
    /// </summary>
    public class ChromiumBorderCanvas : FrameworkElement
    {
        public static readonly DependencyProperty BorderStyleProperty =
            DependencyProperty.Register(nameof(BorderStyle), typeof(string), typeof(ChromiumBorderCanvas),
                new FrameworkPropertyMetadata("solid", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(ChromiumBorderCanvas),
                new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(ChromiumBorderCanvas),
                new FrameworkPropertyMetadata(Brushes.Blue, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(ChromiumBorderCanvas),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public string BorderStyle
        {
            get => (string)GetValue(BorderStyleProperty);
            set => SetValue(BorderStyleProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        { base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            double t = StrokeThickness;
            double r = CornerRadius;
            Brush brush = Stroke;

            if (w <= 0 || h <= 0 || t <= 0 || brush == null) return;

            // CSS Level 3 Inner Radius Geometry Math
            double rOuter = Math.Min(r, Math.Min(w / 2.0, h / 2.0));
            double rInner = Math.Max(0, rOuter - t);

            // Case 1: Solid Border -> Draw filled border ring directly
            if (BorderStyle == "solid")
            {
                Geometry borderRing = CreateChromiumBorderRingGeometry(w, h, t, rOuter, rInner);
                dc.DrawGeometry(brush, null, borderRing);
                return;
            }

            double halfT = t / 2.0;
            Rect strokeRect = new Rect(halfT, halfT, Math.Max(0, w - t), Math.Max(0, h - t));

            // Case 2: Dashed Border (Chromium Blink C++ Algorithm)
            if (BorderStyle == "dashed")
            {
                Geometry borderRingClip = CreateChromiumBorderRingGeometry(w, h, t, rOuter, rInner);
                dc.PushClip(borderRingClip);
                DrawChromiumDashedBorder(dc, brush, strokeRect, w, h, t, rOuter);
                dc.Pop();
                return;
            }

            // Case 3: Dotted Border (Chromium Blink C++ Algorithm)
            if (BorderStyle == "dotted")
            {
                if (rOuter > 0 && t < rOuter)
                {
                    // Low/Medium thickness relative to corner radius (T < rOuter):
                    // Render rounded path dots along perimeter WITHOUT clip mask so dots remain 360-degree round circles.
                    DrawChromiumRoundedPathDots(dc, brush, strokeRect, w, h, t, rOuter);
                }
                else
                {
                    // High thickness (T >= rOuter) OR Sharp corners (rOuter == 0):
                    // Push borderRingClip mask (rInner = Max(0, rOuter - T)) & render vertex corner dots for exact teardrop clipping!
                    Geometry borderRingClip = CreateChromiumBorderRingGeometry(w, h, t, rOuter, rInner);
                    dc.PushClip(borderRingClip);
                    DrawChromiumSharpDottedBorder(dc, brush, strokeRect, t);
                    dc.Pop();
                }
            }
        }

        private Geometry CreateChromiumBorderRingGeometry(double w, double h, double t, double rOuter, double rInner)
        {
            Rect outerRect = new Rect(0, 0, w, h);
            RectangleGeometry outerGeo = new RectangleGeometry(outerRect, rOuter, rOuter);

            double innerW = Math.Max(0, w - 2 * t);
            double innerH = Math.Max(0, h - 2 * t);

            if (innerW <= 0 || innerH <= 0)
            {
                return outerGeo; // Solid block
            }

            Rect innerRect = new Rect(t, t, innerW, innerH);
            RectangleGeometry innerGeo = new RectangleGeometry(innerRect, rInner, rInner);

            return new CombinedGeometry(GeometryCombineMode.Exclude, outerGeo, innerGeo);
        }

        private void DrawChromiumDashedBorder(DrawingContext dc, Brush brush, Rect rect, double w, double h, double t, double rOuter)
        {
            Pen pen = new Pen(brush, t)
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat
            };

            // Chromium Blink C++ Logic for Dashed Border with CornerRadius:
            // Continuous perimeter Skia path is ONLY used when rOuter >= T (where inner radius rInner > 0).
            // When rOuter < T (rInner == 0), Blink renders 4 solid L-shaped corner caps (arm = 3.0T)
            // clipped by the outer rounded geometry mask (borderRingClip)!
            if (rOuter >= t && rOuter > 0)
            {
                // Rounded dashed path matching Chromium Skia when rInner > 0
                double rMid = Math.Max(0, rOuter - t / 2.0);
                double straightX = Math.Max(0, w - 2 * rOuter);
                double straightY = Math.Max(0, h - 2 * rOuter);
                double perimeter = 2 * straightX + 2 * straightY + 2 * Math.PI * rMid;

                double idealCycle = 3.0 * t;
                int k = Math.Max(1, (int)Math.Round(perimeter / idealCycle));
                double actualCycle = perimeter / k;

                double dashLen = actualCycle * (2.0 / 3.0);
                double gapLen = actualCycle * (1.0 / 3.0);

                Pen dashedPen = new Pen(brush, t)
                {
                    DashCap = PenLineCap.Round,
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    DashStyle = new DashStyle(new double[] { dashLen / t, gapLen / t }, 0)
                };

                dc.DrawRoundedRectangle(null, dashedPen, rect, rMid, rMid);
                return;
            }

            // Sharp 90-degree OR Hybrid Outer Rounded Corners (rOuter < T):
            // Blink 4-Corner L-Cap (arm = 2.0T ideal dash length) + Centered Edge Dashes clipped by borderRingClip
            double arm = 2.0 * t; // Dynamic corner L-arm length matching 2.0 * T ideal dash length

            double left = rect.Left;
            double top = rect.Top;
            double right = rect.Right;
            double bottom = rect.Bottom;

            // 1. Draw 4 Corner L-Dashes extending to outer boundaries (0, w, h)
            // Top-Left L
            dc.DrawLine(pen, new Point(0, top), new Point(arm, top));
            dc.DrawLine(pen, new Point(left, 0), new Point(left, arm));

            // Top-Right L
            dc.DrawLine(pen, new Point(w - arm, top), new Point(w, top));
            dc.DrawLine(pen, new Point(right, 0), new Point(right, arm));

            // Bottom-Right L
            dc.DrawLine(pen, new Point(w - arm, bottom), new Point(w, bottom));
            dc.DrawLine(pen, new Point(right, h - arm), new Point(right, h));

            // Bottom-Left L
            dc.DrawLine(pen, new Point(0, bottom), new Point(arm, bottom));
            dc.DrawLine(pen, new Point(left, h - arm), new Point(left, h));

            // 2. Top & Bottom Edge Dashes (Chromium exact numDashes = round((W - 4T) / 3T) math)
            double gx = w - 2.0 * arm; // Available space between 2 corner L-arms (from arm to W - arm)
            if (gx > 0)
            {
                int numDashes = Math.Max(0, (int)Math.Round((w - 4.0 * t) / (3.0 * t)));

                if (numDashes > 0)
                {
                    double idealDash = 2.0 * t;
                    double actualDash = Math.Min(idealDash, (gx * 0.6) / numDashes);
                    double remGapSpace = gx - numDashes * actualDash;
                    double actualGap = remGapSpace / (numDashes + 1);

                    Point topStart = new Point(arm, top);
                    Point bottomStart = new Point(arm, bottom);
                    Vector dir = new Vector(1, 0);

                    for (int i = 0; i < numDashes; i++)
                    {
                        Point startPoint = topStart + dir * (actualGap + i * (actualDash + actualGap));
                        Point endPoint = startPoint + dir * actualDash;
                        dc.DrawLine(pen, startPoint, endPoint);

                        Point startPointB = bottomStart + dir * (actualGap + i * (actualDash + actualGap));
                        Point endPointB = startPointB + dir * actualDash;
                        dc.DrawLine(pen, startPointB, endPointB);
                    }
                }
            }

            // 3. Left & Right Edge Dashes (Chromium exact numDashes = round((H - 4T) / 3T) math)
            double gy = h - 2.0 * arm; // Available space between 2 corner L-arms (from arm to H - arm)
            if (gy > 0)
            {
                int numDashes = Math.Max(0, (int)Math.Round((h - 4.0 * t) / (3.0 * t)));

                if (numDashes > 0)
                {
                    double idealDash = 2.0 * t;
                    double actualDash = Math.Min(idealDash, (gy * 0.6) / numDashes);
                    double remGapSpace = gy - numDashes * actualDash;
                    double actualGap = remGapSpace / (numDashes + 1);

                    Point leftStart = new Point(left, arm);
                    Point rightStart = new Point(right, arm);
                    Vector dirV = new Vector(0, 1);

                    for (int j = 0; j < numDashes; j++)
                    {
                        Point startPointL = leftStart + dirV * (actualGap + j * (actualDash + actualGap));
                        Point endPointL = startPointL + dirV * actualDash;
                        dc.DrawLine(pen, startPointL, endPointL);

                        Point startPointR = rightStart + dirV * (actualGap + j * (actualDash + actualGap));
                        Point endPointR = startPointR + dirV * actualDash;
                        dc.DrawLine(pen, startPointR, endPointR);
                    }
                }
            }
        }

        private void DrawChromiumRoundedPathDots(DrawingContext dc, Brush brush, Rect rect, double w, double h, double t, double rOuter)
        {
            double halfT = t / 2.0;
            double rMid = Math.Max(0, rOuter - halfT);

            double straightX = Math.Max(0, w - 2 * rOuter);
            double straightY = Math.Max(0, h - 2 * rOuter);
            double perimeter = 2 * straightX + 2 * straightY + 2 * Math.PI * rMid;

            double idealCycle = 2.0 * t;
            int k = Math.Max(1, (int)Math.Round(perimeter / idealCycle));
            double actualCycle = perimeter / k;

            double dashLen = 0.0001;
            double dashRatio = dashLen / t;
            double gapRatio = (actualCycle - dashLen) / t;

            Pen dottedPen = new Pen(brush, t)
            {
                DashCap = PenLineCap.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                DashStyle = new DashStyle(new double[] { dashRatio, gapRatio }, 0)
            };

            dc.DrawRoundedRectangle(null, dottedPen, rect, rMid, rMid);
        }

        private void DrawChromiumSharpDottedBorder(DrawingContext dc, Brush brush, Rect rect, double t)
        {
            double radius = t / 2.0;

            double left = rect.Left;
            double top = rect.Top;
            double right = rect.Right;
            double bottom = rect.Bottom;

            // 1. Draw 4 Corner Dots (Positioned at vertex centers T/2, T/2)
            Point tl = new Point(left, top);
            Point tr = new Point(right, top);
            Point br = new Point(right, bottom);
            Point bl = new Point(left, bottom);

            dc.DrawEllipse(brush, null, tl, radius, radius);
            dc.DrawEllipse(brush, null, tr, radius, radius);
            dc.DrawEllipse(brush, null, br, radius, radius);
            dc.DrawEllipse(brush, null, bl, radius, radius);

            // CSS Spec Dot Spacing Ratio: idealSpacing = 2.0 * T
            double idealSpacing = 2.0 * t;

            // 2. Top & Bottom Intermediate Dots
            double lx = right - left; // W - T
            if (lx > 0)
            {
                int n = Math.Max(1, (int)Math.Round(lx / idealSpacing));
                double sx = lx / n;

                for (int i = 1; i < n; i++)
                {
                    double x = left + i * sx;
                    dc.DrawEllipse(brush, null, new Point(x, top), radius, radius);
                    dc.DrawEllipse(brush, null, new Point(x, bottom), radius, radius);
                }
            }

            // 3. Left & Right Intermediate Dots
            double ly = bottom - top; // H - T
            if (ly > 0)
            {
                int m = Math.Max(1, (int)Math.Round(ly / idealSpacing));
                double sy = ly / m;

                for (int j = 1; j < m; j++)
                {
                    double y = top + j * sy;
                    dc.DrawEllipse(brush, null, new Point(left, y), radius, radius);
                    dc.DrawEllipse(brush, null, new Point(right, y), radius, radius);
                }
            }
        }
    }
}
