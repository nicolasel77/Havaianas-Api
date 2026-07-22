using Microsoft.AspNetCore.Mvc;
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
        [HttpGet("{id}")]
        public ActionResult<productos> Get(string id)
        {
            productos producto = sql.obtener_producto(id);
            
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
       
    }    
}