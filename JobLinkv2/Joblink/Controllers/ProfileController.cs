using Joblink.Security;
using Joblink.Services.Profile;
using Joblink.Services.Resumes;
using JobLinkv2.Services.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The logged-in user's own profile (phone, address, links). Employers have
    // one too, so any role may use it - but only for themselves. There is no
    // "list every profile".
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly ResumeDataStore _data;
        private readonly ProfilePhotoProcessor _photos;

        public ProfileController(ResumeDataStore data, ProfilePhotoProcessor photos)
        {
            _data = data;
            _photos = photos;
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var profile = _data.GetProfile(userId, id);

            return profile is null ? NotFound(new { message = "Profile not found." }) : Ok(WithPhoto(profile));
        }

        // 404 until the user saves a profile; 403 for anyone else's.
        [HttpGet("by-user/{userId}")]
        public IActionResult GetByUserId(int userId)
        {
            if (User.GetUserId() is not int callerId)
                return Unauthorized();

            if (userId != callerId)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only view your own profile." });

            var profile = _data.GetProfileByUser(callerId);

            return profile is null ? NotFound(new { message = "You haven't saved a profile yet." }) : Ok(WithPhoto(profile));
        }

        // Creates your profile. There is one per user, so a second is a 409.
        [HttpPost]
        public IActionResult Add([FromBody] SaveProfileRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request is null)
                return BadRequest(new { message = "Profile details are required.", code = "invalid" });

            var created = _data.AddProfile(userId, request.ToFields());

            return created is null
                ? Conflict(new { message = "You already have a profile - update it instead.", code = "profile_exists" })
                : Ok(WithPhoto(created));
        }

        // Replaces the details of your own profile.
        [HttpPut]
        public IActionResult Update([FromBody] SaveProfileRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request is null)
                return BadRequest(new { message = "Profile details are required.", code = "invalid" });

            var current = _data.GetProfileByUser(userId);

            // An id that isn't yours is treated like one that doesn't exist.
            if (current is null || (request.ProfileId is int asked && asked != current.ProfileId))
                return NotFound(new { message = "Profile not found." });

            return _data.UpdateProfile(userId, current.ProfileId, request.ToFields())
                ? Ok(WithPhoto(_data.GetProfile(userId, current.ProfileId)!))
                : NotFound(new { message = "Profile not found." });
        }

        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.DeleteProfile(userId, id)
                ? Ok(new { message = "Profile deleted." })
                : NotFound(new { message = "Profile not found." });
        }

        // ----- profile photo --------------------------------------------------------

        // Validated by content, not by filename or the browser's claimed type; resized, stripped
        // of metadata and re-encoded - see ProfilePhotoProcessor. Only the owner can ever call this.
        [HttpPost("photo")]
        [RequestSizeLimit(5_000_000)]
        public async Task<IActionResult> UploadPhoto(IFormFile? file)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (file is null || file.Length == 0)
                return BadRequest(new { message = "Choose a photo to upload.", code = "invalid" });

            if (file.Length > ProfilePhotoProcessor.MaxUploadBytes)
                return BadRequest(new { message = ProfilePhotoProcessor.Describe(PhotoRejection.TooLarge), code = "too_large" });

            byte[] uploaded;

            using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer);
                uploaded = buffer.ToArray();
            }

            var (rejection, photo) = _photos.Process(uploaded);

            if (rejection is { } why || photo is null)
                return BadRequest(new { message = ProfilePhotoProcessor.Describe(rejection ?? PhotoRejection.Corrupt), code = "invalid_image" });

            var photoKey = _data.SetPhoto(userId, photo.Bytes, photo.ContentType);

            return Ok(new { photoUrl = PhotoUrl(photoKey) });
        }

        [HttpDelete("photo")]
        public IActionResult RemovePhoto()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            _data.RemovePhoto(userId);

            return Ok(new { message = "Photo removed." });
        }

        // No login needed - the key itself (unguessable, regenerated on every upload) is what
        // makes this safe to serve publicly. There is no route that looks up a key by user id.
        [HttpGet("photo/{photoKey:guid}")]
        [AllowAnonymous]
        public IActionResult GetPhoto(Guid photoKey)
        {
            var row = _data.GetPhotoByKey(photoKey);

            if (row is null)
                return NotFound();

            Response.Headers.CacheControl = "public, max-age=31536000, immutable";

            return File(row.Photo, row.ContentType);
        }

        private string PhotoUrl(Guid photoKey) => $"{Request.Scheme}://{Request.Host}/api/Profile/photo/{photoKey}";

        // A profile as the API hands it out: PhotoKey itself is never serialized (see
        // ProfileModel) - only the full, ready-to-use PhotoUrl it turns into, or nothing at all.
        private object WithPhoto(JobLinkv2.Models.ProfileModel profile) => new
        {
            profile.ProfileId,
            profile.UserId,
            profile.Phone,
            profile.Address,
            profile.LinkedinUrl,
            profile.GithubUrl,
            profile.IsDeleted,
            PhotoUrl = profile.PhotoKey is { } key ? PhotoUrl(key) : null
        };
    }
}
