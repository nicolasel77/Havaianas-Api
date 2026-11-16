using Havaianas_Api.Datos;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace Havaianas_Api.Servicios
{
    /// <summary>
    /// Revisa en cada pedido que el usuario del token siga activo y que su sello no haya cambiado.
    /// Así un cambio de contraseña o dar de baja a alguien corta sus sesiones abiertas
    /// (con hasta 1 minuto de demora, porque el resultado se guarda en memoria).
    /// </summary>
    public class ValidadorSesiones
    {
        public const string ClaimSello = "sello";
        private static readonly TimeSpan Cache = TimeSpan.FromMinutes(1);

        private readonly IMemoryCache _cache;

        public ValidadorSesiones(IMemoryCache cache)
        {
            _cache = cache;
        }

        public bool EsValido(ClaimsPrincipal principal)
        {
            var sub = principal.FindFirst("sub")?.Value;
            var sello = principal.FindFirst(ClaimSello)?.Value;
            if (!int.TryParse(sub, out int id) || string.IsNullOrEmpty(sello)) return false;

            var selloActual = _cache.GetOrCreate($"sello:{id}", entrada =>
            {
                entrada.AbsoluteExpirationRelativeToNow = Cache;
                var usuario = new sql_db().obtener_usuario_por_id(id);
                return usuario != null && usuario.activo ? usuario.sello : null;
            });
            return selloActual != null && string.Equals(selloActual, sello, StringComparison.OrdinalIgnoreCase);
        }

        public void Olvidar(int idUsuario) => _cache.Remove($"sello:{idUsuario}");
    }
}
