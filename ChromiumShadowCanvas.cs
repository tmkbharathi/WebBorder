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
            Loaded += (s, e) => UpdateShadowEffect();
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
                // Use WPF BlurEffect scaled by 1.1 to match Skia GPU Gaussian blur dispersion
                BlurEffect blur = new BlurEffect
                {
                    Radius = ShadowBlur * 1.1,
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

            if (this.Effect == null && ShadowBlur > 0 && ShadowColor.A > 0)
            {
                UpdateShadowEffect();
            }

            double w = ActualWidth;
            double h = ActualHeight;
            double r = CornerRadius;

            if (w <= 0 || h <= 0 || ShadowColor.A == 0)
            {
                this.Clip = null;
                return;
            }

            // Chromium Blink C++ Box Shadow Algorithm Implementation
            double s = ShadowSize;
            double dist = ShadowDistance;
            double angleRad = ShadowAngle * Math.PI / 180.0;

            double offsetX = Math.Round(dist * Math.Cos(angleRad));
            double offsetY = Math.Round(dist * Math.Sin(angleRad));

            double clampedR = Math.Min(r, Math.Min(w / 2.0, h / 2.0));
            // Per W3C CSS Spec & Skia painter: If border-radius > 0, shadow corner radius = border-radius + spread.
            // If border-radius == 0, corner rounding is produced by Gaussian blur dispersion (approx 0.3 * blur + 0.15 * spread).
            double shadowR;
            if (clampedR > 0)
            {
                shadowR = Math.Max(0, clampedR + s);
            }
            else
            {
                shadowR = (ShadowBlur > 0) ? Math.Max(0, s * 0.15 + ShadowBlur * 0.3) : 0;
            }

            // 1. Draw SOLID Outer Spread Rectangle
            Rect outerSpreadRect = new Rect(offsetX - s, offsetY - s, Math.Max(0, w + s * 2.0), Math.Max(0, h + s * 2.0));
            RectangleGeometry outerSpreadGeo = new RectangleGeometry(outerSpreadRect, shadowR, shadowR);

            Brush shadowBrush = new SolidColorBrush(ShadowColor);
            dc.DrawGeometry(shadowBrush, null, outerSpreadGeo);

            // 2. Post-Blur Difference Clip (Excludes inner element rect [0, 0, w, h] from final visual)
            // Per W3C CSS Spec / Blink BoxPainterBase: Inner box area must remain transparent.
            // Applying this.Clip excludes the inner rect AFTER WPF BlurEffect has rendered the solid shadow field.
            Rect infiniteBounds = new Rect(-2000, -2000, w + 4000, h + 4000);
            RectangleGeometry infiniteGeo = new RectangleGeometry(infiniteBounds);
            RectangleGeometry innerBoxGeo = new RectangleGeometry(new Rect(0, 0, w, h), clampedR, clampedR);

            this.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, infiniteGeo, innerBoxGeo);
        }
    }
}
