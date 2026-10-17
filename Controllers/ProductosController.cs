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
        [HttpGet("Busqueda")]
        public ActionResult<IList<productos>> GetBusqueda(string nombre, bool todos = false)
        {
            IList<productos> productos = sql.obtener_productos(nombre, todos);
            
            if (productos == null || !productos.Any())
            {
                return NotFound();
            }
            return Ok(productos);
        }       
        [HttpGet("codart")]
        public ActionResult<long> GetCodArt(string nombre)
        {
            long producto = sql.obtener_codart(nombre);
            
            if (producto == 0)
            {
                return NotFound();
            }
            return Ok(producto);
        }
        [HttpGet("idcolor")]
        public ActionResult<int> GetIdColor(string nombre)
        {
            int producto = sql.obtener_idcolor(nombre);

            if (producto == 0)
            {
                return NotFound();
            }
            return Ok(producto);
        }
        [HttpPost]
        public ActionResult<productos> Post([FromBody] productos nuevoProducto)
        {
            int nid = sql.agregar_producto(nuevoProducto);
            return Ok(nid);
        }

        // Obtener el proximo orden
        [HttpGet("ProximoOrden")]
        public ActionResult<int> GetProximoOrden()
        {
            int proximoOrden = sql.obtener_proximo_orden();
            return Ok(proximoOrden);
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