using System.Collections.Concurrent;

namespace Havaianas_Api.Servicios
{
    /// <summary>
    /// Frena a quien prueba contraseñas: después de varios intentos fallidos bloquea por un rato
    /// al usuario (aunque cambie de IP) y a la IP (aunque cambie de usuario). Vive en memoria:
    /// si se reinicia la Api se reinician los contadores.
    /// </summary>
    public class LimitadorLogin
    {
        public const int MaxFallosPorUsuario = 5;
        public const int MaxFallosPorIp = 20;
        public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(15);

        private readonly ConcurrentDictionary<string, Registro> _fallos = new();
        private readonly HashSet<string> _ipsSinLimite;

        // Login:IpsSinLimite = IPs a las que no se les aplica el límite por IP (por ejemplo la del servidor
        // de la WebApp, que hace el login de todos los usuarios). Las IPs locales/privadas ya están exceptuadas;
        // para esas solo cuenta el límite por usuario. La WebApp limita por IP del navegador por su cuenta.
        public LimitadorLogin(IConfiguration config)
        {
            _ipsSinLimite = new HashSet<string>(config.GetSection("Login:IpsSinLimite").Get<string[]>() ?? Array.Empty<string>());
        }

        private class Registro
        {
            public int Cantidad;
            public DateTime Desde;
        }

        /// <summary>Si está bloqueado devuelve cuánto falta para poder volver a probar.</summary>
        public TimeSpan? Bloqueado(string usuario, string ip)
        {
            var ahora = DateTime.UtcNow;
            TimeSpan? espera = null;
            foreach (var (clave, max) in Claves(usuario, ip))
            {
                if (_fallos.TryGetValue(clave, out var r))
                {
                    lock (r)
                    {
                        var fin = r.Desde + Ventana;
                        if (fin <= ahora) continue;
                        if (r.Cantidad >= max && (espera == null || fin - ahora > espera))
                            espera = fin - ahora;
                    }
                }
            }
            return espera;
        }

        public void RegistrarFallo(string usuario, string ip)
        {
            var ahora = DateTime.UtcNow;
            foreach (var (clave, _) in Claves(usuario, ip))
            {
                var r = _fallos.GetOrAdd(clave, _ => new Registro { Desde = ahora });
                lock (r)
                {
                    if (r.Desde + Ventana <= ahora)
                    {
                        r.Cantidad = 0;
                        r.Desde = ahora;
                    }
                    r.Cantidad++;
                }
            }
            Limpiar(ahora);
        }

        public void RegistrarExito(string usuario)
        {
            _fallos.TryRemove(ClaveUsuario(usuario), out _);
        }

        private static string ClaveUsuario(string usuario) => "u:" + usuario.Trim().ToLowerInvariant();

        private IEnumerable<(string, int)> Claves(string usuario, string ip)
        {
            yield return (ClaveUsuario(usuario), MaxFallosPorUsuario);
            if (!SinLimitePorIp(ip))
                yield return ("ip:" + ip, MaxFallosPorIp);
        }

        private bool SinLimitePorIp(string ip)
        {
            if (_ipsSinLimite.Contains(ip)) return true;
            if (!System.Net.IPAddress.TryParse(ip, out var dir)) return false;
            if (dir.IsIPv4MappedToIPv6) dir = dir.MapToIPv4();
            if (System.Net.IPAddress.IsLoopback(dir)) return true;
            var b = dir.GetAddressBytes();
            return dir.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168));
        }

        // Evita que el diccionario crezca sin límite con usuarios inventados
        private void Limpiar(DateTime ahora)
        {
            if (_fallos.Count < 1000) return;
            foreach (var par in _fallos)
            {
                if (par.Value.Desde + Ventana <= ahora) _fallos.TryRemove(par.Key, out _);
            }
        }
    }
}
