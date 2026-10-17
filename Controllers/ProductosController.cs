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

        // GET /Productos?todos=false
        // Catálogo maestro de productos. Las sucursales lo descargan de acá: el servidor es el único
        // que da de alta productos. todos = true incluye los inactivos (duplicados ya unificados).
        [HttpGet]
        public ActionResult<IEnumerable<productos>> Get(bool todos = false)
        {
            productos[] productos = sql.obtener_productos(todos);
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
            int nid = sql.agregar_producto(nuevoProducto, out productos? duplicado);
            if (duplicado != null)
            {
                return Conflict(new { message = $"Ya existe el producto {duplicado.id}: {duplicado.descripcion_codigo} {duplicado.descripcion_color} {duplicado.talles}", producto = duplicado });
            }
            return Ok(nid);
        }

        [HttpPut("{id:int}")]
        public ActionResult Put(int id, [FromBody] productos producto)
        {
            producto.id = id;
            bool existe = sql.editar_producto(producto, out productos? duplicado);
            if (duplicado != null)
            {
                return Conflict(new { message = $"Ya existe el producto {duplicado.id}: {duplicado.descripcion_codigo} {duplicado.descripcion_color} {duplicado.talles}", producto = duplicado });
            }
            if (!existe)
            {
                return NotFound();
            }
            return Ok(new { message = "Producto editado correctamente" });
        }

        // Baja lógica (Activo = 0): se conservan las ventas, ingresos y traslados del producto
        [HttpDelete("{id:int}")]
        public ActionResult Delete(int id)
        {
            if (!sql.desactivar_producto(id))
            {
                return NotFound();
            }
            return Ok(new { message = "Producto dado de baja" });
        }

        // POST /Productos/Equivalencias/{suc}
        // La sucursal manda su listado de productos (mismo formato que GET /Productos) y el servidor
        // los vincula con los suyos (por EAN, Cod_Art + color + talle o descripción + color + talle).
        // Las ventas que manda la sucursal se traducen con ese vínculo. Devuelve los que no se pudieron vincular.
        [HttpPost("Equivalencias/{suc:int}")]
        public ActionResult<IEnumerable<productos>> PostEquivalencias(int suc, [FromBody] List<productos> productosSucursal)
        {
            if (productosSucursal == null || productosSucursal.Count == 0)
            {
                return BadRequest("No se recibieron productos.");
            }
            List<productos> sinVincular = sql.equivalencias_sucursal(suc, productosSucursal);
            return Ok(new { recibidos = productosSucursal.Count, sin_vincular = sinVincular });
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