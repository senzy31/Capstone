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

            // ⚠️ TEMP: storing plain password (we will hash later)
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

            // ⚠️ TEMP: plain text comparison
            if (user.PasswordHash != request.Password)
                return Unauthorized("Invalid password");

            return Ok(new
            {
                message = "Login successful",
                user = new
                {
                    user.UserId,
                    user.FullName,
                    user.Email,
                    user.Role
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
    }

    // ✅ LOGIN REQUEST MODEL
    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }
}