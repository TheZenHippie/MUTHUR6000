using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using MUTHUR6000.Settings;
using MUTHUR6000.Terminal;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace MUTHUR6000
{
    public partial class MainWindow : Window
    {
        private readonly WidgetSettings _settings;
        private readonly TerminalReaderEngine _engine;

        // Visual Styling State
        private FontFamily _currentFontFamily;
        private double _currentFontSize = 13.0;

        private Color _currentPhosphorColor = Color.FromRgb(0, 255, 102); // Matrix / Nostromo Green
        private SolidColorBrush _currentPhosphorBrush;

        private Color _currentBackgroundColor = Color.FromRgb(8, 20, 11); // Tinted CRT Glass Dark Green
        private SolidColorBrush _currentBackgroundBrush;

        private double _backgroundOpacity = 0.85;
        private double _fontOpacity = 1.0;

        // Effects State
        private bool _bloomEnabled = true;
        private double _bloomIntensity = 8.0;

        private bool _crtScanlinesEnabled = false;
        private double _scanlineThickness = 3.0;

        private bool _crtGlitchEnabled = true;
        private double _glitchChance = 15.0;
        private readonly DispatcherTimer _glitchTimer;
        private readonly DispatcherTimer _beamResetTimer;
        private Brush[] _glitchBrushes = Array.Empty<Brush>();

        private bool _crtSnowEnabled = false;
        private double _snowAmount = 25.0;
        private readonly DispatcherTimer _snowTimer;
        private readonly List<WriteableBitmap> _snowBitmaps = new List<WriteableBitmap>();
        private int _snowFrameIndex = 0;

        // Text Flow & Blinking Cursor State
        private Paragraph _activeParagraph;
        private Run _textRun;
        private Run _cursorRun;
        private bool _cursorVisible = true;
        private readonly DispatcherTimer _cursorTimer;

        // UI Helpers
        private readonly DispatcherTimer _effectsCloseTimer;
        private readonly DispatcherTimer _saveDebounceTimer;
        private FrameworkElement? _subscribedPopupChild = null;

        private bool _isClosed = false;
        private bool _isInitialized = false;

        public MainWindow()
        {
            App.EnsureStandardMenuDropAlignment();

            _settings = WidgetSettings.Load();

            InitializeComponent();

            // Setup document and cursor runs
            _activeParagraph = new Paragraph();
            _textRun = new Run(string.Empty);
            _cursorRun = new Run("█");

            _activeParagraph.Inlines.Add(_textRun);
            _activeParagraph.Inlines.Add(_cursorRun);
            TerminalFlowDocument.Blocks.Add(_activeParagraph);

            // Restore font & color from settings
            try
            {
                _currentFontFamily = new FontFamily(_settings.FontFamily);
            }
            catch
            {
                _currentFontFamily = new FontFamily("Cascadia Code, Consolas, Courier New");
            }

            _currentFontSize = _settings.FontSize > 6 ? _settings.FontSize : 13.0;

            try
            {
                _currentPhosphorColor = (Color)ColorConverter.ConvertFromString(_settings.PhosphorColorHex);
            }
            catch
            {
                _currentPhosphorColor = Color.FromRgb(0, 255, 102);
            }

            try
            {
                _currentBackgroundColor = (Color)ColorConverter.ConvertFromString(_settings.BackgroundColorHex);
            }
            catch
            {
                _currentBackgroundColor = Color.FromRgb(8, 20, 11);
            }

            _currentPhosphorBrush = new SolidColorBrush(_currentPhosphorColor);
            _currentPhosphorBrush.Freeze();

            _currentBackgroundBrush = new SolidColorBrush(_currentBackgroundColor);
            _currentBackgroundBrush.Freeze();

            _backgroundOpacity = Math.Clamp(_settings.BackgroundOpacity, 0.0, 1.0);
            _fontOpacity = Math.Clamp(_settings.FontOpacity, 0.1, 1.0);

            _bloomEnabled = _settings.BloomEnabled;
            _bloomIntensity = Math.Clamp(_settings.BloomIntensity, 0.0, 25.0);

            _crtScanlinesEnabled = _settings.CrtScanlinesEnabled;
            _scanlineThickness = Math.Clamp(_settings.ScanlineThickness, 1.0, 20.0);

            _crtGlitchEnabled = _settings.CrtGlitchEnabled;
            _glitchChance = Math.Clamp(_settings.GlitchChance, 0.0, 100.0);

            _crtSnowEnabled = _settings.CrtSnowEnabled;
            _snowAmount = Math.Clamp(_settings.SnowAmount, 0.0, 100.0);

            // Generate monochrome glitch brushes matching active phosphor & background
            RebuildGlitchBrushes();

            // Set up Cursor blinking timer (500ms rate)
            _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _cursorTimer.Tick += CursorTimer_Tick;
            _cursorTimer.Start();

            // Set up CRT glitch timer (120ms tick rate)
            _glitchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _glitchTimer.Tick += GlitchTimer_Tick;

            // Set up Right-to-Left Beam glitch reset timer (110ms hold)
            _beamResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
            _beamResetTimer.Tick += BeamResetTimer_Tick;

            // Set up CRT snow static animation timer (65ms tick rate)
            _snowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(65) };
            _snowTimer.Tick += SnowTimer_Tick;

            // Set up Effects submenu close delay timer (700ms)
            _effectsCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _effectsCloseTimer.Tick += EffectsCloseTimer_Tick;

            // Set up settings save debounce timer (400ms)
            _saveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveDebounceTimer.Tick += (s, e) =>
            {
                _saveDebounceTimer.Stop();
                _settings?.Save();
            };

            // Initialize Terminal Reader Engine
            _engine = new TerminalReaderEngine(_settings.BaudRate, _settings.HoldDelaySeconds);
            _engine.FileStarted += OnEngineFileStarted;
            _engine.CharactersEmitted += OnEngineCharactersEmitted;
            _engine.FileCompleted += OnEngineFileCompleted;
            _engine.ScreenClearRequested += OnEngineScreenClearRequested;
            _engine.StatusChanged += OnEngineStatusChanged;
            _engine.PauseStateChanged += OnEnginePauseStateChanged;

            // Initialize UI elements
            ApplyStyling();
            PopulateFontFamilies();
            UpdateScanlinesBrush(_scanlineThickness);

            // Restore window size
            if (_settings.WindowWidth.HasValue && _settings.WindowWidth.Value >= MinWidth)
            {
                Width = _settings.WindowWidth.Value;
            }
            if (_settings.WindowHeight.HasValue && _settings.WindowHeight.Value >= MinHeight)
            {
                Height = _settings.WindowHeight.Value;
            }

            // Restore window position across multi-monitor setups
            if (_settings.WindowLeft.HasValue && _settings.WindowTop.HasValue)
            {
                double vLeft = SystemParameters.VirtualScreenLeft;
                double vTop = SystemParameters.VirtualScreenTop;
                double vWidth = SystemParameters.VirtualScreenWidth;
                double vHeight = SystemParameters.VirtualScreenHeight;

                double left = _settings.WindowLeft.Value;
                double top = _settings.WindowTop.Value;

                if (left >= vLeft - 20 && left < vLeft + vWidth - 100 &&
                    top >= vTop - 20 && top < vTop + vHeight - 100)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = left;
                    Top = top;
                }
            }

            // Restore window options
            Topmost = _settings.AlwaysOnTop;
            AlwaysOnTopMenuItem.IsChecked = _settings.AlwaysOnTop;
            ShadowMenuItem.IsChecked = _settings.WindowShadow;
            WindowDropShadow.Opacity = _settings.WindowShadow ? 0.35 : 0.0;

            // Apply restored sliders and toggles
            BackgroundOpacitySlider.Value = _backgroundOpacity;
            FontOpacitySlider.Value = _fontOpacity;
            BloomIntensitySlider.Value = _bloomIntensity;
            ScanlineThicknessSlider.Value = _scanlineThickness;
            GlitchChanceSlider.Value = _glitchChance;
            SnowAmountSlider.Value = _snowAmount;

            BloomGlowMenuItem.IsChecked = _bloomEnabled;
            CrtScanlinesMenuItem.IsChecked = _crtScanlinesEnabled;
            CrtGlitchMenuItem.IsChecked = _crtGlitchEnabled;
            CrtSnowMenuItem.IsChecked = _crtSnowEnabled;

            UpdateBaudRateMenuChecks(_settings.BaudRate);
            UpdatePhosphorColorMenuChecks(_settings.PhosphorColorHex);
            UpdateBackgroundColorMenuChecks(_settings.BackgroundColorHex);
            UpdateFontSizeMenuChecks();

            // Apply effects state
            ApplyEffectsState();

            // Register system display changes
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _isInitialized = true;

            Loaded += (s, e) =>
            {
                _engine.Start();
            };
        }

        #region Engine Callbacks

        private void OnEngineFileStarted(string fileName)
        {
            if (_isClosed) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (_isClosed) return;
                ActiveFileText.Text = $"FEED: {fileName}";
                HeaderStatusText.Text = "[ OVERMONITORING MATRIX ]";
                HeaderStatusText.Foreground = _currentPhosphorBrush;
                DeckStatusText.Text = $"DECK: {(_engine.TotalFilesCount > 0 ? "STREAMING" : "EMPTY")}";
                TransmissionStatusText.Text = "STATUS: TRANSMITTING";
            });
        }

        private void OnEngineCharactersEmitted(string chunk)
        {
            if (_isClosed || string.IsNullOrEmpty(chunk)) return;

            Dispatcher.InvokeAsync(() =>
            {
                if (_isClosed) return;

                _textRun.Text += chunk;

                // Cap buffer to prevent excessive memory usage while allowing ample scrollback
                if (_textRun.Text.Length > 40000)
                {
                    _textRun.Text = _textRun.Text.Substring(_textRun.Text.Length - 30000);
                }

                TerminalOutputBox.ScrollToEnd();
            }, DispatcherPriority.Render);
        }

        private void OnEngineFileCompleted(string fileName)
        {
            if (_isClosed) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (_isClosed) return;
                TransmissionStatusText.Text = $"STATUS: COMPLETE (HOLDING {_settings.HoldDelaySeconds:0.#}s)";
            });
        }

        private void OnEngineScreenClearRequested()
        {
            if (_isClosed) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (_isClosed) return;
                _textRun.Text = string.Empty;
                TransmissionStatusText.Text = "STATUS: BUFFER CLEARED";
                TerminalOutputBox.ScrollToHome();
            });
        }

        private void OnEngineStatusChanged(string statusMessage)
        {
            if (_isClosed) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (_isClosed) return;
                TransmissionStatusText.Text = $"STATUS: {statusMessage}";
            });
        }

        private void OnEnginePauseStateChanged(bool isPaused)
        {
            if (_isClosed) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (_isClosed) return;
                PauseButton.Content = isPaused ? "RESUME" : "PAUSE";
                PauseResumeMenuItem.Header = isPaused ? "Resume Transmission" : "Pause Transmission";
                TransmissionStatusText.Text = isPaused ? "STATUS: PAUSED" : "STATUS: TRANSMITTING";
            });
        }

        #endregion

        #region Cursor Animation

        private void CursorTimer_Tick(object? sender, EventArgs e)
        {
            if (_isClosed) return;
            _cursorVisible = !_cursorVisible;
            _cursorRun.Text = _cursorVisible ? "█" : " ";
            _cursorRun.Foreground = _currentPhosphorBrush;
        }

        #endregion

        #region Monochrome Glitch & Right-to-Left Beam Scan

        private void RebuildGlitchBrushes()
        {
            // Create monochrome glitch brushes matching phosphor color at various opacities + blackouts
            Color p = _currentPhosphorColor;
            Color b = _currentBackgroundColor;

            Color[] colors =
            {
                Color.FromArgb(255, p.R, p.G, p.B),                          // Full 100% phosphor
                Color.FromArgb(200, p.R, p.G, p.B),                          // 78% phosphor
                Color.FromArgb(140, p.R, p.G, p.B),                          // 55% phosphor
                Color.FromArgb(255, b.R, b.G, b.B),                          // Background dropouts
                Color.FromArgb(220, (byte)Math.Min(255, p.R + 40), (byte)Math.Min(255, p.G + 40), (byte)Math.Min(255, p.B + 40)), // Intense phosphor flare
                Color.FromArgb(240, 0, 0, 0)                                 // CRT beam dropout
            };

            _glitchBrushes = new Brush[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                var brush = new SolidColorBrush(colors[i]);
                brush.Freeze();
                _glitchBrushes[i] = brush;
            }
        }

        private void GlitchTimer_Tick(object? sender, EventArgs e)
        {
            if (!_crtGlitchEnabled || _isClosed) return;

            // Clear previous horizontal slice glitches (keep beam and highlight elements in canvas)
            var sliceRects = CrtGlitchCanvas.Children.OfType<Rectangle>().Where(r => r != GlitchBeamLine).ToList();
            foreach (var r in sliceRects)
            {
                CrtGlitchCanvas.Children.Remove(r);
            }

            if (Random.Shared.Next(100) < _glitchChance)
            {
                double w = CrtGlitchCanvas.ActualWidth;
                double h = CrtGlitchCanvas.ActualHeight;

                if (w > 30 && h > 30)
                {
                    int sliceCount = Random.Shared.Next(2, 5);
                    for (int i = 0; i < sliceCount; i++)
                    {
                        var rect = new Rectangle
                        {
                            Width = Random.Shared.Next(40, (int)w),
                            Height = Random.Shared.Next(2, 7),
                            Fill = _glitchBrushes[Random.Shared.Next(_glitchBrushes.Length)],
                            Opacity = Random.Shared.NextDouble() * 0.7 + 0.3
                        };

                        Canvas.SetLeft(rect, Random.Shared.Next(-10, (int)w - 20));
                        Canvas.SetTop(rect, Random.Shared.Next(0, (int)h - 8));
                        CrtGlitchCanvas.Children.Add(rect);
                    }

                    // Content micro jitter displacement
                    ContentJitterTransform.X = (Random.Shared.NextDouble() * 4.0) - 2.0;
                    ContentJitterTransform.Y = (Random.Shared.NextDouble() * 3.0) - 1.5;

                    // Trigger Right-to-Left Beam Scan with Reverse-Video Highlight
                    if (Random.Shared.Next(100) < 40)
                    {
                        TriggerRightToLeftBeamGlitch(w, h);
                    }
                }
            }
            else
            {
                ContentJitterTransform.X = 0;
                ContentJitterTransform.Y = 0;
            }
        }

        private void TriggerRightToLeftBeamGlitch(double canvasWidth, double canvasHeight)
        {
            if (canvasWidth <= 50 || canvasHeight <= 50) return;

            // Sample a character on screen or fallback
            char highlightChar = '█';
            string text = _textRun.Text;
            if (!string.IsNullOrEmpty(text))
            {
                // Find a non-whitespace character from recent visible lines
                int searchStart = Math.Max(0, text.Length - 1200);
                var candidates = text.Substring(searchStart)
                                     .Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c))
                                     .ToList();
                if (candidates.Count > 0)
                {
                    highlightChar = candidates[Random.Shared.Next(candidates.Count)];
                }
            }

            // Estimate character position on terminal grid
            double approxCharWidth = Math.Max(7.0, _currentFontSize * 0.62);
            double approxLineHeight = 18.0;

            int maxCols = Math.Max(1, (int)((canvasWidth - 40) / approxCharWidth));
            int maxRows = Math.Max(1, (int)((canvasHeight - 50) / approxLineHeight));

            int targetCol = Random.Shared.Next(2, maxCols);
            int targetRow = Random.Shared.Next(2, maxRows);

            double targetX = 14 + (targetCol * approxCharWidth);
            double targetY = 32 + (targetRow * approxLineHeight);

            // Configure Right-to-Left Beam Scan Line
            GlitchBeamLine.Fill = _currentPhosphorBrush;
            GlitchBeamLine.Height = 2.5;
            GlitchBeamLine.Opacity = 0.95;
            Canvas.SetTop(GlitchBeamLine, targetY + 7);
            Canvas.SetLeft(GlitchBeamLine, targetX);
            GlitchBeamLine.Width = Math.Max(10.0, canvasWidth - targetX);

            // Configure Reverse-Video Character Highlight
            GlitchReverseHighlightBox.Background = _currentPhosphorBrush;
            GlitchReverseHighlightText.Foreground = _currentBackgroundBrush;
            GlitchReverseHighlightText.FontFamily = _currentFontFamily;
            GlitchReverseHighlightText.FontSize = _currentFontSize;
            GlitchReverseHighlightText.Text = highlightChar.ToString();

            Canvas.SetLeft(GlitchReverseHighlightBox, targetX);
            Canvas.SetTop(GlitchReverseHighlightBox, targetY);

            GlitchReverseHighlightBox.Visibility = Visibility.Visible;
            GlitchReverseHighlightBox.Opacity = 1.0;

            _beamResetTimer.Stop();
            _beamResetTimer.Start();
        }

        private void BeamResetTimer_Tick(object? sender, EventArgs e)
        {
            _beamResetTimer.Stop();
            GlitchBeamLine.Opacity = 0.0;
            GlitchBeamLine.Width = 0;
            GlitchReverseHighlightBox.Visibility = Visibility.Collapsed;
            GlitchReverseHighlightBox.Opacity = 0.0;
        }

        #endregion

        #region CRT Snow Static Generation

        private List<WriteableBitmap> GetOrCreateSnowBitmaps()
        {
            if (_snowBitmaps.Count == 8) return _snowBitmaps;

            _snowBitmaps.Clear();
            int w = 256, h = 256;
            Color p = _currentPhosphorColor;

            for (int f = 0; f < 8; f++)
            {
                var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                int stride = w * 4;
                byte[] pixels = new byte[stride * h];

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte intensity = (byte)Random.Shared.Next(256);
                    bool phosphorSpeck = Random.Shared.Next(15) == 0;

                    if (phosphorSpeck)
                    {
                        pixels[i] = (byte)(p.B * intensity / 255); // B
                        pixels[i + 1] = (byte)(p.G * intensity / 255); // G
                        pixels[i + 2] = (byte)(p.R * intensity / 255); // R
                    }
                    else
                    {
                        pixels[i] = (byte)(intensity / 3);
                        pixels[i + 1] = (byte)(intensity / 3);
                        pixels[i + 2] = (byte)(intensity / 3);
                    }
                    pixels[i + 3] = 255;
                }

                wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                wb.Freeze();
                _snowBitmaps.Add(wb);
            }

            return _snowBitmaps;
        }

        private void SnowTimer_Tick(object? sender, EventArgs e)
        {
            if (!_crtSnowEnabled || _isClosed) return;

            var frames = GetOrCreateSnowBitmaps();
            _snowFrameIndex = (_snowFrameIndex + 1) % frames.Count;
            CrtSnowOverlay.Source = frames[_snowFrameIndex];
        }

        #endregion

        #region Visual Styling & Effects Management

        private void ApplyStyling()
        {
            // Window background glass border
            WindowBackgroundBorder.Background = new SolidColorBrush(_currentBackgroundColor);
            WindowBackgroundBorder.Opacity = _backgroundOpacity;
            WindowBackgroundBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(90, _currentPhosphorColor.R, _currentPhosphorColor.G, _currentPhosphorColor.B));

            // Terminal text output box styling
            TerminalOutputBox.FontFamily = _currentFontFamily;
            TerminalOutputBox.FontSize = _currentFontSize;
            TerminalOutputBox.Foreground = _currentPhosphorBrush;
            TerminalOutputBox.CaretBrush = _currentPhosphorBrush;

            // Content grid opacity
            TerminalContentGrid.Opacity = _fontOpacity;

            // Header and Badge elements
            SystemBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(120, _currentPhosphorColor.R, _currentPhosphorColor.G, _currentPhosphorColor.B));
            SystemBadgeText.Foreground = _currentPhosphorBrush;

            if (OvermonitoringBadgeBorder != null)
            {
                OvermonitoringBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(120, _currentPhosphorColor.R, _currentPhosphorColor.G, _currentPhosphorColor.B));
            }

            HeaderStatusText.Text = "[ OVERMONITORING MATRIX ]";
            HeaderStatusText.Foreground = _currentPhosphorBrush;

            BaudBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(100, _currentPhosphorColor.R, _currentPhosphorColor.G, _currentPhosphorColor.B));
            BaudBadgeText.Foreground = _currentPhosphorBrush;
            BaudBadgeText.Text = $"[ {_engine.BaudRate} BAUD ]";

            HeaderBarBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, _currentPhosphorColor.R, _currentPhosphorColor.G, _currentPhosphorColor.B));
            FooterBarBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, _currentPhosphorColor.R, _currentPhosphorColor.G, _currentPhosphorColor.B));

            ActiveFileText.Foreground = _currentPhosphorBrush;

            // Scrollbar thumb indicator tint
            var thumbColor = Color.FromArgb(110, _currentPhosphorColor.R, _currentPhosphorColor.G, _currentPhosphorColor.B);
            var thumbBrush = new SolidColorBrush(thumbColor);
            thumbBrush.Freeze();
            TerminalOutputBox.Resources["ScrollThumbBrush"] = thumbBrush;

            // Phosphor Bloom Glow Effect
            TextBloomEffect.Color = _currentPhosphorColor;
            TextBloomEffect.BlurRadius = _bloomEnabled ? _bloomIntensity : 0.0;
            TextBloomEffect.Opacity = (_bloomEnabled && _bloomIntensity > 0) ? 0.85 : 0.0;

            // Sliders status labels
            if (BackgroundOpacityValueText != null) BackgroundOpacityValueText.Text = $"{Math.Round(_backgroundOpacity * 100)}%";
            if (FontOpacityValueText != null) FontOpacityValueText.Text = $"{Math.Round(_fontOpacity * 100)}%";
            if (BloomIntensityValueText != null) BloomIntensityValueText.Text = $"{Math.Round(_bloomIntensity)}px";
            if (ScanlineThicknessValueText != null) ScanlineThicknessValueText.Text = $"{Math.Round(_scanlineThickness)}px";
            if (GlitchChanceValueText != null) GlitchChanceValueText.Text = $"{Math.Round(_glitchChance)}%";
            if (SnowAmountValueText != null) SnowAmountValueText.Text = $"{Math.Round(_snowAmount)}%";
        }

        private void ApplyEffectsState()
        {
            // Bloom
            TextBloomEffect.BlurRadius = _bloomEnabled ? _bloomIntensity : 0.0;
            TextBloomEffect.Opacity = (_bloomEnabled && _bloomIntensity > 0) ? 0.85 : 0.0;

            // Scanlines
            CrtScanlinesOverlay.Visibility = _crtScanlinesEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (_crtScanlinesEnabled)
            {
                UpdateScanlinesBrush(_scanlineThickness);
            }

            // Glitch
            CrtGlitchCanvas.Visibility = _crtGlitchEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (_crtGlitchEnabled)
            {
                _glitchTimer.Start();
            }
            else
            {
                _glitchTimer.Stop();
                ContentJitterTransform.X = 0;
                ContentJitterTransform.Y = 0;
            }

            // Snow
            CrtSnowOverlay.Visibility = _crtSnowEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (_crtSnowEnabled)
            {
                CrtSnowOverlay.Opacity = (_snowAmount / 100.0) * 0.40;
                _snowBitmaps.Clear();
                _snowTimer.Start();
            }
            else
            {
                _snowTimer.Stop();
            }
        }

        private void UpdateScanlinesBrush(double thickness)
        {
            double totalHeight = Math.Max(2.0, thickness * 2.0);
            CrtDrawingBrush.Viewport = new Rect(0, 0, 1, totalHeight);

            var drawingGroup = new DrawingGroup();
            var transparentRect = new RectangleGeometry(new Rect(0, 0, 1, totalHeight));
            transparentRect.Freeze();
            drawingGroup.Children.Add(new GeometryDrawing(Brushes.Transparent, null, transparentRect));

            var scanlineColor = Color.FromArgb(235, 0, 0, 0);
            var scanlineBrush = new SolidColorBrush(scanlineColor);
            scanlineBrush.Freeze();

            var scanlineRect = new RectangleGeometry(new Rect(0, 0, 1, thickness));
            scanlineRect.Freeze();
            drawingGroup.Children.Add(new GeometryDrawing(scanlineBrush, null, scanlineRect));

            drawingGroup.Freeze();
            CrtDrawingBrush.Drawing = drawingGroup;
        }

        #endregion

        #region Baud Rate & Playback Menu Handlers

        private void BaudRate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string rateStr && int.TryParse(rateStr, out int rate))
            {
                SetBaudRate(rate);
            }
        }

        private void BaudRateBadge_Click(object sender, RoutedEventArgs e)
        {
            // Cycle through baud rates on badge click: 300 -> 600 -> 1200 -> 2400 -> 4800 -> 9600 -> 300
            int[] rates = { 300, 600, 1200, 2400, 4800, 9600 };
            int current = _engine.BaudRate;
            int idx = Array.IndexOf(rates, current);
            int nextRate = rates[(idx + 1) % rates.Length];
            SetBaudRate(nextRate);
        }

        private void SetBaudRate(int baudRate)
        {
            _engine.SetBaudRate(baudRate);
            _settings.BaudRate = baudRate;
            BaudBadgeText.Text = $"[ {baudRate} BAUD ]";
            UpdateBaudRateMenuChecks(baudRate);
            SaveCurrentSettings();
        }

        private void UpdateBaudRateMenuChecks(int activeRate)
        {
            foreach (var item in BaudRateMenuItem.Items.OfType<MenuItem>())
            {
                if (item.Tag is string s && int.TryParse(s, out int r))
                {
                    item.IsChecked = (r == activeRate);
                }
            }
        }

        private void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            _engine.TogglePause();
        }

        private void SkipNext_Click(object sender, RoutedEventArgs e)
        {
            _engine.SkipNext();
        }

        private void RestartBoot_Click(object sender, RoutedEventArgs e)
        {
            _engine.RestartBoot();
        }

        private void ClearScreen_Click(object sender, RoutedEventArgs e)
        {
            _textRun.Text = string.Empty;
        }

        #endregion

        #region Phosphor & Background Color Handlers

        private void PhosphorColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string hex)
            {
                UpdatePhosphorColorMenuChecks(hex);
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    SetPhosphorColor(color, hex);
                }
                catch { }
            }
        }

        private void UpdatePhosphorColorMenuChecks(string activeHex)
        {
            foreach (var item in PhosphorColorMenuItem.Items.OfType<MenuItem>())
            {
                if (item.Tag is string hex)
                {
                    item.IsChecked = string.Equals(hex, activeHex, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void CustomPhosphorColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorInputDialog("Custom Phosphor Color", _currentPhosphorColor, (newColor, hex) =>
            {
                SetPhosphorColor(newColor, hex);
                UpdatePhosphorColorMenuChecks(hex);
            });
        }

        private void SetPhosphorColor(Color color, string hexCode)
        {
            _currentPhosphorColor = color;
            _settings.PhosphorColorHex = hexCode;

            _currentPhosphorBrush = new SolidColorBrush(color);
            _currentPhosphorBrush.Freeze();

            RebuildGlitchBrushes();
            _snowBitmaps.Clear();

            ApplyStyling();
            SaveCurrentSettings();
        }

        private void BackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string hex)
            {
                UpdateBackgroundColorMenuChecks(hex);
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    SetBackgroundColor(color, hex);
                }
                catch { }
            }
        }

        private void UpdateBackgroundColorMenuChecks(string activeHex)
        {
            foreach (var item in BackgroundColorMenuItem.Items.OfType<MenuItem>())
            {
                if (item.Tag is string hex)
                {
                    item.IsChecked = string.Equals(hex, activeHex, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void CustomBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorInputDialog("Custom Background Color", _currentBackgroundColor, (newColor, hex) =>
            {
                SetBackgroundColor(newColor, hex);
                UpdateBackgroundColorMenuChecks(hex);
            });
        }

        private void SetBackgroundColor(Color color, string hexCode)
        {
            _currentBackgroundColor = color;
            _settings.BackgroundColorHex = hexCode;

            _currentBackgroundBrush = new SolidColorBrush(color);
            _currentBackgroundBrush.Freeze();

            RebuildGlitchBrushes();

            ApplyStyling();
            SaveCurrentSettings();
        }

        private void ShowColorInputDialog(string title, Color currentColor, Action<Color, string> onApply)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 330,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(15, 22, 16)),
                Foreground = Brushes.White,
                WindowStyle = WindowStyle.ToolWindow
            };

            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock
            {
                Text = "Enter Hex Color Code (e.g. #00FF66, #FFB000, #08140B):",
                Foreground = Brushes.LightGray,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var tb = new TextBox
            {
                Text = $"#{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(8, 12, 9)),
                Foreground = _currentPhosphorBrush,
                CaretBrush = _currentPhosphorBrush
            };
            sp.Children.Add(tb);

            var btnOk = new Button
            {
                Content = "Apply Color",
                Width = 100,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true,
                Background = _currentPhosphorBrush,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold
            };
            btnOk.Click += (s, args) =>
            {
                try
                {
                    string hex = tb.Text.Trim();
                    if (!hex.StartsWith("#")) hex = "#" + hex;
                    var parsed = (Color)ColorConverter.ConvertFromString(hex);
                    onApply(parsed, hex);
                    dialog.DialogResult = true;
                    dialog.Close();
                }
                catch
                {
                    MessageBox.Show("Please enter a valid hex color code (#RRGGBB).", "Invalid Color", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            sp.Children.Add(btnOk);

            dialog.Content = sp;
            dialog.ShowDialog();
        }

        #endregion

        #region Dual Opacity & Sliders Handlers

        private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _settings == null) return;

            _backgroundOpacity = e.NewValue;
            _settings.BackgroundOpacity = _backgroundOpacity;

            if (BackgroundOpacityValueText != null)
            {
                BackgroundOpacityValueText.Text = $"{Math.Round(_backgroundOpacity * 100)}%";
            }

            if (WindowBackgroundBorder != null)
            {
                WindowBackgroundBorder.Opacity = _backgroundOpacity;
            }

            SaveCurrentSettings(false);
        }

        private void FontOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _settings == null) return;

            _fontOpacity = e.NewValue;
            _settings.FontOpacity = _fontOpacity;

            if (FontOpacityValueText != null)
            {
                FontOpacityValueText.Text = $"{Math.Round(_fontOpacity * 100)}%";
            }

            if (TerminalContentGrid != null)
            {
                TerminalContentGrid.Opacity = _fontOpacity;
            }

            SaveCurrentSettings(false);
        }

        #endregion

        #region Font Selection Handlers

        private void PopulateFontFamilies()
        {
            while (FontFamilyMenuItem.Items.Count > 2)
            {
                FontFamilyMenuItem.Items.RemoveAt(2);
            }

            string[] candidateFonts =
            {
                "Cascadia Code",
                "Cascadia Mono",
                "Consolas",
                "Lucida Console",
                "Courier New",
                "Fira Code",
                "JetBrains Mono"
            };

            var installedFamilies = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var fontName in candidateFonts)
            {
                if (installedFamilies.Contains(fontName) || fontName == "Consolas" || fontName == "Courier New")
                {
                    var item = new MenuItem
                    {
                        Header = fontName,
                        Tag = fontName,
                        IsCheckable = true,
                        IsChecked = _currentFontFamily.Source.Contains(fontName, StringComparison.OrdinalIgnoreCase)
                    };
                    item.Click += FontFamily_Click;
                    FontFamilyMenuItem.Items.Add(item);
                }
            }
        }

        private void ChooseAllFonts_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FontPickerWindow(_currentFontFamily, _currentFontSize, _currentPhosphorBrush)
            {
                Owner = this
            };

            if (picker.ShowDialog() == true)
            {
                _currentFontFamily = picker.SelectedFontFamily;
                _currentFontSize = picker.SelectedFontSize;
                _settings.FontFamily = _currentFontFamily.Source;
                _settings.FontSize = _currentFontSize;

                ApplyStyling();
                PopulateFontFamilies();
                UpdateFontSizeMenuChecks();
                SaveCurrentSettings();
            }
        }

        private void FontFamily_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string fontName)
            {
                foreach (var item in FontFamilyMenuItem.Items.OfType<MenuItem>().Where(i => i.Tag != null))
                {
                    item.IsChecked = (item == menuItem);
                }

                _currentFontFamily = new FontFamily(fontName);
                _settings.FontFamily = fontName;
                ApplyStyling();
                SaveCurrentSettings();
            }
        }

        private void FontSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string sizeStr && double.TryParse(sizeStr, out double size))
            {
                _currentFontSize = size;
                _settings.FontSize = size;
                UpdateFontSizeMenuChecks();
                ApplyStyling();
                SaveCurrentSettings();
            }
        }

        private void UpdateFontSizeMenuChecks()
        {
            foreach (var item in FontSizeMenuItem.Items.OfType<MenuItem>())
            {
                if (item.Tag is string tagStr && double.TryParse(tagStr, out double s))
                {
                    item.IsChecked = Math.Abs(s - _currentFontSize) < 0.1;
                }
            }
        }

        #endregion

        #region Effects Suite Handlers (Bloom, Scanlines, Glitch, Snow)

        private void BloomGlow_Click(object sender, RoutedEventArgs e)
        {
            _bloomEnabled = BloomGlowMenuItem.IsChecked;
            _settings.BloomEnabled = _bloomEnabled;

            TextBloomEffect.BlurRadius = _bloomEnabled ? _bloomIntensity : 0.0;
            TextBloomEffect.Opacity = (_bloomEnabled && _bloomIntensity > 0) ? 0.85 : 0.0;

            SaveCurrentSettings();
        }

        private void BloomIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _settings == null) return;

            _bloomIntensity = e.NewValue;
            _settings.BloomIntensity = _bloomIntensity;

            if (BloomIntensityValueText != null)
            {
                BloomIntensityValueText.Text = $"{Math.Round(_bloomIntensity)}px";
            }

            if (_bloomEnabled && TextBloomEffect != null)
            {
                TextBloomEffect.BlurRadius = _bloomIntensity;
                TextBloomEffect.Opacity = _bloomIntensity > 0 ? 0.85 : 0.0;
            }

            SaveCurrentSettings();
        }

        private void CrtScanlines_Click(object sender, RoutedEventArgs e)
        {
            _crtScanlinesEnabled = CrtScanlinesMenuItem.IsChecked;
            _settings.CrtScanlinesEnabled = _crtScanlinesEnabled;

            CrtScanlinesOverlay.Visibility = _crtScanlinesEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (_crtScanlinesEnabled)
            {
                UpdateScanlinesBrush(_scanlineThickness);
            }

            SaveCurrentSettings();
        }

        private void ScanlineThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _settings == null) return;

            _scanlineThickness = e.NewValue;
            _settings.ScanlineThickness = _scanlineThickness;

            if (ScanlineThicknessValueText != null)
            {
                ScanlineThicknessValueText.Text = $"{Math.Round(_scanlineThickness)}px";
            }

            if (_crtScanlinesEnabled)
            {
                UpdateScanlinesBrush(_scanlineThickness);
            }

            SaveCurrentSettings();
        }

        private void CrtGlitch_Click(object sender, RoutedEventArgs e)
        {
            _crtGlitchEnabled = CrtGlitchMenuItem.IsChecked;
            _settings.CrtGlitchEnabled = _crtGlitchEnabled;

            CrtGlitchCanvas.Visibility = _crtGlitchEnabled ? Visibility.Visible : Visibility.Collapsed;

            if (_crtGlitchEnabled)
            {
                _glitchTimer.Start();
            }
            else
            {
                _glitchTimer.Stop();
                ContentJitterTransform.X = 0;
                ContentJitterTransform.Y = 0;
            }

            SaveCurrentSettings();
        }

        private void GlitchChanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _settings == null) return;

            _glitchChance = e.NewValue;
            _settings.GlitchChance = _glitchChance;

            if (GlitchChanceValueText != null)
            {
                GlitchChanceValueText.Text = $"{Math.Round(_glitchChance)}%";
            }

            SaveCurrentSettings();
        }

        private void CrtSnow_Click(object sender, RoutedEventArgs e)
        {
            _crtSnowEnabled = CrtSnowMenuItem.IsChecked;
            _settings.CrtSnowEnabled = _crtSnowEnabled;

            CrtSnowOverlay.Visibility = _crtSnowEnabled ? Visibility.Visible : Visibility.Collapsed;

            if (_crtSnowEnabled)
            {
                CrtSnowOverlay.Opacity = (_snowAmount / 100.0) * 0.40;
                _snowBitmaps.Clear();
                _snowTimer.Start();
            }
            else
            {
                _snowTimer.Stop();
                CrtSnowOverlay.Opacity = 0.0;
            }

            SaveCurrentSettings();
        }

        private void SnowAmountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _settings == null) return;

            _snowAmount = e.NewValue;
            _settings.SnowAmount = _snowAmount;

            if (SnowAmountValueText != null)
            {
                SnowAmountValueText.Text = $"{Math.Round(_snowAmount)}%";
            }

            if (_crtSnowEnabled && CrtSnowOverlay != null)
            {
                CrtSnowOverlay.Opacity = (_snowAmount / 100.0) * 0.40;
            }

            SaveCurrentSettings();
        }

        #endregion

        #region Context Menu & Hover Bridge

        private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            App.EnsureStandardMenuDropAlignment();
            _effectsCloseTimer.Stop();
        }

        private void MainContextMenu_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            _effectsCloseTimer.Stop();
        }

        private void MainContextMenu_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _effectsCloseTimer.Stop();
        }

        private void MainContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            _effectsCloseTimer.Stop();
            DetachPopupHook();
        }

        private void EffectsMenuItem_MouseEnter(object sender, MouseEventArgs e)
        {
            _effectsCloseTimer.Stop();
        }

        private void EffectsMenuItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (EffectsMenuItem.IsSubmenuOpen)
            {
                _effectsCloseTimer.Stop();
                _effectsCloseTimer.Start();
            }
        }

        private void EffectsMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            _effectsCloseTimer.Stop();
            AttachPopupHook();
        }

        private void EffectsMenuItem_SubmenuClosed(object sender, RoutedEventArgs e)
        {
            _effectsCloseTimer.Stop();
            DetachPopupHook();
        }

        private void EffectsCloseTimer_Tick(object? sender, EventArgs e)
        {
            _effectsCloseTimer.Stop();
            if (EffectsMenuItem != null && EffectsMenuItem.IsSubmenuOpen)
            {
                EffectsMenuItem.IsSubmenuOpen = false;
            }
        }

        private void AttachPopupHook()
        {
            if (_subscribedPopupChild != null) return;
            try
            {
                var popup = FindVisualChild<Popup>(EffectsMenuItem);
                if (popup?.Child is FrameworkElement popupChild)
                {
                    _subscribedPopupChild = popupChild;
                    _subscribedPopupChild.MouseEnter += Submenu_MouseEnter;
                    _subscribedPopupChild.MouseLeave += Submenu_MouseLeave;
                }
            }
            catch { }
        }

        private void DetachPopupHook()
        {
            if (_subscribedPopupChild != null)
            {
                try
                {
                    _subscribedPopupChild.MouseEnter -= Submenu_MouseEnter;
                    _subscribedPopupChild.MouseLeave -= Submenu_MouseLeave;
                }
                catch { }
                _subscribedPopupChild = null;
            }
        }

        private void Submenu_MouseEnter(object sender, MouseEventArgs e)
        {
            _effectsCloseTimer.Stop();
        }

        private void Submenu_MouseLeave(object sender, MouseEventArgs e)
        {
            if (EffectsMenuItem.IsSubmenuOpen)
            {
                _effectsCloseTimer.Stop();
                _effectsCloseTimer.Start();
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is Slider slider)
            {
                double delta = e.Delta > 0 ? slider.SmallChange : -slider.SmallChange;
                slider.Value = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);
                e.Handled = true;
            }
        }

        #endregion

        #region Window Drag, Sizing & Lifecycle

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SaveCurrentSettings(false);
        }

        private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            Topmost = AlwaysOnTopMenuItem.IsChecked;
            _settings.AlwaysOnTop = Topmost;
            SaveCurrentSettings();
        }

        private void Shadow_Click(object sender, RoutedEventArgs e)
        {
            WindowDropShadow.Opacity = ShadowMenuItem.IsChecked ? 0.35 : 0.0;
            _settings.WindowShadow = ShadowMenuItem.IsChecked;
            SaveCurrentSettings();
        }

        private void CloseWidget_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ExitAll_Click(object sender, RoutedEventArgs e)
        {
            App.CleanProcessExit(0);
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_isClosed) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (!_isClosed) InvalidateVisual();
            });
        }

        private void SaveCurrentSettings(bool immediate = false)
        {
            if (!_isInitialized || _isClosed || _settings == null) return;

            if (WindowState == WindowState.Normal)
            {
                _settings.WindowWidth = ActualWidth;
                _settings.WindowHeight = ActualHeight;
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
            }

            if (immediate)
            {
                _saveDebounceTimer?.Stop();
                _settings.Save();
            }
            else
            {
                _saveDebounceTimer?.Stop();
                _saveDebounceTimer?.Start();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            _cursorTimer.Stop();
            _glitchTimer.Stop();
            _beamResetTimer.Stop();
            _snowTimer.Stop();
            _effectsCloseTimer.Stop();
            _saveDebounceTimer.Stop();

            SaveCurrentSettings(true);

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _engine.Dispose();

            base.OnClosed(e);
        }

        #endregion
    }
}