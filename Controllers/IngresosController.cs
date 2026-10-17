using Havaianas.Facturacion;
using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Havaianas_Api.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class IngresosController : ControllerBase
    {
        private readonly sql_db sql = new sql_db();
        private readonly FacturaPdfParser facturaPdfParser = new FacturaPdfParser();

        [HttpGet]
        public ActionResult<IEnumerable<IngresoMercaderia>> Get(DateTime fechaInicio, DateTime fechaFin)
        {
            Havaianas.Facturacion.IngresoMercaderia[] ingresos = sql.obtener_ingresos(fechaInicio, fechaFin);
            return Ok(ingresos);
        }
        
        [HttpGet("Detalle")]
        public ActionResult<IEnumerable<IngresoMercaderiaDetalle>> GetDetalle(DateTime fechaInicio, DateTime fechaFin)
        {
            Havaianas.Facturacion.IngresoMercaderiaDetalle[] ingresos = sql.obtener_ingresos_detalle(fechaInicio, fechaFin);
            return Ok(ingresos);
        }

        [HttpGet("{id}")]
        public ActionResult<IngresoMercaderia> Get(int id)
        {
            IngresoMercaderia? ingreso = sql.obtener_ingreso(id);
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
        [HttpPost("Check")]
        // Editar un ingreso
        public ActionResult EditarCheck(IngresoMercaderiaDetalle ingreso)
        {
            bool resultado = sql.editar_checkingreso(ingreso);
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

        [HttpPost("ImportarFactura")]
        public ActionResult<ResultadoImportacionFactura> ImportarFactura(ImportarFactura factura)
        {
            if (factura == null || factura.fecha == default || factura.lineas.Count == 0)
            {
                return BadRequest(new { message = "La factura debe incluir fecha y al menos una línea." });
            }

            List<string> erroresValidacion = factura.lineas
                .Select((linea, indice) => new { linea, indice })
                .Where(item => item.linea.cantidad <= 0 || item.linea.precio < 0)
                .Select(item => $"La línea {item.indice + 1} debe tener cantidad mayor que cero y precio no negativo.")
                .ToList();

            if (erroresValidacion.Count > 0)
            {
                return BadRequest(new ResultadoImportacionFactura { errores = erroresValidacion });
            }

            ResultadoImportacionFactura resultado = sql.agregar_ingresos_factura(factura);
            if (resultado.errores.Count > 0)
            {
                return BadRequest(resultado);
            }

            return Ok(resultado);
        }

        [HttpPost("ImportarPdf")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult> ImportarPdf(IFormFile archivo, [FromForm] bool guardar = true)
        {
            if (archivo == null || archivo.Length == 0)
            {
                return BadRequest(new { message = "Debe enviar un archivo PDF." });
            }

            if (!string.Equals(Path.GetExtension(archivo.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "El archivo debe tener extensión .pdf." });
            }

            if (archivo.Length > 20 * 1024 * 1024)
            {
                return BadRequest(new { message = "El archivo PDF no puede superar los 20 MB." });
            }

            await using MemoryStream contenido = new MemoryStream();
            await archivo.CopyToAsync(contenido);
            contenido.Position = 0;

            ResultadoLecturaFactura lectura;
            try
            {
                lectura = facturaPdfParser.Extraer(contenido);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"No se pudo leer el PDF: {ex.Message}" });
            }

            if (lectura.errores.Count > 0)
            {
                return BadRequest(lectura);
            }

            if (!guardar)
            {
                return Ok(lectura);
            }

            ResultadoImportacionFactura importacion = sql.agregar_ingresos_factura(lectura.factura);
            if (importacion.errores.Count > 0)
            {
                return BadRequest(new { lectura.texto_extraido, importacion });
            }

            return Ok(new { lectura.texto_extraido, lectura.factura, importacion });
        }
    }
}
