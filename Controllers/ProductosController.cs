using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Havaianas_Api.Modelos;
using Havaianas_Api.Datos;
using Microsoft.AspNetCore.Authorization;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ProductosController : ControllerBase
    {
        private readonly sql_db sql = new sql_db();

        [HttpGet]
        public ActionResult<IEnumerable<productos>> Get()
        {
            productos[] productos = sql.obtener_productos();
            return Ok(productos);
        }
        [HttpGet("{nombre}")]
        public ActionResult<productos> Get(string nombre)
        {
            productos producto = sql.obtener_producto(nombre);
            
            if (producto == null)
            {
                return NotFound();
            }
            return Ok(producto);
        }       

        [HttpPost, Authorize]
        public ActionResult<productos> Post([FromBody] productos nuevoProducto)
        {
            // Aqu solo se simula el guardado, ya que productos_db no persiste datos.
            // En un escenario real, deberías agregar el producto a una base de datos.
            return CreatedAtAction(nameof(Post), new { id = nuevoProducto.id }, nuevoProducto);
        }

        // Obtener listado de Generos
        [HttpGet("Generos")]
        public IEnumerable<string> GetGeneros()
        {
            return sql.obtener_generos();
        }
        // Obtener listado de colores
        [HttpGet("Colores")]
        public IEnumerable<string> GetColores()
        {
            return sql.obtener_colores();
        }
        // Obtener listado de talles
        [HttpGet("Talles")]
        public IEnumerable<string> GetTalles()
        {
            return sql.obtener_talles();
        }
        
        // Obtener listado de nombres
        [HttpGet("Nombres")]
        public IEnumerable<string> GetNombres()
        {
            return sql.obtener_nombres();
        }

        // Obtener listado de productos por codigo equis
        [HttpGet("PorCodigoEquis/{codigo}")]
        public ActionResult<IEnumerable<productos>> GetProductos(long codigo)
        {
            var productos = sql.obtener_por_equis(codigo);
            if (productos == null || !productos.Any())
            {
                return NotFound();
            }
            return Ok(productos);
        }

        // Obtener producto por ean
        [HttpGet("PorEan/{codigo}")]
        public ActionResult<productos> GetProductoEan(long codigo)
        {
            var producto = sql.obtener_por_ean(codigo);
            if (producto == null )
            {
                return NotFound();
            }
            return Ok(producto);
        }
    }    
}