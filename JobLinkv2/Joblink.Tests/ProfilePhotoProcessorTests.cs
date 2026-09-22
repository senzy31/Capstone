using System.Text;
using Joblink.Services.Profile;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Joblink.Tests
{
    // ProfilePhotoProcessor: what it accepts is decided by reading the file itself (ImageSharp's
    // own format sniffing), never by a filename or a claimed content type - there is nothing here
    // that even takes one, so a file relabelled with a ".jpg" extension is exactly as safe as an
    // honestly-named one. No database.
    public sealed class ProfilePhotoProcessorTests
    {
        private readonly ProfilePhotoProcessor _processor = new();

        private static byte[] Encode(SixLabors.ImageSharp.Formats.IImageEncoder encoder, int width = 10, int height = 12)
        {
            using var image = new Image<Rgba32>(width, height);
            using var output = new MemoryStream();
            image.Save(output, encoder);
            return output.ToArray();
        }

        [Fact]
        public void A_file_over_2MB_is_rejected_before_anything_tries_to_decode_it()
        {
            var huge = new byte[ProfilePhotoProcessor.MaxUploadBytes + 1];

            var (rejection, photo) = _processor.Process(huge);

            Assert.Equal(PhotoRejection.TooLarge, rejection);
            Assert.Null(photo);
        }

        [Fact]
        public void An_empty_upload_is_rejected()
        {
            var (rejection, photo) = _processor.Process(Array.Empty<byte>());

            Assert.Equal(PhotoRejection.TooLarge, rejection);
            Assert.Null(photo);
        }

        [Fact]
        public void Plain_text_pretending_to_be_a_photo_is_rejected()
        {
            var textFile = Encoding.UTF8.GetBytes("this is not an image, whatever its filename claims");

            var (rejection, photo) = _processor.Process(textFile);

            Assert.Equal(PhotoRejection.UnsupportedFormat, rejection);
            Assert.Null(photo);
        }

        [Fact]
        public void A_real_image_in_a_format_that_is_not_on_the_allow_list_is_rejected()
        {
            // BMP: a real, fully-decodable image - just not JPG/PNG/WEBP.
            var bmp = Encode(new BmpEncoder());

            var (rejection, photo) = _processor.Process(bmp);

            Assert.Equal(PhotoRejection.UnsupportedFormat, rejection);
            Assert.Null(photo);
        }

        [Theory]
        [InlineData("jpeg")]
        [InlineData("png")]
        [InlineData("webp")]
        public void Jpg_png_and_webp_are_all_accepted_and_normalised_to_a_256_square_jpeg(string format)
        {
            SixLabors.ImageSharp.Formats.IImageEncoder encoder = format switch
            {
                "jpeg" => new JpegEncoder(),
                "png" => new PngEncoder(),
                "webp" => new WebpEncoder(),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

            var uploaded = Encode(encoder, width: 400, height: 100); // not already square

            var (rejection, photo) = _processor.Process(uploaded);

            Assert.Null(rejection);
            Assert.NotNull(photo);
            Assert.Equal("image/jpeg", photo!.ContentType);

            using var result = Image.Load(photo.Bytes);
            Assert.Equal(ProfilePhotoProcessor.OutputSize, result.Width);
            Assert.Equal(ProfilePhotoProcessor.OutputSize, result.Height);
        }

        [Fact]
        public void Metadata_such_as_exif_gps_data_does_not_survive_processing()
        {
            using var source = new Image<Rgba32>(50, 50);
            source.Metadata.ExifProfile = new ExifProfile();
            source.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitude, new[] { new SixLabors.ImageSharp.Rational(14), new SixLabors.ImageSharp.Rational(35), new SixLabors.ImageSharp.Rational(0) });

            using var input = new MemoryStream();
            source.Save(input, new JpegEncoder());

            var (rejection, photo) = _processor.Process(input.ToArray());

            Assert.Null(rejection);

            using var result = Image.Load(photo!.Bytes);
            Assert.Null(result.Metadata.ExifProfile);
        }

        [Fact]
        public void A_photo_already_square_is_not_distorted()
        {
            var uploaded = Encode(new PngEncoder(), width: 256, height: 256);

            var (_, photo) = _processor.Process(uploaded);

            using var result = Image.Load(photo!.Bytes);
            Assert.Equal(256, result.Width);
            Assert.Equal(256, result.Height);
        }
    }
}
