using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Havaianas_Api.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly sql_db sql = new sql_db();
        private readonly TokenService _tokens;
        private readonly PasswordHasher<usuarios> _hasher = new();

        public AuthController(TokenService tokens)
        {
            _tokens = tokens;
        }

        // POST /Auth/login  { "usuario": "...", "password": "..." }
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Usuario) || string.IsNullOrEmpty(req.Password))
                return BadRequest(new { error = "Ingresá usuario y contraseña." });

            usuarios? usuario = sql.obtener_usuario(req.Usuario.Trim());

            var resultado = usuario == null
                ? PasswordVerificationResult.Failed
                : _hasher.VerifyHashedPassword(usuario, usuario.password_hash, req.Password);

            if (usuario == null || !usuario.activo || resultado == PasswordVerificationResult.Failed)
            {
                // Demora fija para que probar contraseñas a mano sea lento
                await Task.Delay(1000);
                return Unauthorized(new { error = "Usuario o contraseña incorrectos." });
            }

            if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
                sql.actualizar_password_usuario(usuario.id, _hasher.HashPassword(usuario, req.Password));

            sql.registrar_login_usuario(usuario.id);
            return Ok(_tokens.Generar(usuario));
        }

        // GET /Auth/yo  -> datos del usuario del token (sirve para probar que el token es válido)
        [HttpGet("yo")]
        public IActionResult Yo()
        {
            return Ok(new
            {
                id = User.FindFirst("sub")?.Value,
                usuario = User.Identity?.Name,
                nombre = User.FindFirst("nombre")?.Value,
                id_sucursal = User.FindFirst("id_sucursal")?.Value
            });
        }
    }
}
