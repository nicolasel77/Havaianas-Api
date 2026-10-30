using Havaianas_Api.Datos;
using Havaianas_Api.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PushController : ControllerBase
    {
        sql_db sql = new sql_db();
        private readonly AvisosPush avisos;

        public PushController(AvisosPush avisos)
        {
            this.avisos = avisos;
        }

        [HttpGet("clave-publica")]
        // Clave VAPID pública que el navegador necesita para suscribirse
        public ActionResult GetClavePublica()
        {
            if (avisos.ClavePublica == null)
            {
                return StatusCode(503, "Las notificaciones push no están configuradas en la Api.");
            }
            return Ok(new { clave = avisos.ClavePublica });
        }

        [HttpPost("suscripcion")]
        // Recibe PushSubscription.toJSON() del navegador. Si ya existe se actualizan las claves
        public ActionResult PostSuscripcion([FromBody] Modelos.SuscripcionPush suscripcion)
        {
            if (string.IsNullOrWhiteSpace(suscripcion.endpoint) || !suscripcion.endpoint.StartsWith("https://")
                || string.IsNullOrWhiteSpace(suscripcion.keys.p256dh) || string.IsNullOrWhiteSpace(suscripcion.keys.auth))
            {
                return BadRequest("Suscripción inválida.");
            }
            sql.guardar_suscripcion_push(suscripcion);
            return Ok();
        }

        [HttpPost("baja")]
        public ActionResult PostBaja([FromBody] Modelos.SuscripcionPush suscripcion)
        {
            sql.borrar_suscripcion_push(suscripcion.endpoint);
            return Ok();
        }

        [HttpPost("prueba")]
        // Manda una notificación de prueba a todos los suscriptos
        public async Task<ActionResult> PostPrueba()
        {
            if (avisos.ClavePublica == null)
            {
                return StatusCode(503, "Las notificaciones push no están configuradas en la Api.");
            }
            int enviadas = await avisos.Enviar("Havaianas", "Los avisos están funcionando.", "prueba");
            return Ok(new { enviadas });
        }
    }
}
