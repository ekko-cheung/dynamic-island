using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Core;

/// <summary>Decodes WinRT image streams into frozen WPF bitmaps usable from any thread.</summary>
public static class StreamImage
{
    public static async Task<ImageSource?> LoadAsync(Windows.Storage.Streams.IRandomAccessStreamReference? reference, int decodeWidth = 160)
    {
        if (reference is null) return null;
        try
        {
            using var winStream = await reference.OpenReadAsync();
            using var ms = new MemoryStream();
            await winStream.AsStreamForRead().CopyToAsync(ms);
            if (ms.Length == 0) return null;
            ms.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodeWidth;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
