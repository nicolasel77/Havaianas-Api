using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class IngresosController : ControllerBase
    {
        private readonly sql_db sql = new sql_db();

        [HttpGet]
        public ActionResult<IEnumerable<IngresoMercaderia>> Get(DateTime fechaInicio, DateTime fechaFin)
        {
            IngresoMercaderia[] ingresos = sql.obtener_ingresos(fechaInicio, fechaFin);
            return Ok(ingresos);
        }
        [HttpGet("{id}")]
        public ActionResult<IngresoMercaderia> Get(int id)
        {
            IngresoMercaderia ingreso = sql.obtener_ingreso(id);
            if (ingreso == null)
            {
                return NotFound();
            }
            return Ok(ingreso);
        }

        [HttpPut]
        // Editar un ingreso
        public ActionResult Editar(IngresoMercaderia ingreso)
        {
            bool resultado = sql.editar_ingreso(ingreso);
            if (resultado)
            {
                return Ok(new { message = "Ingreso editado correctamente" });
            }
            else
            {
                return BadRequest(new { message = "Error al editar el ingreso" });
            }
        }

        [HttpPost]
        public ActionResult Post(IngresoMercaderia ingreso)
        {
            // Lógica para agregar un nuevo ingreso
            bool resultado = sql.agregar_ingreso(ingreso);
            if (resultado)
            {
                return Ok(new { message = "Ingreso agregado correctamente" });
            }
            else
            {
                return BadRequest(new { message = "Error al agregar el ingreso" });
            }
        }
    }
}
