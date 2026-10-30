using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class FormasPagoController : ControllerBase
    {
        private readonly sql_db sql = new sql_db();

        // Lista unica de formas de pago (la descargan las sucursales)
        [HttpGet]
        public ActionResult<IEnumerable<formas_pago>> Get()
        {
            return Ok(sql.obtener_formas_pago());
        }

        // ID local -> ID unico de las formas de pago de una sucursal
        [HttpGet("Mapeo/{idSucursal}")]
        public ActionResult<IEnumerable<mapeo_id>> GetMapeo(int idSucursal)
        {
            return Ok(sql.obtener_mapeo_formas_pago(idSucursal));
        }

        // Recibe la tabla de formas de pago de una sucursal y la mapea a la lista unica
        [HttpPost("Sucursal/{idSucursal}")]
        public ActionResult PostSucursal(int idSucursal, [FromBody] List<formas_pago> formas)
        {
            if (idSucursal <= 0 || formas == null || formas.Count == 0)
            {
                return BadRequest("No se recibieron formas de pago.");
            }
            try
            {
                sql.guardar_formas_pago_sucursal(idSucursal, formas);
                return Ok(formas.Count);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al guardar formas de pago: {ex.Message}");
            }
        }
    }
}
