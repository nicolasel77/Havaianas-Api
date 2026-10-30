using System.Net;
using System.Text.Json;
using Havaianas_Api.Datos;
using WebPush;

namespace Havaianas_Api.Servicios
{
    /// <summary>
    /// Notificaciones push (Web Push con claves VAPID) a los navegadores que activaron los avisos en el Inicio.
    /// Claves en la configuración: Push:ClavePublica, Push:ClavePrivada y Push:Contacto (mailto: o https:).
    /// Se generan con: dotnet "Havaianas Api.dll" generar-claves-push
    /// </summary>
    public class AvisosPush
    {
        private readonly ILogger<AvisosPush> log;
        private readonly VapidDetails? vapid;
        private readonly WebPushClient cliente = new WebPushClient();

        public AvisosPush(IConfiguration config, ILogger<AvisosPush> log)
        {
            this.log = log;
            string? publica = config["Push:ClavePublica"];
            string? privada = config["Push:ClavePrivada"];
            string contacto = config["Push:Contacto"] ?? "mailto:avisos@lacosteleta.ar";
            if (!string.IsNullOrWhiteSpace(publica) && !string.IsNullOrWhiteSpace(privada))
            {
                vapid = new VapidDetails(contacto, publica, privada);
            }
            else
            {
                log.LogWarning("Notificaciones push desactivadas: faltan Push:ClavePublica y Push:ClavePrivada.");
            }
        }

        public string? ClavePublica => vapid?.PublicKey;

        /// <summary>
        /// Revisa los productos vendidos hoy y avisa los que quedaron con stock 0 en su sucursal (una vez por día).
        /// </summary>
        public async Task AvisarAgotados(IEnumerable<(int sucursal, int producto)> vendidos)
        {
            if (vapid == null) return;
            var sql = new sql_db();
            foreach (var (sucursal, producto) in vendidos)
            {
                try
                {
                    if (sql.stock_producto(sucursal, producto) != 0m) continue;
                    var datos = sql.datos_venta_producto(sucursal, producto);
                    if (datos == null || !sql.registrar_aviso_agotado(sucursal, producto)) continue;

                    var (nombreSucursal, nombreProducto, talle, color) = datos.Value;
                    string texto = $"{nombreSucursal} vendió el último {string.Join(" ", new[] { nombreProducto, talle, color }.Where(t => t != ""))}";
                    await Enviar("Se vendió la última unidad", texto, $"agotado-{sucursal}-{producto}");
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Error al avisar el agotado del producto {producto} en la sucursal {sucursal}", producto, sucursal);
                }
            }
        }

        /// <summary>
        /// Manda la notificación a todas las suscripciones. Las que el navegador dio de baja se borran.
        /// </summary>
        public async Task<int> Enviar(string titulo, string texto, string etiqueta)
        {
            if (vapid == null) return 0;
            var sql = new sql_db();
            string mensaje = JsonSerializer.Serialize(new { titulo, texto, etiqueta, url = "/" });
            int enviadas = 0;
            foreach (var s in sql.obtener_suscripciones_push())
            {
                try
                {
                    await cliente.SendNotificationAsync(new PushSubscription(s.endpoint, s.keys.p256dh, s.keys.auth), mensaje, vapid);
                    enviadas++;
                }
                catch (WebPushException ex) when (ex.StatusCode == HttpStatusCode.Gone || ex.StatusCode == HttpStatusCode.NotFound)
                {
                    sql.borrar_suscripcion_push(s.endpoint);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "No se pudo enviar la notificación push a {endpoint}", s.endpoint);
                }
            }
            return enviadas;
        }
    }
}
