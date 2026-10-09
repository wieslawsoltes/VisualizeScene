using SkiaSharp;
using VisualizeScene.Rendering.Skia;
using Windows.Storage;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphControl
{
    private SKTypeface? customTypeface;
    private long fontRequest;
    /// <summary>Loads an application font without relying on a browser's native font manager. The control owns the loaded face.</summary>
    public async Task LoadTypefaceAsync(Uri applicationUri)
    {
        ArgumentNullException.ThrowIfNull(applicationUri);
        if (applicationUri.Scheme != "ms-appx") throw new ArgumentException("Use an ms-appx application resource URI.", nameof(applicationUri));
        var request = ++fontRequest; var file = await StorageFile.GetFileFromApplicationUriAsync(applicationUri);
        using var stream = await file.OpenStreamForReadAsync(); using var memory = new MemoryStream(); await stream.CopyToAsync(memory);
        using var data = SKData.CreateCopy(memory.ToArray()); var face = SKTypeface.FromData(data) ?? throw new InvalidDataException("The application font could not be decoded.");
        if (disposed || request != fontRequest) { face.Dispose(); return; }
        GraphRenderer next;
        try { next = new GraphRenderer(face); } catch { face.Dispose(); throw; }
        renderer.Dispose(); customTypeface?.Dispose(); customTypeface = face; renderer = next; Invalidate();
    }
}
