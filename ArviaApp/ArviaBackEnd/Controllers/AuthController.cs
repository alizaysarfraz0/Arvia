using ArviaBackEnd.Data;
using ArviaBackEnd.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace ArviaBackEnd.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public AuthController(UserManager<IdentityUser> userManager, IConfiguration configuration, ApplicationDbContext context) 
        {
            _userManager = userManager;
            _configuration = configuration;
            _context = context;
        }

        // ... [Keep your Register endpoint exactly as is] ...

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            var user = await _userManager.FindByNameAsync(model.Email);
            
            if (user == null || !await _userManager.CheckPasswordAsync(user, model.Password))
            {
                return Unauthorized(new AuthResponse { ErrorMessage = "Invalid credentials" });
            }

            // 👇 THE NEW LOGIC: Check if they are already logged in somewhere else
            var currentSession = await _context.UserSessions.FindAsync(user.Id);
            if (currentSession != null)
            {
                // We found an active session! Block this login attempt.
                return StatusCode(StatusCodes.Status409Conflict, new AuthResponse { 
                    ErrorMessage = "You are already logged in on another device. Would you like to force logout the other device?" 
                });
            }

            // If no session exists, generate a token and log them in
            var token = GenerateJwtToken(user);
            _context.UserSessions.Add(new UserSession { UserId = user.Id, ActiveToken = token });
            await _context.SaveChangesAsync();

            return Ok(new AuthResponse { Token = token });
        }

        // 👇 THE NEW "FORCE LOGOUT" ENDPOINT 👇
        [HttpPost("force-login")]
        public async Task<IActionResult> ForceLogin([FromBody] LoginModel model)
        {
            var user = await _userManager.FindByNameAsync(model.Email);
            
            if (user == null || !await _userManager.CheckPasswordAsync(user, model.Password))
            {
                return Unauthorized(new AuthResponse { ErrorMessage = "Invalid credentials" });
            }

            var token = GenerateJwtToken(user);

            // Find the old session and ruthlessly overwrite it
            var currentSession = await _context.UserSessions.FindAsync(user.Id);
            if (currentSession != null)
            {
                currentSession.ActiveToken = token;
            }
            else
            {
                _context.UserSessions.Add(new UserSession { UserId = user.Id, ActiveToken = token });
            }
            
            await _context.SaveChangesAsync();

            return Ok(new AuthResponse { Token = token });
        }

        // 👇 A NEW LOGOUT ENDPOINT (Important so users can leave cleanly) 👇
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            // We read the token from the current request
            var token = HttpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
            
            if (!string.IsNullOrEmpty(token))
            {
                // Find the session that matches this token and delete it
                var session = await _context.UserSessions.FirstOrDefaultAsync(s => s.ActiveToken == token);
                if (session != null)
                {
                    _context.UserSessions.Remove(session);
                    await _context.SaveChangesAsync();
                }
            }
            return Ok(new { Message = "Logged out successfully." });
        }

        private string GenerateJwtToken(IdentityUser user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Email!),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id)
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}