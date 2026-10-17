using Havaianas_Api.Datos;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using System.Drawing;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class StockController : ControllerBase
    {
        sql_db sql = new sql_db();
        [HttpGet]
        // GET: /Stock?suc=1&fechaInicio=2024-01-01&fechaFin=2024-01-31
        // Parámetros:
        // - suc: ID de la sucursal (opcional, por defecto 0 para todas las sucursales)
        // - fechaInicio: Fecha de inicio para filtrar el stock (opcional)
        // - fechaFin: Fecha de fin para filtrar el stock (opcional)
        // - producto: ID del producto (opcional, por defecto 0 para todos los productos)
        public ActionResult<IEnumerable<Modelos.stock>> Get(int suc = 0, DateTime? fecha = null, int producto = 0, int codart = 0, bool agrSuc = false, bool agrProd = false, string? talle = "", string? genero = "", string? color = "", bool todos = false)
        {
            return Ok(sql.stock(suc, fecha, producto, codart, agrSuc, agrProd, talle, genero, color, todos));
        }

        [HttpGet("NombreProducto")]
        // GET: /Stock?suc=1&fechaInicio=2024-01-01&fechaFin=2024-01-31
        // Parámetros:
        // - suc: ID de la sucursal (opcional, por defecto 0 para todas las sucursales)
        // - fechaInicio: Fecha de inicio para filtrar el stock (opcional)
        // - fechaFin: Fecha de fin para filtrar el stock (opcional)
        // - producto: nombre del producto
        public ActionResult<IEnumerable<Modelos.stock>> GetPorNombre(int suc = 0, DateTime? fecha = default, string? producto = "", string? talle = "", string? genero = "", int color = 0)
        {
            if (fecha == null) fecha = DateTime.Now;
            return Ok(sql.stock(suc, fecha, producto));
        }

        // Ajuste de stock
        [HttpPost("AjusteStock")]
        public ActionResult AjusteStock([FromBody] Modelos.stock stock)
        {
            if (stock == null)
            {
                return BadRequest("El objeto stock no puede ser nulo.");
            }
            bool resultado = sql.ajusteStock(stock);
            if (resultado)
            {
                return Ok("Ajuste de stock realizado correctamente.");
            }
            else
            {
                return StatusCode(500, "Error al realizar el ajuste de stock.");
            }
        }
    }
}
