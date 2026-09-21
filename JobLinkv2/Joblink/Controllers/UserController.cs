using Joblink.Security;
using Joblink.Services.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // Sign up, log in, and the logged-in user's own account.
    //
    // Nothing here returns a password hash or accepts a database row from the
    // client: requests are the small classes in AccountDtos.cs, responses are
    // AccountResponse. There is no "list all users" and no delete - the pages
    // don't use them, and both were open to anyone.
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly UserAccountService _accounts;
        private readonly JwtTokenService _tokens;

        public UserController(UserAccountService accounts, JwtTokenService tokens)
        {
            _accounts = accounts;
            _tokens = tokens;
        }

        // Your own account. Asking for anyone else's is a 403.
        [HttpGet("{id}")]
        [Authorize]
        public IActionResult GetUserId(int id)
        {
            if (User.GetUserId() is not int callerId)
                return Unauthorized();

            if (id != callerId)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only view your own account." });

            var user = _accounts.Get(callerId);

            if (user is null)
                return NotFound(new { message = "Account not found." });

            return Ok(AccountResponse.From(user));
        }

        // Sign up. Errors are plain text: the signup page shows them as they are.
        [HttpPost]
        public IActionResult AddUser([FromBody] SignupRequest? request)
        {
            var result = _accounts.Signup(request);

            return result.Outcome switch
            {
                AccountOutcome.Ok => Ok(new { message = "User registered successfully" }),
                AccountOutcome.Duplicate => Conflict(result.Message),
                _ => BadRequest(result.Message)
            };
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest? request)
        {
            var result = _accounts.Login(request?.Email, request?.Password);

            if (!result.IsOk)
                return Unauthorized(result.Message);

            var user = result.Value!;

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

        // Change your own name, email and (employers) company name. Which user
        // it is comes from the login token; nothing else can be changed here.
        //
        // A wrong current password is a 403, not a 401: the pages treat a 401
        // as an expired login and sign the user out.
        [HttpPut]
        [Authorize]
        public IActionResult Update([FromBody] UpdateAccountRequest? request)
        {
            if (User.GetUserId() is not int callerId)
                return Unauthorized();

            var result = _accounts.UpdateAccount(callerId, request);

            return result.Outcome switch
            {
                AccountOutcome.Ok => Ok(new { message = "User updated successfully", user = AccountResponse.From(result.Value!) }),
                AccountOutcome.NotFound => NotFound(new { message = result.Message }),
                AccountOutcome.Duplicate => Conflict(new { message = result.Message, code = "email_taken" }),
                AccountOutcome.PasswordRequired => BadRequest(new { message = result.Message, code = "password_required" }),
                AccountOutcome.WrongPassword => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Message, code = "wrong_password" }),
                _ => BadRequest(new { message = result.Message, code = "invalid" })
            };
        }
    }

    public class LoginRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
    }
}
