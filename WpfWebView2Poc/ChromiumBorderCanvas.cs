using System;
using System.Windows;
using System.Windows.Media;

namespace WpfWebView2Poc
{
    /// <summary>
    /// Custom WPF Control implementing Chromium/Skia's exact CSS border geometry algorithm.
    /// Strictly enforces CSS Level 3 Inner Radius spec: InnerRadius = Max(0, OuterRadius - BorderThickness).
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

            // CSS Specification Corner Radius Math:
            // Outer Radius = r
            // Inner Radius = Max(0, r - t)
            double rOuter = Math.Min(r, Math.Min(w / 2.0, h / 2.0));
            double rInner = Math.Max(0, rOuter - t);

            // Case 1: Solid Border (Geometry Combination matching CSS Inner/Outer Radius)
            if (BorderStyle == "solid")
            {
                Geometry borderGeo = CreateCssBorderGeometry(w, h, t, rOuter, rInner);
                dc.DrawGeometry(brush, null, borderGeo);
                return;
            }

            // Case 2: Rounded Corners (r > 0) -> Chromium Skia Dashed / Dotted Path
            if (r > 0)
            {
                double halfT = t / 2.0;
                Rect midRect = new Rect(halfT, halfT, Math.Max(0, w - t), Math.Max(0, h - t));
                double rMid = Math.Max(0, rOuter - halfT);

                Pen roundedPen = new Pen(brush, t)
                {
                    DashCap = PenLineCap.Round,
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };

                if (BorderStyle == "dashed")
                {
                    roundedPen.DashStyle = new DashStyle(new double[] { 3.0, 1.5 }, 0);
                }
                else if (BorderStyle == "dotted")
                {
                    roundedPen.DashStyle = new DashStyle(new double[] { 1.0, 2.0 }, 0);
                }

                dc.DrawRoundedRectangle(null, roundedPen, midRect, rMid, rMid);
                return;
            }

            // Case 3: Sharp 90-Degree Corners (r == 0) -> Chromium Blink 4-Corner L-Cap + Even Dashes
            double halfStroke = t / 2.0;
            Rect strokeRect = new Rect(halfStroke, halfStroke, Math.Max(0, w - t), Math.Max(0, h - t));

            Pen pen = new Pen(brush, t)
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat
            };

            if (BorderStyle == "dashed")
            {
                DrawChromiumDashedBorder(dc, pen, strokeRect, t);
            }
            else if (BorderStyle == "dotted")
            {
                DrawChromiumDottedBorder(dc, brush, strokeRect, t);
            }
        }

        /// <summary>
        /// Creates a combined geometry subtracting the Inner Rounded Rectangle from the Outer Rounded Rectangle.
        /// Strictly implements CSS Spec: InnerRadius = Max(0, OuterRadius - Thickness).
        /// </summary>
        private Geometry CreateCssBorderGeometry(double w, double h, double t, double rOuter, double rInner)
        {
            Rect outerRect = new Rect(0, 0, w, h);
            RectangleGeometry outerGeo = new RectangleGeometry(outerRect, rOuter, rOuter);

            double innerW = Math.Max(0, w - 2 * t);
            double innerH = Math.Max(0, h - 2 * t);

            if (innerW <= 0 || innerH <= 0)
            {
                return outerGeo; // Border fills entire box
            }

            Rect innerRect = new Rect(t, t, innerW, innerH);
            RectangleGeometry innerGeo = new RectangleGeometry(innerRect, rInner, rInner);

            return new CombinedGeometry(GeometryCombineMode.Exclude, outerGeo, innerGeo);
        }

        private void DrawChromiumDashedBorder(DrawingContext dc, Pen pen, Rect rect, double t)
        {
            double cornerArm = 3.0 * t;

            double left = rect.Left;
            double top = rect.Top;
            double right = rect.Right;
            double bottom = rect.Bottom;

            // Draw 4 Closed Symmetrical L-Shaped Corner Caps
            dc.DrawLine(pen, new Point(left, top), new Point(left + cornerArm, top));
            dc.DrawLine(pen, new Point(left, top), new Point(left, top + cornerArm));

            dc.DrawLine(pen, new Point(right - cornerArm, top), new Point(right, top));
            dc.DrawLine(pen, new Point(right, top), new Point(right, top + cornerArm));

            dc.DrawLine(pen, new Point(right - cornerArm, bottom), new Point(right, bottom));
            dc.DrawLine(pen, new Point(right, bottom - cornerArm), new Point(right, bottom));

            dc.DrawLine(pen, new Point(left, bottom), new Point(left + cornerArm, bottom));
            dc.DrawLine(pen, new Point(left, bottom - cornerArm), new Point(left, bottom));

            // Draw Evenly Spaced Dashes along Top & Bottom Sides
            double horizLength = rect.Width - 2 * cornerArm;
            if (horizLength > 0)
            {
                DrawChromiumEdgeDashes(dc, pen, new Point(left + cornerArm, top), new Point(right - cornerArm, top), horizLength, t);
                DrawChromiumEdgeDashes(dc, pen, new Point(left + cornerArm, bottom), new Point(right - cornerArm, bottom), horizLength, t);
            }

            // Draw Evenly Spaced Dashes along Left & Right Sides
            double vertLength = rect.Height - 2 * cornerArm;
            if (vertLength > 0)
            {
                DrawChromiumEdgeDashes(dc, pen, new Point(right, top + cornerArm), new Point(right, bottom - cornerArm), vertLength, t);
                DrawChromiumEdgeDashes(dc, pen, new Point(left, top + cornerArm), new Point(left, bottom - cornerArm), vertLength, t);
            }
        }

        private void DrawChromiumEdgeDashes(DrawingContext dc, Pen pen, Point p1, Point p2, double length, double t)
        {
            double idealDash = 3.0 * t;
            double idealGap = 1.5 * t;
            double cycle = idealDash + idealGap;

            int numDashes = Math.Max(1, (int)Math.Round(length / cycle));
            
            double totalDashLength = numDashes * idealDash;
            double actualGap = (length - totalDashLength) / (numDashes + 1);

            if (actualGap < t * 0.4)
            {
                double actualCycle = length / (numDashes + 0.5);
                idealDash = actualCycle * (3.0 / 4.5);
                actualGap = actualCycle * (1.5 / 4.5);
            }

            Vector dir = (p2 - p1);
            dir.Normalize();

            Point curr = p1 + dir * actualGap;
            for (int i = 0; i < numDashes; i++)
            {
                Point next = curr + dir * idealDash;
                dc.DrawLine(pen, curr, next);
                curr = next + dir * (idealDash + actualGap);
            }
        }

        private void DrawChromiumDottedBorder(DrawingContext dc, Brush brush, Rect rect, double t)
        {
            double radius = t / 2.0;

            dc.DrawEllipse(brush, null, rect.TopLeft, radius, radius);
            dc.DrawEllipse(brush, null, rect.TopRight, radius, radius);
            dc.DrawEllipse(brush, null, rect.BottomRight, radius, radius);
            dc.DrawEllipse(brush, null, rect.BottomLeft, radius, radius);

            DrawEvenDots(dc, brush, new Point(rect.Left + t, rect.Top), new Point(rect.Right - t, rect.Top), rect.Width - 2 * t, radius, t);
            DrawEvenDots(dc, brush, new Point(rect.Left + t, rect.Bottom), new Point(rect.Right - t, rect.Bottom), rect.Width - 2 * t, radius, t);
            DrawEvenDots(dc, brush, new Point(rect.Left, rect.Top + t), new Point(rect.Left, rect.Bottom - t), rect.Height - 2 * t, radius, t);
            DrawEvenDots(dc, brush, new Point(rect.Right, rect.Top + t), new Point(rect.Right, rect.Bottom - t), rect.Height - 2 * t, radius, t);
        }

        private void DrawEvenDots(DrawingContext dc, Brush brush, Point p1, Point p2, double length, double dotRadius, double t)
        {
            if (length <= 0) return;

            double idealGap = 2.0 * t;
            int count = Math.Max(1, (int)Math.Round(length / idealGap));
            double actualGap = length / (count + 1);

            Vector dir = (p2 - p1);
            dir.Normalize();

            for (int i = 1; i <= count; i++)
            {
                Point dotPos = p1 + dir * (i * actualGap);
                dc.DrawEllipse(brush, null, dotPos, dotRadius, dotRadius);
            }
        }
    }
}
