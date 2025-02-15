#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2022 Pixeval/UIHelper.cs
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Pixeval.Attributes;
using Pixeval.Misc;
using Pixeval.Util.IO;
using Pixeval.Utilities;
using QRCoder;
using Microsoft.UI.Xaml.Data;
using Pixeval.Options;
using Pixeval.Util.Threading;
using WinUI3Utilities;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using Brush = Microsoft.UI.Xaml.Media.Brush;
using Color = Windows.UI.Color;
using Image = SixLabors.ImageSharp.Image;
using Point = Windows.Foundation.Point;
using Size = Windows.Foundation.Size;

namespace Pixeval.Util.UI;

public static partial class UIHelper
{
    /// <summary>
    /// With higher <paramref name="magnitude"/> you will get brighter color and vice-versa.
    /// </summary>
    /// <param name="color"></param>
    /// <param name="magnitude"></param>
    /// <returns></returns>
    public static Color Brighten(this Color color, int magnitude)
    {
        const int maxByte = 255;

        magnitude = magnitude.CoerceIn((-255, 255));
        int red = color.R;
        int green = color.G;
        int blue = color.B;

        switch (magnitude)
        {
            case < 0:
            {
                (red, green, blue) = (red + magnitude, green + magnitude, blue + magnitude);
                break;
            }
            case > 0:
            {
                var limit = maxByte - magnitude;
                (red, green, blue) = ((red + magnitude).CoerceIn((0, limit)), (green + magnitude).CoerceIn((0, limit)), (blue + magnitude).CoerceIn((0, limit)));
                break;
            }
        }

        return Color.FromArgb(color.A, (byte) red, (byte) green, (byte) blue);
    }

    public static async Task<double> GetImageAspectRatioAsync(Stream stream, bool disposeOfStream = true)
    {
        using var image = await Image.LoadAsync(stream);
        var result = image.Width / (double) image.Height;
        if (disposeOfStream)
        {
            await stream.DisposeAsync();
        }

        return result;
    }

    public static async Task<Color> GetDominantColorAsync(Stream stream, bool disposeOfStream = true)
    {
        using var image = await Image.LoadAsync<Rgb24>(stream);
        image.Mutate(x => x
            .Resize(new ResizeOptions { Sampler = KnownResamplers.NearestNeighbor, Size = new SixLabors.ImageSharp.Size(100, 0) })
            .Quantize(new OctreeQuantizer(new QuantizerOptions { Dither = null, MaxColors = 1 })));
        var pixel = image[0, 0];
        if (disposeOfStream)
        {
            await stream.DisposeAsync();
        }
        return Color.FromArgb(0xFF, pixel.R, pixel.G, pixel.B);
    }

    public static async Task LoadMoreItemsAsync(this AdvancedCollectionView acv, uint count, Action<LoadMoreItemsResult> callback)
    {
        var result = await acv.LoadMoreItemsAsync(count);
        callback(result);
    }

    //public static T GetDataContext<T>(this FrameworkElement element)
    //{
    //    return (T) element.DataContext;
    //}

    //public static T GetDataContext<T>(this object element)
    //{
    //    return ((FrameworkElement) element).GetDataContext<T>(); // direct cast will throw exception if the type check fails, and that's exactly what we want
    //}

    public static ImageSource GetImageSourceFromUriRelativeToAssetsImageFolder(string relativeToAssetsImageFolder)
    {
        return new BitmapImage(new Uri($"ms-appx:///Assets/Images/{relativeToAssetsImageFolder}"));
    }

    public static void ScrollToElement(this ScrollViewer scrollViewer, UIElement element)
    {
        var transform = element.TransformToVisual((UIElement)scrollViewer.Content);
        var position = transform.TransformPoint(new Point(0, 0));
        scrollViewer.ChangeView(null, position.Y, null, false);
    }

    public static Storyboard CreateStoryboard(params Timeline[] animations)
    {
        var sb = new Storyboard();
        foreach (var animation in animations)
        {
            sb.Children.Add(animation);
        }

        return sb;
    }

    public static DoubleAnimation CreateDoubleAnimation(this DependencyObject depObj,
        string property,
        Duration duration = default,
        EasingFunctionBase? easingFunction = null,
        double by = default,
        double from = default,
        double to = default)
    {
        var animation = new DoubleAnimation
        {
            Duration = duration,
            EasingFunction = easingFunction,
            By = by,
            From = from,
            To = to
        };
        Storyboard.SetTarget(animation, depObj);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    public static Storyboard GetStoryboard(this Timeline timeline)
    {
        return CreateStoryboard(timeline);
    }

    public static void BeginStoryboard(this Timeline timeline)
    {
        CreateStoryboard(timeline).Begin();
    }

    public static void SetClipboardContent(Action<DataPackage> contentAction)
    {
        Clipboard.SetContent(new DataPackage().Apply(contentAction));
    }

    public static async Task SetClipboardContentAsync(Func<DataPackage, Task> contentAction)
    {
        var package = new DataPackage();
        await contentAction(package);
        Clipboard.SetContent(package);
    }

    public static void NavigateByNavigationViewTag(this Frame frame, NavigationView sender, NavigationTransitionInfo? transitionInfo = null)
    {
        if (sender.SelectedItem is NavigationViewItem { Tag: NavigationViewTag tag })
        {
            frame.Navigate(tag.NavigateTo, tag.Parameter, transitionInfo ?? new SuppressNavigationTransitionInfo());
        }
    }

    public static Visibility Inverse(this Visibility visibility)
    {
        return visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility ToVisibility(this bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static FontIcon GetFontIcon(this FontIconSymbols symbol, double? fontSize = null)
    {
        var icon = new FontIcon
        {
            Glyph = symbol.GetMetadataOnEnumMember()
        };
        if (fontSize is not null)
        {
            icon.FontSize = fontSize.Value;
        }

        return icon;
    }

    public static FontIconSource GetFontIconSource(this FontIconSymbols symbol, double? fontSize = null, Brush? foregroundBrush = null)
    {
        var icon = new FontIconSource
        {
            Glyph = symbol.GetMetadataOnEnumMember(),
        };
        if (fontSize is not null)
        {
            icon.FontSize = fontSize.Value;
        }

        if (foregroundBrush is not null)
        {
            icon.Foreground = foregroundBrush;
        }

        return icon;
    }

    public static T? GetComboBoxSelectedItemTag<T>(this ComboBox box, T? defaultValue = default)
    {
        return box is { SelectedItem: ComboBoxItem { Tag: T t } } ? t : defaultValue;
    }

    public static bool IsFullyOrPartiallyVisible(this FrameworkElement child, FrameworkElement scrollViewer)
    {
        var childTransform = child.TransformToVisual(scrollViewer);
        var childRectangle = childTransform.TransformBounds(new Rect(new Point(0, 0), child.RenderSize));
        var ownerRectangle = new Rect(new Point(0, 0), scrollViewer.RenderSize);
        return ownerRectangle.IntersectsWith(childRectangle);
    }

    public static IEnumerable<T> FindChildren<T>(this FrameworkElement startNode) where T : DependencyObject
    {
        return startNode.FindChildren().OfType<T>();
    }

    public static void ClearContent(this RichEditBox box)
    {
        box.Document.SetText(TextSetOptions.None, string.Empty);
    }

    public static void Deactivate(this ObservableRecipient recipient)
    {
        recipient.IsActive = false;
    }

    public static void Collapse(this UIElement element)
    {
        element.Visibility = Visibility.Collapsed;
    }

    public static void Show(this UIElement element)
    {
        element.Visibility = Visibility.Visible;
    }

    public static async Task<SoftwareBitmapSource> GenerateQrCodeForUrlAsync(string url)
    {
        var qrCodeGen = new QRCodeGenerator();
        var urlPayload = new PayloadGenerator.Url(url);
        var qrCodeData = qrCodeGen.CreateQrCode(urlPayload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new BitmapByteQRCode(qrCodeData);
        var bytes = qrCode.GetGraphic(20);
        return await (await IOHelper.GetRandomAccessStreamFromByteArrayAsync(bytes)).GetSoftwareBitmapSourceAsync(true);
    }

    public static async Task<SoftwareBitmapSource> GenerateQrCodeAsync(string content)
    {
        var qrCodeGen = new QRCodeGenerator();
        var qrCodeData = qrCodeGen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new BitmapByteQRCode(qrCodeData);
        var bytes = qrCode.GetGraphic(20);
        return await (await IOHelper.GetRandomAccessStreamFromByteArrayAsync(bytes)).GetSoftwareBitmapSourceAsync(true);
    }

    public static IAsyncOperation<StorageFolder?> OpenFolderPickerAsync(PickerLocationId suggestedStartLocation)
    {
        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = suggestedStartLocation,
            FileTypeFilter = { "*" }
        };
        return folderPicker.InitializeWithWindow().PickSingleFolderAsync();
    }

    public static IAsyncOperation<StorageFile?> OpenFileSavePickerAsync(string suggestedFileName, string fileTypeName, string fileTypeId)
    {
        var savePicker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            FileTypeChoices =
            {
                [fileTypeId] = new List<string> { fileTypeId }
            },
            SuggestedFileName = suggestedFileName
        };
        return savePicker.InitializeWithWindow().PickSaveFileAsync();
    }

    public static IAsyncOperation<StorageFile?> OpenFileOpenPickerAsync()
    {
        var openPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
            FileTypeFilter = { "*" }
        };
        return openPicker.InitializeWithWindow().PickSingleFileAsync();
    }

    public static Task AwaitPageTransitionAsync<T>(this FrameworkElement root) where T : Page
    {
        return ThreadingHelper.SpinWaitAsync(() => root.FindDescendant<T>() is null);
    }
    
    public static Color ParseHexColor(string hex)
    {
        var trimmed = !hex.StartsWith('#') ? $"#{hex}" : hex;
        var color = ColorTranslator.FromHtml(trimmed);
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    /// <summary>
    /// Get the scale factor of the original image when it is contained inside an <see cref="Microsoft.UI.Xaml.Controls.Image"/> control, and the <see cref="Microsoft.UI.Xaml.Controls.Image.Stretch"/>
    /// property is set to <see cref="Stretch.UniformToFill"/> or <see cref="Stretch.Uniform"/>
    /// </summary>
    public static double GetImageScaledFactor(double originalImageWidth, double originalImageHeight, double imageWidth, double imageHeight, double imageZoomFactor)
    {
        var illustResolution = originalImageWidth / originalImageHeight;
        var imageResolution = imageWidth / imageHeight;

        var displayImageResolution = (imageResolution - illustResolution) switch
        {
            > 0 => imageHeight * imageZoomFactor / originalImageHeight, // imageResolution - illustResolution > 0: the height is filled
            _ => imageWidth * imageZoomFactor / originalImageWidth, // imageResolution - illustResolution < 0: the width is filled; or = 0, then the choose is arbitrary
        };
        return displayImageResolution;
    }
}
