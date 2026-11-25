using Android.Graphics;
using Bitmap = Android.Graphics.Bitmap;
using Canvas = Android.Graphics.Canvas;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;

namespace Shink.Mobile.Platforms.Android;

/// <summary>Filtered artwork for Android canvases without RenderEffect.</summary>
internal sealed class AndroidBarBitmapBackdrop(int width, int height) : IDisposable
{
    private readonly Bitmap _samples = CreateBitmap(width * 4, height * 4);
    private readonly Bitmap _half = CreateBitmap(width * 2, height * 2);
    // RenderThread may still be displaying a previous frame when the UI thread scrolls.
    private readonly Bitmap[] _frames = [CreateBitmap(width, height), CreateBitmap(width, height), CreateBitmap(width, height)];
    private readonly Canvas _canvas = new();
    private readonly Paint _filter = new(PaintFlags.FilterBitmap | PaintFlags.Dither);
    private readonly global::Android.Graphics.Rect _destination = new();
    private int _displayed = -1;

    public int Width => width;
    public int Height => height;

    public Canvas BeginCapture()
    {
        _canvas.SetBitmap(_samples);
        _samples.EraseColor(Color.Transparent);
        return _canvas;
    }

    public void FinishCapture(Paint tint, Paint fade)
    {
        // Each exact 2:1 filtered reduction averages four neighbouring pixels.
        // The two passes integrate sixteen artwork samples per output pixel.
        Downsample(_samples, _half);
        var next = (_displayed + 1) % _frames.Length;
        Downsample(_half, _frames[next]);
        _canvas.DrawRect(0, 0, width, height, tint);
        _canvas.DrawRect(0, 0, width, height, fade);
        _displayed = next;
    }

    public void Draw(Canvas canvas, int viewWidth, int viewHeight)
    {
        if (_displayed < 0) return;
        _destination.Set(0, 0, viewWidth, viewHeight);
        canvas.DrawBitmap(_frames[_displayed], null, _destination, _filter);
    }

    private void Downsample(Bitmap input, Bitmap output)
    {
        _canvas.SetBitmap(output);
        output.EraseColor(Color.Transparent);
        _destination.Set(0, 0, output.Width, output.Height);
        _canvas.DrawBitmap(input, null, _destination, _filter);
    }

    private static Bitmap CreateBitmap(int w, int h) =>
        Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)!;

    public void Dispose()
    {
        _canvas.Dispose();
        _filter.Dispose();
        _destination.Dispose();
        _samples.Dispose();
        _half.Dispose();
        foreach (var frame in _frames) frame.Dispose();
    }
}
