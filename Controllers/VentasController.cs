using Havaianas_Api.Datos;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class VentasController : ControllerBase
    {
        sql_db sql = new sql_db();

        [HttpGet]
        // GET: /Ventas?suc=1&fechaInicio=2024-01-01&fechaFin=2024-01-31&agDia=true&agProd=false&agSuc=false
        // Parámetros:
        // - suc: ID de la sucursal (opcional, por defecto 0 para todas las sucursales)
        // - fechaInicio: Fecha de inicio para filtrar las ventas (opcional)
        // - fechaFin: Fecha de fin para filtrar las ventas (opcional)
        // - agDia: Agregar agrupación por día (opcional, por defecto false)
        // - agProd: Agregar agrupación por producto (opcional, por defecto false)
        // - agSuc: Agregar agrupación por sucursal (opcional, por defecto false)
        public ActionResult<IEnumerable<Modelos.ventas>> Get(int suc = 0, DateTime? fechaInicio = null, DateTime? fechaFin = null, bool agDia = false, bool agProd = false
            , bool agTalle = false, bool agColor = false, bool agSuc = false)
        {
            return Ok(sql.ventas(suc, fechaInicio, fechaFin, agDia, agProd, agTalle, agColor, agSuc));
        }

        [HttpGet("ultimo/{suc}")]
        public ActionResult<IEnumerable<Modelos.ventas>> GetUltimo(int suc)
        {
            return Ok(sql.ultima_venta(suc));
        }

        [HttpGet("resumen-mini")]
        // GET: /Ventas/resumen-mini?suc=1&fechaInicio=2024-01-01&fechaFin=2024-01-31
        // Parámetros:
        // - suc: ID de la sucursal (opcional, por defecto 0 para todas las sucursales)
        // - fechaInicio: Fecha de inicio para filtrar las ventas (opcional)
        // - fechaFin: Fecha de fin para filtrar las ventas (opcional)
        public ActionResult<IEnumerable<Modelos.ventas>> GetResumenMini(int suc = 0, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            return Ok(sql.ventas_resumen_mini(suc, fechaInicio, fechaFin));
        }

        [HttpPost]
        // Recive las ventas de las sucursales y las ingresa a la tabla ventas
        public ActionResult Post([FromBody] List<Modelos.ventas> ventas)
        {
            if (ventas == null || ventas.Count == 0)
            {
                return BadRequest("No se recibieron ventas para procesar.");
            }
            try
            {
                sql.insertar_ventas(ventas);
                return Ok("Ventas insertadas correctamente.");
            }
            catch (Exception ex)
            {
                // Log the exception (ex) here if needed
                return StatusCode(500, $"Error al insertar ventas: {ex.Message}");
            }
        }

        [HttpGet("productos_vendidos")]
        public ActionResult<IEnumerable<Modelos.productos_vendidos>> GetProductosVendidos(int suc = 0, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            return Ok(sql.productos_vendidos(suc, fechaInicio, fechaFin));
        }
        [HttpGet("top-ventas-productos")]
        public ActionResult<IEnumerable<Modelos.productos_vendidos>> GetTopVentasProductos(int suc = 0, int cantidad = 10, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            return Ok(sql.top_venta_producto(suc, cantidad, fechaInicio, fechaFin));
        }

        [HttpGet("por-tipo-pago")]
        public ActionResult<IEnumerable<Modelos.ventas_por_tipo_pago>> GetVentasPorTipoPago(int suc = 0, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            return Ok(sql.ventas_por_tipo_pago(suc, fechaInicio, fechaFin));
        }
    }
}
