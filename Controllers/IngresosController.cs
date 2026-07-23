using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class IngresosController : ControllerBase
    {
        private readonly sql_db sql = new sql_db();

        [HttpGet]
        public ActionResult<IEnumerable<Ingresos>> Get(DateTime fecha)
        {
            Ingresos[] ingresos = sql.obtener_ingresos(fecha);
            return Ok(ingresos);
        }

        [HttpPost]
        public ActionResult Post(Ingresos ingreso)
        {
            // Lógica para agregar un nuevo ingreso
            return CreatedAtAction(nameof(Get), new { fecha = ingreso.fecha }, ingreso);
        }
    }
}
