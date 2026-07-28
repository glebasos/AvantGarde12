// -----------------------------------------------------------------------------
// PROJECT   : Avant Garde
// COPYRIGHT : Andy Thomas (C) 2022-25
// LICENSE   : GPL-3.0-or-later
// HOMEPAGE  : https://github.com/kuiperzone/AvantGarde
//
// Avant Garde is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.
//
// Avant Garde is distributed in the hope that it will be useful, but WITHOUT
// ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along
// with Avant Garde. If not, see <https://www.gnu.org/licenses/>.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using AvantGarde.Loading;
using AvantGarde.Settings;
using AvantGarde.ViewModels;

namespace AvantGarde.Views;

/// <summary>
/// Implements the central preview widget, without surrounding area or related controls.
/// </summary>
public partial class PreviewControl : UserControl
{
    private readonly PreviewControlViewModel _model = new();

    /// <summary>
    /// Constructor.
    /// </summary>
    public PreviewControl()
    {
        DataContext = _model;
        AvaloniaXamlLoader.Load(this);
        Update(null, 1.0);
    }

    /// <summary>
    /// Occurs when the user interacts with the preview.
    /// </summary>
    public Action<PointerEventMessage>? PointerEventOccurred;

    /// <summary>
    /// Occurs when the user clicks on "Goto" to locate error.
    /// </summary>
    public Action<PreviewError>? GotoClick;

    /// <summary>
    /// Gets or sets the preview window color.
    /// </summary>
    public PreviewWindowTheme WindowTheme
    {
        get { return _model.Theme; }
        set { _model.Theme = value; }
    }

    /// <summary>
    /// Gets whether has content. This may be true if non-xaml image.
    /// </summary>
    public bool IsEmpty { get; private set; }

    /// <summary>
    /// Gets the current payload.
    /// </summary>
    public PreviewPayload? Payload  { get; private set; }

    /// <summary>
    /// Gets the current scale value.
    /// </summary>
    public double Scale { get; private set; } = 1.0;

    /// <summary>
    /// Gets the size in dips of what the control draws around the preview bitmap - the window
    /// top-bar above it and the dimension labels either side. Fit-to-window has to allow for it,
    /// and it is measured rather than modelled because it varies with the payload: the top-bar is
    /// there only for a window, the labels only when dimensions are shown, and the top-bar scales
    /// with the zoom. The result is empty until a bitmap has been arranged.
    /// </summary>
    public Size ChromeSize
    {
        get
        {
            var image = _model.MainImage?.Size ?? default;
            var bounds = Bounds.Size;

            if (!(image.Width > 0) || !(image.Height > 0) ||
                bounds.Width < image.Width || bounds.Height < image.Height)
            {
                return default;
            }

            return new Size(bounds.Width - image.Width, bounds.Height - image.Height);
        }
    }

    /// <summary>
    /// Updates the preview at the given scale.
    /// </summary>
    public void Update(PreviewPayload? payload, double scale, bool showDimensions = true)
    {
        Debug.WriteLine($"{nameof(PreviewControl)}.{nameof(Update)}");
        Debug.WriteLine("PAYLOAD: " + payload?.Name ?? "{null}");
        Debug.WriteLine("Dimension: " + payload?.Source?.PixelSize);
        Debug.WriteLine("Error: " + payload?.Error);

        Payload = payload;
        IsEmpty = payload?.Source == null;

        if (payload?.Source != null)
        {
            _model.IsWindow = payload.IsWindow;
            _model.WindowTitleScale = scale;
            _model.WindowIcon = payload.WindowIcon;
            _model.WindowTitleText = payload.WindowTitle;
            _model.WindowCanResize = payload.WindowCanResize;

            _model.MainImage = payload.Source;
            _model.MainBackground = GlobalModel.Global.Colors.PreviewTile;

            if (showDimensions)
            {
                _model.WidthText = GetDimensionText(payload.Width, payload.NaturalWidth);
                _model.HeightText = GetDimensionText(payload.Height, payload.NaturalHeight);
            }
            else
            {
                _model.WidthText = null;
                _model.HeightText = null;
            }
        }
        else
        {
            _model.IsWindow = false;
            _model.MainImage = null;
            _model.MainBackground = null;
            _model.WidthText = null;
            _model.HeightText = null;
        }

        if (payload?.Error != null)
        {
            Debug.WriteLine("Error line: " + payload.Error.LineNum);
            _model.HasErrorLocation = payload.Error.LineNum > 0;
            _model.MessageText = payload.Error.Message;
            _model.MainImage ??= GlobalModel.Global.Assets.WarnIcon;
        }
        else
        {
            _model.HasErrorLocation = false;
            _model.MessageText = _model.MainImage == null ? "None" : null;
        }
    }

    /// <summary>
    /// Gets a bitmap of the image, which may include the window top-bar.
    /// </summary>
    public Bitmap? GetBitmap()
    {
        if (Payload?.Source != null && Payload.IsWindow == true)
        {
            var window = new Window();

            try
            {
                var clone = new PreviewControl();
                var temp = Payload.Clone();
                temp.Error = null;

                clone.Update(temp, Scale, false);

                // Keep window from displaying
                window.ShowInTaskbar = false;
                window.WindowState = WindowState.Minimized;
                window.WindowDecorations = WindowDecorations.None;

                window.Content = clone;
                window.SizeToContent = SizeToContent.WidthAndHeight;

                window.Show();

                var pxz = new PixelSize((int)window.DesiredSize.Width, (int)window.DesiredSize.Height);
                var bmp = new RenderTargetBitmap(pxz, new Vector(96, 96));

                bmp.Render(window);

                window.Close();
                return bmp;
            }
            finally
            {
                window?.Close();
            }
        }

        return Payload?.Source;
    }

    /// <summary>
    /// Formats a dimension label, preferring the size the designer host actually rendered at over
    /// the locally parsed d:DesignWidth/Height. The two agree where a design size is declared; where
    /// one is not, the host's value is the only one there is and the declared value shows as NaN.
    /// Any min/max from the markup is retained, as it still describes the control.
    /// </summary>
    private static string GetDimensionText(ControlDimension declared, double natural)
    {
        if (!double.IsFinite(natural) || natural <= 0)
        {
            return declared.ToString(true);
        }

        natural = Math.Round(natural);

        if (declared.HasRange)
        {
            return new ControlDimension(natural, declared.Min, declared.Max).ToString(true);
        }

        return new ControlDimension(natural).ToString(true);
    }

    private void PreviewPointerMovedHandler(object? sender, PointerEventArgs e)
    {
        if (sender is Visual visual)
        {
            PointerEventOccurred?.Invoke(new PointerEventMessage(visual, e));
        }
    }

    private void PreviewPointerPressedHandler(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Visual visual)
        {
            PointerEventOccurred?.Invoke(new PointerEventMessage(visual, e));
        }
    }

    private void PreviewPointerReleasedHandler(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Visual visual)
        {
            PointerEventOccurred?.Invoke(new PointerEventMessage(visual, e));
        }
    }

    private void GotoClickHandler(object? sender, RoutedEventArgs e)
    {
        if (Payload?.Error != null)
        {
            GotoClick?.Invoke(Payload.Error);
        }
    }
}