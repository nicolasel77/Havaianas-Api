using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    // Las formas de pago se administran solo en el servidor; las sucursales las descargan con GET /FormasPago.
    [ApiController]
    [Route("[controller]")]
    public class FormasPagoController : ControllerBase
    {
        private readonly sql_db sql = new sql_db();

        [HttpGet]
        public ActionResult<IEnumerable<formas_pago>> Get()
        {
            return Ok(sql.obtener_formas_pago());
        }

        [HttpPost]
        public ActionResult Post([FromBody] formas_pago formaPago)
        {
            if (string.IsNullOrWhiteSpace(formaPago.descripcion))
            {
                return BadRequest(new { message = "Falta la descripción" });
            }
            int nid = sql.agregar_forma_pago(formaPago, out formas_pago? duplicado);
            if (duplicado != null)
            {
                return Conflict(new { message = $"Ya existe la forma de pago {duplicado.id}: {duplicado.descripcion}", formaPago = duplicado });
            }
            return Ok(nid);
        }

        [HttpPut("{id:int}")]
        public ActionResult Put(int id, [FromBody] formas_pago formaPago)
        {
            if (string.IsNullOrWhiteSpace(formaPago.descripcion))
            {
                return BadRequest(new { message = "Falta la descripción" });
            }
            formaPago.id = id;
            bool existe = sql.editar_forma_pago(formaPago, out formas_pago? duplicado);
            if (duplicado != null)
            {
                return Conflict(new { message = $"Ya existe la forma de pago {duplicado.id}: {duplicado.descripcion}", formaPago = duplicado });
            }
            if (!existe)
            {
                return NotFound();
            }
            return Ok(new { message = "Forma de pago editada correctamente" });
        }

        // POST /FormasPago/Equivalencias/{suc}
        // La sucursal manda sus formas de pago (id + descripcion) y se vinculan por descripción con las del servidor.
        // Devuelve las que no se pudieron vincular (cargarlas a mano en FormasPagoSucursal, ver sql/unificacion).
        [HttpPost("Equivalencias/{suc:int}")]
        public ActionResult PostEquivalencias(int suc, [FromBody] List<formas_pago> formasSucursal)
        {
            if (formasSucursal == null || formasSucursal.Count == 0)
            {
                return BadRequest("No se recibieron formas de pago.");
            }
            List<formas_pago> sinVincular = sql.equivalencias_formas_pago(suc, formasSucursal);
            return Ok(new { recibidas = formasSucursal.Count, sin_vincular = sinVincular });
        }
    }
}
