using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace WpfWebView2Poc
{
    public partial class MainWindow : Window
    {
        private bool _isUpdating = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeWebViewAsync();
        }

        private async void InitializeWebViewAsync()
        {
            // Initialize WebView2 environment
            await webView.EnsureCoreWebView2Async(null);

            // Subscribe to Web -> WPF messages
            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            // Load local HTML file
            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "index.html");
            if (File.Exists(htmlPath))
            {
                webView.Source = new Uri(htmlPath);
            }

            // Sync initial parameter values once web view is ready
            webView.NavigationCompleted += (s, e) =>
            {
                SyncParametersToBothViews();
            };
        }

        private void OnParameterChanged(object sender, SelectionChangedEventArgs e)
        {
            SyncParametersToBothViews();
        }

        private void OnParameterChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SyncParametersToBothViews();
        }

        private void OnSliderWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating || txtWidth == null) return;
            _isUpdating = true;
            txtWidth.Text = Math.Round(e.NewValue).ToString();
            _isUpdating = false;

            SyncParametersToBothViews();
        }

        private void OnSliderHeightChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating || txtHeight == null) return;
            _isUpdating = true;
            txtHeight.Text = Math.Round(e.NewValue).ToString();
            _isUpdating = false;

            SyncParametersToBothViews();
        }

        private void OnNumericInputChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating || sldWidth == null || sldHeight == null) return;

            _isUpdating = true;
            if (double.TryParse(txtWidth?.Text, out double wVal))
            {
                sldWidth.Value = Math.Clamp(wVal, sldWidth.Minimum, sldWidth.Maximum);
            }

            if (double.TryParse(txtHeight?.Text, out double hVal))
            {
                sldHeight.Value = Math.Clamp(hVal, sldHeight.Minimum, sldHeight.Maximum);
            }
            _isUpdating = false;

            SyncParametersToBothViews();
        }

        /// <summary>
        /// Handles Top Toolbar Text Input change (Top Toolbar -> WPF Native + Web View)
        /// </summary>
        private async void OnGlobalTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating || txtGlobalText == null) return;

            _isUpdating = true;
            string newText = txtGlobalText.Text;

            // Update WPF Native TextBox
            if (wpfDynamicTextBox != null && wpfDynamicTextBox.Text != newText)
            {
                wpfDynamicTextBox.Text = newText;
            }

            // Update Web View (HTML Textarea)
            if (webView != null && webView.CoreWebView2 != null)
            {
                string safeText = JsonSerializer.Serialize(newText);
                string script = $@"
                    if (window.chrome && window.chrome.webview) {{
                        const input = document.getElementById('dynamicInput');
                        if (input && input.value !== {safeText}) {{
                            input.value = {safeText};
                        }}
                    }}
                ";
                await webView.ExecuteScriptAsync(script);
            }

            _isUpdating = false;
        }

        /// <summary>
        /// Synchronizes style parameters (thickness, width, height, style, color, radius) to both WPF and Web views
        /// </summary>
        private async void SyncParametersToBothViews()
        {
            if (cmbBorderStyle == null || sldThickness == null || sldWidth == null || sldHeight == null || cmbBorderColor == null || sldRadius == null)
                return;

            // 1. Read Parameter Values
            string borderStyle = (cmbBorderStyle.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "solid";
            double thickness = sldThickness.Value;
            double width = sldWidth.Value;
            double height = sldHeight.Value;
            double radius = sldRadius.Value;

            string colorHex = "#3B82F6";
            if (cmbBorderColor.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                colorHex = item.Tag.ToString()!;
            }

            Brush brush = (Brush)new BrushConverter().ConvertFromString(colorHex)!;

            // Read Box Shadow Parameters (Size, Distance, Color, Blur, Angle)
            string shadowColorHex = "#40000000";
            if (cmbShadowColor != null && cmbShadowColor.SelectedItem is ComboBoxItem shadowItem && shadowItem.Tag != null)
            {
                shadowColorHex = shadowItem.Tag.ToString()!;
            }

            double shadowDistance = sldShadowDistance?.Value ?? 0;
            double shadowAngle = sldShadowAngle?.Value ?? 0;
            double shadowBlur = sldShadowBlur?.Value ?? 0;
            double shadowSize = sldShadowSize?.Value ?? 0;

            // Convert Distance + Angle (degrees) to X, Y offsets
            double rad = shadowAngle * Math.PI / 180.0;
            double offsetX = Math.Round(shadowDistance * Math.Cos(rad));
            double offsetY = Math.Round(shadowDistance * Math.Sin(rad));

            Color sColor = (Color)ColorConverter.ConvertFromString(shadowColorHex);
            double alphaFraction = Math.Round(sColor.A / 255.0, 2);
            string cssColor = $"rgba({sColor.R}, {sColor.G}, {sColor.B}, {alphaFraction.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            string boxShadowCss = $"{offsetX:0}px {offsetY:0}px {shadowBlur:0}px {shadowSize:0}px {cssColor}";

            // 2. Update Native WPF Controls Container
            if (wpfHostGrid != null)
            {
                wpfHostGrid.Width = width;
                wpfHostGrid.Height = height;
            }

            // Adjust TextBox margin so typed text sits cleanly inside the border
            if (wpfDynamicTextBox != null)
            {
                wpfDynamicTextBox.Margin = new Thickness(thickness);
            }

            // 3. Update Chromium Border Canvas & Chromium Shadow Canvas
            if (chromiumShadowCanvas != null)
            {
                chromiumShadowCanvas.CornerRadius = radius;
                chromiumShadowCanvas.ShadowColor = sColor;
                chromiumShadowCanvas.ShadowDistance = shadowDistance;
                chromiumShadowCanvas.ShadowAngle = shadowAngle;
                chromiumShadowCanvas.ShadowBlur = shadowBlur;
                chromiumShadowCanvas.ShadowSize = shadowSize;
            }

            if (chromiumBorderCanvas != null)
            {
                chromiumBorderCanvas.BorderStyle = borderStyle;
                chromiumBorderCanvas.StrokeThickness = thickness;
                chromiumBorderCanvas.Stroke = brush;
                chromiumBorderCanvas.CornerRadius = radius;
            }

            // Update Specs Panel Text
            if (txtWpfSpecStyle != null) txtWpfSpecStyle.Text = borderStyle;
            if (txtWpfSpecThickness != null) txtWpfSpecThickness.Text = $"{thickness:0}px";
            if (txtWpfSpecWidth != null) txtWpfSpecWidth.Text = $"{width:0}px";
            if (txtWpfSpecHeight != null) txtWpfSpecHeight.Text = $"{height:0}px";
            if (txtWpfSpecColor != null) txtWpfSpecColor.Text = colorHex;
            if (txtWpfSpecRadius != null) txtWpfSpecRadius.Text = $"{radius:0}px";
            if (txtWpfSpecShadow != null) txtWpfSpecShadow.Text = boxShadowCss;

            // 4. Send parameters to Web View (HTML/CSS)
            if (webView != null && webView.CoreWebView2 != null)
            {
                string safeShadow = JsonSerializer.Serialize(boxShadowCss);
                string script = $"updateStyleProperties('{borderStyle}', {thickness}, {width}, {height}, '{colorHex}', {radius}, {safeShadow});";
                await webView.ExecuteScriptAsync(script);
            }
        }

        /// <summary>
        /// Handles incoming messages from HTML JavaScript (Web -> WPF & Top Textbox Sync)
        /// </summary>
        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string jsonString = e.WebMessageAsJson;
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "textChange")
                {
                    string value = root.GetProperty("text").GetString() ?? "";

                    _isUpdating = true;
                    if (wpfDynamicTextBox != null && wpfDynamicTextBox.Text != value)
                    {
                        wpfDynamicTextBox.Text = value;
                    }
                    if (txtGlobalText != null && txtGlobalText.Text != value)
                    {
                        txtGlobalText.Text = value;
                    }
                    _isUpdating = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing web message: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles WPF TextBox TextChanged event (WPF -> Web & Top Textbox Sync)
        /// </summary>
        private async void WpfInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating || webView.CoreWebView2 == null) return;

            _isUpdating = true;
            string currentText = wpfDynamicTextBox.Text;

            if (txtGlobalText != null && txtGlobalText.Text != currentText)
            {
                txtGlobalText.Text = currentText;
            }

            string safeText = JsonSerializer.Serialize(currentText);
            string script = $@"
                if (window.chrome && window.chrome.webview) {{
                    const input = document.getElementById('dynamicInput');
                    if (input && input.value !== {safeText}) {{
                        input.value = {safeText};
                    }}
                }}
            ";

            await webView.ExecuteScriptAsync(script);
            _isUpdating = false;
        }
    }
}