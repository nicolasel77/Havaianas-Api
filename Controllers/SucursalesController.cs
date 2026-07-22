using Havaianas_Api.Datos;
using Microsoft.AspNetCore.Mvc;

namespace Havaianas_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SucursalesController : Controller
    {

        private readonly sql_db sql = new sql_db();

        //get
        [HttpGet]
        public ActionResult<IEnumerable<Modelos.sucursales>> Get()
        {
            Modelos.sucursales[] sucursales = sql.obtener_sucursales();
            return Ok(sucursales);
        }
    }
}
