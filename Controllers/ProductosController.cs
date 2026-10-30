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

        // Recibe todos los productos de la base local de una sucursal (para unificar IDs)
        [HttpPost("Sucursal/{idSucursal}")]
        [RequestSizeLimit(50_000_000)]
        public ActionResult PostProductosSucursal(int idSucursal, [FromBody] List<productos_sucursal> productos)
        {
            if (idSucursal <= 0 || productos == null || productos.Count == 0)
            {
                return BadRequest("No se recibieron productos.");
            }
            try
            {
                sql.guardar_productos_sucursal(idSucursal, productos);
                return Ok(productos.Count);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al guardar productos: {ex.Message}");
            }
        }

        // Productos activos del servidor (lo descargan las sucursales)
        [HttpGet("Catalogo")]
        public ActionResult<IEnumerable<productos_sucursal>> GetCatalogo()
        {
            return Ok(sql.obtener_catalogo());
        }

        // Productos unificados: ID viejo -> ID del producto que quedo
        [HttpGet("Unificados")]
        public ActionResult<IEnumerable<mapeo_id>> GetUnificados()
        {
            return Ok(sql.obtener_unificados());
        }

        // ID local -> ID central de los productos de una sucursal
        [HttpGet("Mapeo/{idSucursal}")]
        public ActionResult<IEnumerable<mapeo_id>> GetMapeo(int idSucursal)
        {
            return Ok(sql.obtener_mapeo(idSucursal));
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