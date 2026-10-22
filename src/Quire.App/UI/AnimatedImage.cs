using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Quire.UI;

/// <summary>
/// An <see cref="Image"/> that plays animated GIFs. WPF's own Image only shows the first frame,
/// so this composes each frame (honouring offsets, delays and disposal) and animates Source.
/// GIFs always play, as they do in browsers, even when Windows animation effects are turned off.
/// </summary>
public sealed class AnimatedImage : Image
{
    public static readonly DependencyProperty GifDataProperty = DependencyProperty.Register(
        nameof(GifData), typeof(object), typeof(AnimatedImage),
        new PropertyMetadata(null, (d, _) => ((AnimatedImage)d).Reload()));

    private static readonly ConditionalWeakTable<byte[], GifAnimation> Cache = [];

    /// <summary>The GIF file's bytes. Typed as object because XAML cannot set array-typed properties from a binding element.</summary>
    public object? GifData
    {
        get => GetValue(GifDataProperty);
        set => SetValue(GifDataProperty, value);
    }

    private void Reload()
    {
        BeginAnimation(SourceProperty, null);
        Source = null;

        if (GifData is not byte[] data || Load(data) is not { } animation)
            return;

        Source = animation.Frames[0];
        if (animation.Frames.Count > 1)
            BeginAnimation(SourceProperty, animation.Timeline);
    }

    private static GifAnimation? Load(byte[] data)
    {
        if (Cache.TryGetValue(data, out var cached))
            return cached;

        try
        {
            var animation = GifAnimation.Decode(data);
            Cache.AddOrUpdate(data, animation);
            return animation;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException or IOException)
        {
            return null;
        }
    }

    private sealed class GifAnimation
    {
        private const int DefaultDelayMs = 100;

        public required IReadOnlyList<BitmapSource> Frames { get; init; }
        public required ObjectAnimationUsingKeyFrames Timeline { get; init; }

        public static GifAnimation Decode(byte[] data)
        {
            var decoder = new GifBitmapDecoder(new MemoryStream(data, writable: false),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            var (width, height) = ScreenSize(decoder);
            var frames = new List<BitmapSource>();
            var timeline = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            var elapsed = TimeSpan.Zero;

            BitmapSource? canvas = null;     // what is on screen after the previous frame
            BitmapSource? restorePoint = null;

            foreach (var frame in decoder.Frames)
            {
                var meta = frame.Metadata as BitmapMetadata;
                var left = Query(meta, "/imgdesc/Left");
                var top = Query(meta, "/imgdesc/Top");
                var delay = Query(meta, "/grctlext/Delay") * 10;
                var disposal = Query(meta, "/grctlext/Disposal");

                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    if (canvas is not null)
                        context.DrawImage(canvas, new Rect(0, 0, width, height));
                    context.DrawImage(frame, new Rect(left, top, frame.PixelWidth, frame.PixelHeight));
                }
                var composed = Render(visual, width, height);
                frames.Add(composed);

                timeline.KeyFrames.Add(new DiscreteObjectKeyFrame(composed, KeyTime.FromTimeSpan(elapsed)));
                // Browsers treat very small delays as 100 ms; matching that keeps speed familiar.
                elapsed += TimeSpan.FromMilliseconds(delay < 20 ? DefaultDelayMs : delay);

                // Disposal decides what the next frame is drawn over.
                switch (disposal)
                {
                    case 2: // restore to background: clear this frame's area
                        var cleared = new DrawingVisual();
                        using (var context = cleared.RenderOpen())
                        {
                            context.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,
                                new RectangleGeometry(new Rect(0, 0, width, height)),
                                new RectangleGeometry(new Rect(left, top, frame.PixelWidth, frame.PixelHeight))));
                            context.DrawImage(composed, new Rect(0, 0, width, height));
                            context.Pop();
                        }
                        canvas = Render(cleared, width, height);
                        break;
                    case 3: // restore to previous
                        canvas = restorePoint;
                        break;
                    default:
                        restorePoint = composed;
                        canvas = composed;
                        break;
                }
            }

            // A final key frame at the total duration makes the last frame hold for its full delay.
            timeline.Duration = elapsed;
            return new GifAnimation { Frames = frames, Timeline = Freeze(timeline) };
        }

        private static (int Width, int Height) ScreenSize(GifBitmapDecoder decoder)
        {
            var meta = decoder.Metadata;
            var width = Query(meta, "/logscrdesc/Width");
            var height = Query(meta, "/logscrdesc/Height");
            if (width > 0 && height > 0)
                return (width, height);
            var first = decoder.Frames[0];
            return (first.PixelWidth, first.PixelHeight);
        }

        private static BitmapSource Render(Visual visual, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static ObjectAnimationUsingKeyFrames Freeze(ObjectAnimationUsingKeyFrames timeline)
        {
            timeline.Freeze();
            return timeline;
        }

        private static int Query(BitmapMetadata? metadata, string query)
        {
            try
            {
                return metadata?.GetQuery(query) switch
                {
                    ushort value => value,
                    byte value => value,
                    int value => value,
                    _ => 0,
                };
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException)
            {
                return 0;
            }
        }
    }
}
