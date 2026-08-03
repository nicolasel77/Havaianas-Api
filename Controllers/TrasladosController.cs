using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TrasladosController : ControllerBase
    {
        private readonly sql_db sql = new sql_db();

        [HttpGet]
        public ActionResult<IEnumerable<traslados>> Get(DateTime fechaInicio, DateTime fechaFin)
        {
            traslados[] tras = sql.obtener_traslados(fechaInicio, fechaFin);
            return Ok(tras);
        }

        [HttpGet("{id}")]
        public ActionResult<traslados> Get(int id)
        {
            traslados t = sql.obtener_traslado(id);
            if (t == null)
            {
                return NotFound();
            }
            return Ok(t);
        }

        [HttpPut]
        public ActionResult Editar(traslados traslado)
        {
            bool resultado = sql.editar_traslado(traslado);
            if (resultado)
            {
                return Ok(new { message = "Traslado editado correctamente" });
            }
            else
            {
                return BadRequest(new { message = "Error al editar el traslado" });
            }
        }

        [HttpPost]
        public ActionResult Post(traslados traslado)
        {
            bool resultado = sql.agregar_traslado(traslado);
            if (resultado)
            {
                return Ok(new { message = "Traslado agregado correctamente" });
            }
            else
            {
                return BadRequest(new { message = "Error al agregar el traslado" });
            }
        }
    }
}
