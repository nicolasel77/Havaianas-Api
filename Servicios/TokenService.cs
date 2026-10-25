using Havaianas_Api.Modelos;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Havaianas_Api.Servicios
{
    // Configuración de "Jwt" en appsettings.json
    public class JwtOpciones
    {
        public string Emisor { get; set; } = "Havaianas-Api";
        public string Audiencia { get; set; } = "Havaianas-WebApp";
        public string Clave { get; set; } = "";
        public int HorasValidez { get; set; } = 12;

        public SymmetricSecurityKey ObtenerClave() => new(Encoding.UTF8.GetBytes(Clave));
    }

    public class TokenService
    {
        private readonly JwtOpciones _opciones;

        public TokenService(JwtOpciones opciones)
        {
            _opciones = opciones;
        }

        public LoginResponse Generar(usuarios usuario)
        {
            var expira = DateTime.UtcNow.AddHours(_opciones.HorasValidez);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, usuario.id.ToString()),
                new(JwtRegisteredClaimNames.UniqueName, usuario.usuario),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new("nombre", usuario.nombre),
            };
            if (usuario.id_sucursal != null)
            {
                claims.Add(new Claim("id_sucursal", usuario.id_sucursal.Value.ToString()));
            }

            var token = new JwtSecurityToken(
                issuer: _opciones.Emisor,
                audience: _opciones.Audiencia,
                claims: claims,
                expires: expira,
                signingCredentials: new SigningCredentials(_opciones.ObtenerClave(), SecurityAlgorithms.HmacSha256));

            return new LoginResponse(
                new JwtSecurityTokenHandler().WriteToken(token),
                expira,
                usuario.usuario,
                usuario.nombre,
                usuario.id_sucursal,
                usuario.sucursal);
        }
    }
}
