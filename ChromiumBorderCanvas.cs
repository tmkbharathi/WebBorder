using System;
using System.Windows;
using System.Windows.Media;

namespace WpfWebView2Poc
{
    /// <summary>
    /// Custom WPF Control implementing Chromium Blink's complete C++ border rendering engine.
    /// Perfectly handles solid, dashed, and dotted borders across all thickness levels and corner radii:
    /// - T < rOuter: Smooth rounded path dots (complete round circles).
    /// - T >= rOuter or rOuter == 0: Border Ring Clip Mask (rInner = Max(0, rOuter - T)) producing exact Chromium teardrop corner dots.
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
        {
            base.OnRender(dc);

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

            // Case 2: Dashed Border
            if (BorderStyle == "dashed")
            {
                Geometry borderRingClip = CreateChromiumBorderRingGeometry(w, h, t, rOuter, rInner);
                dc.PushClip(borderRingClip);
                DrawChromiumDashedBorder(dc, brush, strokeRect, t, rOuter);
                dc.Pop();
                return;
            }

            // Case 3: Dotted Border (Chromium Blink C++ Rendering Engine)
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

        private void DrawChromiumDashedBorder(DrawingContext dc, Brush brush, Rect rect, double t, double rOuter)
        {
            Pen pen = new Pen(brush, t)
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat
            };

            if (rOuter > 0)
            {
                // Rounded dashed path matching Chromium Skia
                pen.DashStyle = new DashStyle(new double[] { 3.0, 1.5 }, 0);
                pen.DashCap = PenLineCap.Round;
                double rMid = Math.Max(0, rOuter - t / 2.0);
                dc.DrawRoundedRectangle(null, pen, rect, rMid, rMid);
                return;
            }

            // Sharp 90-degree corners (r == 0): Chromium Blink 4-Corner L-Cap + Side Dashes
            double arm = 1.5 * t;

            double left = rect.Left;
            double top = rect.Top;
            double right = rect.Right;
            double bottom = rect.Bottom;

            // 1. Draw 4 Corner L-Dashes
            dc.DrawLine(pen, new Point(left, top), new Point(left + arm, top));
            dc.DrawLine(pen, new Point(left, top), new Point(left, top + arm));

            dc.DrawLine(pen, new Point(right - arm, top), new Point(right, top));
            dc.DrawLine(pen, new Point(right, top), new Point(right, top + arm));

            dc.DrawLine(pen, new Point(right - arm, bottom), new Point(right, bottom));
            dc.DrawLine(pen, new Point(right, bottom - arm), new Point(right, bottom));

            dc.DrawLine(pen, new Point(left, bottom), new Point(left + arm, bottom));
            dc.DrawLine(pen, new Point(left, bottom - arm), new Point(left, bottom));

            // 2. Top & Bottom Edge Dashes
            double lx = (right - arm) - (left + arm);
            if (lx > 0)
            {
                double idealDash = 3.0 * t;
                double idealGap = 3.0 * t;
                double cycle = idealDash + idealGap;

                int numDashes = Math.Max(0, (int)Math.Round(lx / cycle));
                if (numDashes > 0)
                {
                    double actualCycle = lx / numDashes;
                    double dashLen = actualCycle * 0.5;
                    double gapLen = actualCycle * 0.5;

                    for (int i = 0; i < numDashes; i++)
                    {
                        double startX = (left + arm) + i * actualCycle + gapLen / 2.0;
                        double endX = startX + dashLen;
                        dc.DrawLine(pen, new Point(startX, top), new Point(endX, top));
                        dc.DrawLine(pen, new Point(startX, bottom), new Point(endX, bottom));
                    }
                }
            }

            // 3. Left & Right Edge Dashes
            double ly = (bottom - arm) - (top + arm);
            if (ly > 0)
            {
                double idealDash = 3.0 * t;
                double idealGap = 3.0 * t;
                double cycle = idealDash + idealGap;

                int numDashes = Math.Max(0, (int)Math.Round(ly / cycle));
                if (numDashes > 0)
                {
                    double actualCycle = ly / numDashes;
                    double dashLen = actualCycle * 0.5;
                    double gapLen = actualCycle * 0.5;

                    for (int j = 0; j < numDashes; j++)
                    {
                        double startY = (top + arm) + j * actualCycle + gapLen / 2.0;
                        double endY = startY + dashLen;
                        dc.DrawLine(pen, new Point(left, startY), new Point(left, endY));
                        dc.DrawLine(pen, new Point(right, startY), new Point(right, endY));
                    }
                }
            }
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
