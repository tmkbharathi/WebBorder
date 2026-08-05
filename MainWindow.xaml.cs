using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace WpfWebView2Poc
{
    public class UserSettings
    {
        public string TextValue { get; set; } = "";
        public string BorderStyle { get; set; } = "solid";
        public double Thickness { get; set; } = 11;
        public double CornerRadius { get; set; } = 0;
        public double Width { get; set; } = 450;
        public double Height { get; set; } = 150;
        public string BorderColor { get; set; } = "#3B82F6";
        public double ShadowR { get; set; } = 0;
        public double ShadowG { get; set; } = 0;
        public double ShadowB { get; set; } = 0;
        public double ShadowOpacity { get; set; } = 25;
        public double ShadowDistance { get; set; } = 8;
        public double ShadowAngle { get; set; } = 90;
        public double ShadowBlur { get; set; } = 12;
        public double ShadowSize { get; set; } = 0;
    }

    public partial class MainWindow : Window
    {
        private bool _isUpdating = false;
        private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_settings.json");

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            SyncParametersToBothViews();
            InitializeWebViewAsync();
        }

        private void LoadSettings()
        {
            if (!File.Exists(SettingsFilePath)) return;

            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<UserSettings>(json);
                if (settings == null) return;

                _isUpdating = true;

                if (txtGlobalText != null) txtGlobalText.Text = settings.TextValue ?? "";
                if (wpfDynamicTextBox != null) wpfDynamicTextBox.Text = settings.TextValue ?? "";

                if (cmbBorderStyle != null)
                {
                    foreach (ComboBoxItem item in cmbBorderStyle.Items)
                    {
                        if (item.Content?.ToString() == settings.BorderStyle)
                        {
                            cmbBorderStyle.SelectedItem = item;
                            break;
                        }
                    }
                }

                if (sldThickness != null) sldThickness.Value = Math.Clamp(settings.Thickness, sldThickness.Minimum, sldThickness.Maximum);
                if (sldRadius != null) sldRadius.Value = Math.Clamp(settings.CornerRadius, sldRadius.Minimum, sldRadius.Maximum);

                if (sldWidth != null) sldWidth.Value = Math.Clamp(settings.Width, sldWidth.Minimum, sldWidth.Maximum);
                if (txtWidth != null && sldWidth != null) txtWidth.Text = Math.Round(sldWidth.Value).ToString();

                if (sldHeight != null) sldHeight.Value = Math.Clamp(settings.Height, sldHeight.Minimum, sldHeight.Maximum);
                if (txtHeight != null && sldHeight != null) txtHeight.Text = Math.Round(sldHeight.Value).ToString();

                if (cmbBorderColor != null)
                {
                    foreach (ComboBoxItem item in cmbBorderColor.Items)
                    {
                        if (item.Tag?.ToString() == settings.BorderColor)
                        {
                            cmbBorderColor.SelectedItem = item;
                            break;
                        }
                    }
                }

                if (sldShadowR != null) sldShadowR.Value = Math.Clamp(settings.ShadowR, sldShadowR.Minimum, sldShadowR.Maximum);
                if (sldShadowG != null) sldShadowG.Value = Math.Clamp(settings.ShadowG, sldShadowG.Minimum, sldShadowG.Maximum);
                if (sldShadowB != null) sldShadowB.Value = Math.Clamp(settings.ShadowB, sldShadowB.Minimum, sldShadowB.Maximum);
                if (sldShadowOpacity != null) sldShadowOpacity.Value = Math.Clamp(settings.ShadowOpacity, sldShadowOpacity.Minimum, sldShadowOpacity.Maximum);
                if (sldShadowDistance != null) sldShadowDistance.Value = Math.Clamp(settings.ShadowDistance, sldShadowDistance.Minimum, sldShadowDistance.Maximum);
                if (sldShadowAngle != null) sldShadowAngle.Value = Math.Clamp(settings.ShadowAngle, sldShadowAngle.Minimum, sldShadowAngle.Maximum);
                if (sldShadowBlur != null) sldShadowBlur.Value = Math.Clamp(settings.ShadowBlur, sldShadowBlur.Minimum, sldShadowBlur.Maximum);
                if (sldShadowSize != null) sldShadowSize.Value = Math.Clamp(settings.ShadowSize, sldShadowSize.Minimum, sldShadowSize.Maximum);

                _isUpdating = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load user settings: {ex.Message}");
                _isUpdating = false;
            }
        }

        private void SaveSettings()
        {
            if (_isUpdating) return;

            try
            {
                var settings = new UserSettings
                {
                    TextValue = txtGlobalText?.Text ?? "",
                    BorderStyle = (cmbBorderStyle?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "solid",
                    Thickness = sldThickness?.Value ?? 11,
                    CornerRadius = sldRadius?.Value ?? 0,
                    Width = sldWidth?.Value ?? 450,
                    Height = sldHeight?.Value ?? 150,
                    BorderColor = (cmbBorderColor?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "#3B82F6",
                    ShadowR = sldShadowR?.Value ?? 0,
                    ShadowG = sldShadowG?.Value ?? 0,
                    ShadowB = sldShadowB?.Value ?? 0,
                    ShadowOpacity = sldShadowOpacity?.Value ?? 25,
                    ShadowDistance = sldShadowDistance?.Value ?? 8,
                    ShadowAngle = sldShadowAngle?.Value ?? 90,
                    ShadowBlur = sldShadowBlur?.Value ?? 12,
                    ShadowSize = sldShadowSize?.Value ?? 0
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save user settings: {ex.Message}");
            }
        }

        private async void InitializeWebViewAsync()
        {
            // Force WebView2/Chromium to render in standard sRGB color space, matching SkiaSharp & WPF colors 100%
            var options = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions("--force-color-profile=srgb");
            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, null, options);
            await webView.EnsureCoreWebView2Async(environment);

            // Subscribe to Web -> WPF messages
            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            // Load local HTML file
            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "index.html");
            if (File.Exists(htmlPath))
            {
                webView.Source = new Uri(htmlPath);
            }

            // Sync initial parameter values once web view is ready
            webView.NavigationCompleted += async (s, e) =>
            {
                SyncParametersToBothViews();

                if (txtGlobalText != null && !string.IsNullOrEmpty(txtGlobalText.Text))
                {
                    string safeText = JsonSerializer.Serialize(txtGlobalText.Text);
                    string script = $@"
                        if (window.chrome && window.chrome.webview) {{
                            const input = document.getElementById('dynamicInput');
                            if (input) {{ input.value = {safeText}; }}
                        }}
                    ";
                    await webView.ExecuteScriptAsync(script);
                }
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
            SaveSettings();
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
            byte sRed = (byte)(sldShadowR?.Value ?? 0);
            byte sGreen = (byte)(sldShadowG?.Value ?? 0);
            byte sBlue = (byte)(sldShadowB?.Value ?? 0);
            double opacityPercent = sldShadowOpacity?.Value ?? 25;
            byte sAlpha = (byte)Math.Round(255.0 * (opacityPercent / 100.0));

            Color sColor = Color.FromArgb(sAlpha, sRed, sGreen, sBlue);
            string shadowColorHex = $"#{sAlpha:X2}{sRed:X2}{sGreen:X2}{sBlue:X2}";

            double shadowDistance = sldShadowDistance?.Value ?? 0;
            double shadowAngle = sldShadowAngle?.Value ?? 0;
            double shadowBlur = sldShadowBlur?.Value ?? 0;
            double shadowSize = sldShadowSize?.Value ?? 0;

            // Convert Distance + Angle (degrees) to X, Y offsets
            double rad = shadowAngle * Math.PI / 180.0;
            double offsetX = Math.Round(shadowDistance * Math.Cos(rad));
            double offsetY = Math.Round(shadowDistance * Math.Sin(rad));

            double alphaFraction = Math.Round(sAlpha / 255.0, 2);
            string cssColor = $"rgba({sRed}, {sGreen}, {sBlue}, {alphaFraction.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
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

            SaveSettings();
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
                    SaveSettings();
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
            SaveSettings();
        }
    }
}