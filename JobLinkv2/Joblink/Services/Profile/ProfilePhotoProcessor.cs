using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Joblink.Services.Profile
{
    public enum PhotoRejection { TooLarge, UnsupportedFormat, Corrupt }

    public sealed record ProcessedPhoto(byte[] Bytes, string ContentType);

    // Turns an uploaded file into something safe to store and serve as a profile photo: real
    // JPG/PNG/WEBP only (found by reading the file itself - ImageSharp identifies a format from
    // its signature bytes, never from a filename or the browser's claimed content type), resized
    // and cropped to a square, every metadata block stripped (EXIF can carry GPS coordinates),
    // then re-encoded as JPEG - one predictable output format regardless of what came in.
    public sealed class ProfilePhotoProcessor
    {
        public const int MaxUploadBytes = 2 * 1024 * 1024;
        public const int OutputSize = 256;
        private const string OutputContentType = "image/jpeg";

        private static readonly HashSet<string> AllowedFormats =
            new(StringComparer.OrdinalIgnoreCase) { "JPEG", "PNG", "WEBP" };

        public (PhotoRejection? Rejection, ProcessedPhoto? Photo) Process(byte[] uploaded)
        {
            if (uploaded.Length == 0 || uploaded.Length > MaxUploadBytes)
                return (PhotoRejection.TooLarge, null);

            using var input = new MemoryStream(uploaded);

            Image image;

            try
            {
                image = Image.Load(input);
            }
            catch (UnknownImageFormatException)
            {
                return (PhotoRejection.UnsupportedFormat, null);
            }
            catch (InvalidImageContentException)
            {
                return (PhotoRejection.Corrupt, null);
            }

            using (image)
            {
                var format = image.Metadata.DecodedImageFormat;

                if (format is null || !AllowedFormats.Contains(format.Name))
                    return (PhotoRejection.UnsupportedFormat, null);

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Crop,
                    Size = new Size(OutputSize, OutputSize),
                    Position = AnchorPositionMode.Center
                }));

                image.Metadata.ExifProfile = null;
                image.Metadata.IccProfile = null;
                image.Metadata.IptcProfile = null;
                image.Metadata.XmpProfile = null;

                using var output = new MemoryStream();
                image.SaveAsJpeg(output, new JpegEncoder { Quality = 85 });

                return (null, new ProcessedPhoto(output.ToArray(), OutputContentType));
            }
        }

        public static string Describe(PhotoRejection rejection) => rejection switch
        {
            PhotoRejection.TooLarge => "Photos must be 2MB or smaller.",
            PhotoRejection.UnsupportedFormat => "Photos must be a JPG, PNG or WEBP image.",
            PhotoRejection.Corrupt => "That file couldn't be read as an image.",
            _ => "That photo couldn't be used."
        };
    }
}
