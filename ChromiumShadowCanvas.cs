using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WpfWebView2Poc
{
    /// <summary>
    /// Custom WPF Control implementing Chromium Blink's exact C++ Box Shadow rendering engine.
    /// Replicates BoxPainterBase::PaintNormalBoxShadow:
    /// 1. Outer Spread Expansion (-spread, -spread, w + 2*spread, h + 2*spread)
    /// 2. Offset Translation by (offsetX, offsetY)
    /// 3. Difference Clip / Exclusion of inner Box Rect (0, 0, w, h)
    /// 4. Direct Fill with Shadow Color & Skia-equivalent GPU Gaussian BlurEffect
    /// </summary>
    public class ChromiumShadowCanvas : FrameworkElement
    {
        public ChromiumShadowCanvas()
        {
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
        }

        public static readonly DependencyProperty ShadowDistanceProperty =
            DependencyProperty.Register(nameof(ShadowDistance), typeof(double), typeof(ChromiumShadowCanvas),
                new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender, OnShadowPropertyChanged));

        public static readonly DependencyProperty ShadowAngleProperty =
            DependencyProperty.Register(nameof(ShadowAngle), typeof(double), typeof(ChromiumShadowCanvas),
                new FrameworkPropertyMetadata(90.0, FrameworkPropertyMetadataOptions.AffectsRender, OnShadowPropertyChanged));

        public static readonly DependencyProperty ShadowBlurProperty =
            DependencyProperty.Register(nameof(ShadowBlur), typeof(double), typeof(ChromiumShadowCanvas),
                new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender, OnShadowPropertyChanged));

        public static readonly DependencyProperty ShadowSizeProperty =
            DependencyProperty.Register(nameof(ShadowSize), typeof(double), typeof(ChromiumShadowCanvas),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnShadowPropertyChanged));

        public static readonly DependencyProperty ShadowColorProperty =
            DependencyProperty.Register(nameof(ShadowColor), typeof(Color), typeof(ChromiumShadowCanvas),
                new FrameworkPropertyMetadata(Color.FromArgb(0x40, 0x00, 0x00, 0x00), FrameworkPropertyMetadataOptions.AffectsRender, OnShadowPropertyChanged));

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(ChromiumShadowCanvas),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double ShadowDistance
        {
            get => (double)GetValue(ShadowDistanceProperty);
            set => SetValue(ShadowDistanceProperty, value);
        }

        public double ShadowAngle
        {
            get => (double)GetValue(ShadowAngleProperty);
            set => SetValue(ShadowAngleProperty, value);
        }

        public double ShadowBlur
        {
            get => (double)GetValue(ShadowBlurProperty);
            set => SetValue(ShadowBlurProperty, value);
        }

        public double ShadowSize
        {
            get => (double)GetValue(ShadowSizeProperty);
            set => SetValue(ShadowSizeProperty, value);
        }

        public Color ShadowColor
        {
            get => (Color)GetValue(ShadowColorProperty);
            set => SetValue(ShadowColorProperty, value);
        }

        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        private static void OnShadowPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ChromiumShadowCanvas canvas)
            {
                canvas.UpdateShadowEffect();
            }
        }

        public void UpdateShadowEffect()
        {
            if (ShadowColor.A > 0 && ShadowBlur > 0)
            {
                // Use WPF BlurEffect with Gaussian kernel type to match Chromium Skia's GPU Gaussian blur filter
                BlurEffect blur = new BlurEffect
                {
                    Radius = ShadowBlur,
                    KernelType = KernelType.Gaussian
                };
                this.Effect = blur;
            }
            else
            {
                this.Effect = null;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            double r = CornerRadius;

            if (w <= 0 || h <= 0 || ShadowColor.A == 0) return;

            // Chromium Blink C++ Box Shadow Algorithm Implementation
            double s = ShadowSize;
            double dist = ShadowDistance;
            double angleRad = ShadowAngle * Math.PI / 180.0;

            double offsetX = Math.Round(dist * Math.Cos(angleRad));
            double offsetY = Math.Round(dist * Math.Sin(angleRad));

            double clampedR = Math.Min(r, Math.Min(w / 2.0, h / 2.0));
            // Per W3C CSS Spec: If border-radius is 0, spread maintains sharp 90-degree corners
            double shadowR = (clampedR > 0) ? Math.Max(0, clampedR + s) : 0;

            // 1. Outer Spread Expansion translated by (offsetX, offsetY)
            Rect outerSpreadRect = new Rect(offsetX - s, offsetY - s, Math.Max(0, w + s * 2.0), Math.Max(0, h + s * 2.0));
            RectangleGeometry outerSpreadGeo = new RectangleGeometry(outerSpreadRect, shadowR, shadowR);

            // 2. Inner Box Geometry (0, 0, w, h) - clipped out per W3C CSS spec
            Rect innerBoxRect = new Rect(0, 0, w, h);
            RectangleGeometry innerBoxGeo = new RectangleGeometry(innerBoxRect, clampedR, clampedR);

            // 3. Exclude inner box from spread shadow shape
            CombinedGeometry shadowGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, outerSpreadGeo, innerBoxGeo);

            // 4. Draw shadow shape filled directly with ShadowColor
            Brush shadowBrush = new SolidColorBrush(ShadowColor);
            dc.DrawGeometry(shadowBrush, null, shadowGeometry);
        }
    }
}
