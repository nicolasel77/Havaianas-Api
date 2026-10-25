using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Havaianas_Api.Servicios;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        public const int LargoMinimoPassword = 8;

        private readonly sql_db sql = new sql_db();
        private readonly TokenService _tokens;
        private readonly LimitadorLogin _limitador;
        private readonly ValidadorSesiones _sesiones;
        private readonly PasswordHasher<usuarios> _hasher = new();

        public AuthController(TokenService tokens, LimitadorLogin limitador, ValidadorSesiones sesiones)
        {
            _tokens = tokens;
            _limitador = limitador;
            _sesiones = sesiones;
        }

        // POST /Auth/login  { "usuario": "...", "password": "..." }
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Usuario) || string.IsNullOrEmpty(req.Password))
                return BadRequest(new { error = "Ingresá usuario y contraseña." });

            var nombreUsuario = req.Usuario.Trim();
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";

            var espera = _limitador.Bloqueado(nombreUsuario, ip);
            if (espera != null)
            {
                int minutos = (int)Math.Ceiling(espera.Value.TotalMinutes);
                Response.Headers.RetryAfter = ((int)espera.Value.TotalSeconds).ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = $"Demasiados intentos fallidos. Probá de nuevo en {minutos} minuto{(minutos == 1 ? "" : "s")}." });
            }

            usuarios? usuario = sql.obtener_usuario(nombreUsuario);

            var resultado = usuario == null
                ? PasswordVerificationResult.Failed
                : _hasher.VerifyHashedPassword(usuario, usuario.password_hash, req.Password);

            if (usuario == null || !usuario.activo || resultado == PasswordVerificationResult.Failed)
            {
                _limitador.RegistrarFallo(nombreUsuario, ip);
                // Demora fija: probar contraseñas a mano se vuelve lento
                await Task.Delay(1000);
                return Unauthorized(new { error = "Usuario o contraseña incorrectos." });
            }

            _limitador.RegistrarExito(nombreUsuario);

            if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
                sql.actualizar_hash_usuario(usuario.id, _hasher.HashPassword(usuario, req.Password));

            sql.registrar_login_usuario(usuario.id);
            return Ok(_tokens.Generar(usuario));
        }

        // POST /Auth/cambiar-password  { "actual": "...", "nueva": "..." }
        // Cambia la contraseña del usuario del token y cierra sus otras sesiones. Devuelve un token nuevo.
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpPost("cambiar-password")]
        public async Task<ActionResult<LoginResponse>> CambiarPassword([FromBody] CambiarPasswordRequest req)
        {
            if (!int.TryParse(User.FindFirst("sub")?.Value, out int id))
                return Unauthorized();

            var usuario = sql.obtener_usuario_por_id(id);
            if (usuario == null || !usuario.activo)
                return Unauthorized();

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
            var espera = _limitador.Bloqueado(usuario.usuario, ip);
            if (espera != null)
                return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Demasiados intentos fallidos. Probá más tarde." });

            if (string.IsNullOrEmpty(req.Actual)
                || _hasher.VerifyHashedPassword(usuario, usuario.password_hash, req.Actual) == PasswordVerificationResult.Failed)
            {
                _limitador.RegistrarFallo(usuario.usuario, ip);
                await Task.Delay(1000);
                return BadRequest(new { error = "La contraseña actual no es correcta." });
            }

            var problema = ProblemaPassword(req.Nueva, usuario.usuario);
            if (problema != null)
                return BadRequest(new { error = problema });

            if (req.Nueva == req.Actual)
                return BadRequest(new { error = "La contraseña nueva tiene que ser distinta de la actual." });

            sql.cambiar_password_usuario(usuario.id, _hasher.HashPassword(usuario, req.Nueva));
            _sesiones.Olvidar(usuario.id);

            // Releer para tomar el sello nuevo
            var actualizado = sql.obtener_usuario_por_id(usuario.id)!;
            return Ok(_tokens.Generar(actualizado));
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

        /// <summary>null si la contraseña sirve; si no, el motivo.</summary>
        public static string? ProblemaPassword(string? password, string usuario)
        {
            if (string.IsNullOrEmpty(password) || password.Length < LargoMinimoPassword)
                return $"La contraseña tiene que tener al menos {LargoMinimoPassword} caracteres.";
            if (password.Length > 128)
                return "La contraseña es demasiado larga.";
            if (string.Equals(password, usuario, StringComparison.OrdinalIgnoreCase))
                return "La contraseña no puede ser igual al usuario.";
            if (password.Distinct().Count() < 4)
                return "La contraseña es demasiado simple.";
            return null;
        }
    }
}
