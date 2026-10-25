using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace Havaianas_Api.Servicios
{
    /// <summary>
    /// Autenticación de los programas de las sucursales (Havaianas Escritorio), que no tienen usuario
    /// y contraseña: mandan la cabecera X-Clave-Sucursal con una clave propia de cada sucursal.
    /// Las claves se configuran en el servidor en "ClavesSucursales" (id de sucursal -> clave),
    /// por ejemplo con la variable de entorno ClavesSucursales__3 = (clave de la sucursal 3).
    /// </summary>
    public class ClaveSucursalHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string Esquema = "ClaveSucursal";
        public const string Cabecera = "X-Clave-Sucursal";
        public const int LargoMinimo = 32;

        private readonly IConfiguration _config;

        public ClaveSucursalHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock, IConfiguration config)
            : base(options, logger, encoder, clock)
        {
            _config = config;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Cabecera, out var valor) || string.IsNullOrEmpty(valor))
                return Task.FromResult(AuthenticateResult.NoResult());

            var recibida = Hash(valor.ToString());
            foreach (var clave in _config.GetSection("ClavesSucursales").GetChildren())
            {
                if (string.IsNullOrEmpty(clave.Value) || clave.Value.Length < LargoMinimo) continue;

                // Comparación en tiempo constante para no dar pistas de la clave
                if (CryptographicOperations.FixedTimeEquals(recibida, Hash(clave.Value)))
                {
                    var identidad = new ClaimsIdentity(new[]
                    {
                        new Claim("unique_name", $"sucursal-{clave.Key}"),
                        new Claim("id_sucursal", clave.Key),
                        new Claim("tipo", "sucursal"),
                    }, Esquema, "unique_name", null);
                    var ticket = new AuthenticationTicket(new ClaimsPrincipal(identidad), Esquema);
                    return Task.FromResult(AuthenticateResult.Success(ticket));
                }
            }

            Logger.LogWarning("Clave de sucursal inválida desde {Ip}", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Clave de sucursal inválida."));
        }

        private static byte[] Hash(string texto) => SHA256.HashData(Encoding.UTF8.GetBytes(texto));
    }
}
