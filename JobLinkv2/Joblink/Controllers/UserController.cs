using BCrypt.Net;
using Joblink.Security;
using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        UserServices userServices = new UserServices();

        private readonly JwtTokenService _tokens;

        public UserController(JwtTokenService tokens)
        {
            _tokens = tokens;
        }

        // ✅ GET ALL USERS
        [HttpGet]
        public IActionResult GetAll()
        {
            var users = userServices.GetAll();
            return Ok(users);
        }

        // ✅ GET USER BY ID
        [HttpGet("{id}")]
        public IActionResult GetUserId(int id)
        {
            var user = userServices.GetUserId(id);

            if (user == null)
                return NotFound();

            return Ok(user);
        }

        // ✅ REGISTER (SIGNUP)
        [HttpPost]
        public IActionResult AddUser([FromBody] UserModel user)
        {
            if (user == null)
                return BadRequest();

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
                return BadRequest("Password is required");

            // The client sends the raw password over HTTPS (transport is
            // already encrypted); it never touches the database until it's
            // hashed here.
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
            user.CreatedAt = DateTime.Now;
            user.IsDeleted = false;

            var result = userServices.AddUser(user);

            if (!result)
                return BadRequest("Failed to create user");

            return Ok(new { message = "User registered successfully" });
        }

        // ✅ LOGIN
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            if (request == null)
                return BadRequest();

            var users = userServices.GetAll();

            var user = users.FirstOrDefault(u =>
                u.Email == request.Email && !u.IsDeleted);

            if (user == null)
                return Unauthorized("User not found");

            if (!VerifyAndUpgradePassword(user, request.Password))
                return Unauthorized("Invalid password");

            return Ok(new
            {
                message = "Login successful",
                token = _tokens.CreateToken(user),
                user = new
                {
                    user.UserId,
                    user.FullName,
                    user.Email,
                    user.Role,
                    user.CompanyName
                }
            });
        }

        // ✅ UPDATE
        [HttpPut]
        public IActionResult Update([FromBody] UserModel user)
        {
            var result = userServices.UpdateUser(user);

            if (!result)
                return BadRequest("Update failed");

            return Ok(new { message = "User updated successfully" });
        }

        // ✅ DELETE
        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var result = userServices.DeleteUser(id);

            if (!result)
                return BadRequest("Delete failed");

            return Ok(new { message = "User deleted successfully" });
        }

        // Verifies a login attempt against a bcrypt hash. Accounts created
        // before bcrypt was introduced still have a plain-text password_hash
        // (not a valid bcrypt hash, so BCrypt.Verify throws SaltParseException)
        // - for those, fall back to a plain comparison once and, if it
        // matches, transparently rehash and save it so it's never stored in
        // plain text again.
        private bool VerifyAndUpgradePassword(UserModel user, string suppliedPassword)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(suppliedPassword, user.PasswordHash);
            }
            catch (SaltParseException)
            {
                if (user.PasswordHash != suppliedPassword)
                    return false;

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(suppliedPassword);
                userServices.UpdateUser(user);
                return true;
            }
        }
    }

    // ✅ LOGIN REQUEST MODEL
    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }
}