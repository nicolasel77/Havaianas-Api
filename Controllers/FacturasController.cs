// FacturasController.cs
//
// Endpoints ASP.NET Core Web API para el flujo:
//   1) Subir PDF -> parsear -> guardar en estado "Pendiente" + devolver preview
//   2) Editar/confirmar mapeos manuales de líneas sin match
//   3) Confirmar ingreso -> aplica movimiento de stock
//
// Depende de:
//   - GunarInvoiceParser (parser PdfPig del mensaje anterior)
//   - Tablas creadas en 01_tablas_ingreso_mercaderia.sql
//   - Un DbContext (acá lo llamo AppDbContext, ajustalo al tuyo)
//
using Havaianas.Facturacion.Parsing;
using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Havaianas.Facturacion.Controllers
{
    [ApiController]
    [Route("facturas")]
    public class FacturasController : ControllerBase
    {
        private readonly sql_db sql = new sql_db();
        private readonly IWebHostEnvironment _env;

        public FacturasController(IWebHostEnvironment env)
        {
            _env = env;
        }

        // -----------------------------------------------------------------
        // POST /api/facturas/importar
        // Sube el PDF, lo parsea, y lo persiste en estado "Pendiente" junto
        // con el resultado del mapeo automático. No toca stock todavía.
        // -----------------------------------------------------------------
        [HttpPost("importar")]
        public async Task<IActionResult> Importar(IFormFile pdf, [FromQuery] int proveedorId)
        {
            if (pdf == null || pdf.Length == 0)
                return BadRequest(new { error = "No se recibió ningún archivo." });

            var carpetaTemp = Path.Combine(_env.ContentRootPath, "App_Data", "facturas_temp");
            Directory.CreateDirectory(carpetaTemp);
            var rutaTemp = Path.Combine(carpetaTemp, $"{Guid.NewGuid()}.pdf");

            await using (var stream = System.IO.File.Create(rutaTemp))
            {
                await pdf.CopyToAsync(stream);
            }

            FacturaParseada facturaParseada;
            try
            {
                facturaParseada = GunarInvoiceParser.Parse(rutaTemp);
            }
            catch (Exception ex)
            {
                return UnprocessableEntity(new
                {
                    error = "No se pudo parsear el PDF. Puede que el formato haya cambiado.",
                    detalle = ex.Message
                });
            }

            var (validacionOk, validacionDetalle) = facturaParseada.Validar();

            var numeroFactura = NormalizarNumeroFactura(facturaParseada.Header.Numero);
            if (numeroFactura == null)
            {
                return UnprocessableEntity(new
                {
                    error = "No se encontró el número de factura en el PDF.",
                    detalle = "Verificá que el PDF corresponda al formato esperado y que contenga el número de comprobante."
                });
            }

            if (facturaParseada.Productos.Count == 0)
            {
                return UnprocessableEntity(new
                {
                    error = "No se detectaron productos en la factura.",
                    detalle = "El PDF puede requerir OCR o tener un formato no compatible."
                });
            }

            // Evitar duplicados: si ya existe una factura con ese número para este proveedor
            var yaExiste = await sql.ExisteIngresoMercaderiaAsync(proveedorId, numeroFactura);
            if (yaExiste)
            {
                //return Conflict(new { error = $"La factura {numeroFactura} ya fue importada." });
                //Borrar la factura existente
                sql.BorrarIngresoMercaderia(proveedorId, numeroFactura);
            }

            var ingreso = new IngresoMercaderia
            {
                ProveedorId = proveedorId,
                NroFactura = numeroFactura,
                FechaFactura = ParseFechaAr(facturaParseada.Header.Fecha),
                CuitCliente = facturaParseada.Header.CuitCliente,
                Remito = facturaParseada.Header.Remito,
                CondicionPago = facturaParseada.Header.CondicionPago,
                SucursalDestino = facturaParseada.Header.Sucursal,
                TotalCv = facturaParseada.Totales.TotalCv,
                TotalUnidades = facturaParseada.Totales.TotalUnidades,
                Subtotal = facturaParseada.Totales.Subtotal,
                Iva = facturaParseada.Totales.Iva,
                PercepcionIibb = facturaParseada.Totales.PercepcionIibb,
                TotalFactura = facturaParseada.Totales.TotalFactura,
                ValidacionOk = validacionOk,
                ValidacionDetalle = validacionDetalle,
                Estado = "Pendiente",
                ArchivoOriginalPath = rutaTemp,
                Detalle = facturaParseada.Productos.Select(p => new IngresoMercaderiaDetalle
                {
                    CodigoProductoProveedor = p.CodigoProductoProveedor,
                    CodigoModelo = p.CodigoModelo,
                    NombreModelo = p.NombreModelo,
                    Marca = p.Marca,
                    Material = p.Material,
                    Color = p.Color,
                    TalleDesde = p.TalleDesde,
                    TalleHasta = p.TalleHasta,
                    CantidadCv = p.CantidadCv,
                    UnidadesPorCv = p.UnidadesPorCv,
                    CantidadUnidades = p.CantidadUnidades,
                    PrecioUnitario = p.PrecioUnitario,
                    BonificacionPct = p.BonificacionPct,
                    Importe = p.Importe,
                }).ToList()
            };

            sql.IngresoMercaderia.Add(ingreso);
            await sql.SaveChangesAsync();

            // Resolver mapeo automático contra MapeoProductoProveedor
            await sql.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_ResolverMapeoIngreso @IngresoId = {ingreso.Id}, @ProveedorId = {proveedorId}");

            return Ok(await ObtenerPreview(ingreso.Id));
        }

        // -----------------------------------------------------------------
        // GET /api/facturas/{id}/preview
        // Devuelve la factura con el estado de mapeo de cada línea, para la
        // pantalla de revisión antes de confirmar.
        // -----------------------------------------------------------------
        [HttpGet("{id}/preview")]
        public async Task<IActionResult> Preview(int id)
        {
            var preview = await ObtenerPreview(id);
            return preview is null ? NotFound() : Ok(preview);
        }

        private async Task<object?> ObtenerPreview(int ingresoId)
        {
            var ingreso = sql.obtener_ingreso_con_detalle(ingresoId);

            if (ingreso == null) return null;

            return new
            {
                ingreso.Id,
                ingreso.NroFactura,
                ingreso.FechaFactura,
                ingreso.SucursalDestino,
                ingreso.TotalFactura,
                ingreso.ValidacionOk,
                ingreso.ValidacionDetalle,
                ingreso.Estado,
                LineasPendientesRevision = ingreso.Detalle.Count(d => d.RequiereRevision),
                Lineas = ingreso.Detalle.Select(d => new
                {
                    d.Id,
                    d.CodigoProductoProveedor,
                    d.CodigoModelo,
                    d.NombreModelo,
                    d.Color,
                    Talle = $"{d.TalleDesde} - {d.TalleHasta}",
                    d.CantidadUnidades,
                    d.Importe,
                    d.ProductoId,
                    d.MapeoEncontrado,
                    d.RequiereRevision
                })
            };
        }

        // -----------------------------------------------------------------
        // PUT /api/facturas/{id}/lineas/{detalleId}/mapear
        // Asigna manualmente un SKU interno a una línea sin match, y guarda
        // el mapeo para que las próximas facturas de ese código/color/talle
        // se resuelvan solas.
        // -----------------------------------------------------------------
        public record MapearLineaRequest(int ProductoId, string Usuario);

        [HttpPut("{id}/lineas/{detalleId}/mapear")]
        public async Task<IActionResult> MapearLinea(int id, int detalleId, [FromBody] MapearLineaRequest req)
        {
            IngresoMercaderia ingreso = sql.obtener_ingreso(id);
            if (ingreso == null) return NotFound();

            IngresoMercaderiaDetalle linea = sql.obtener_ingreso_detalle(detalleId);
            if (linea == null) return NotFound(new { error = "Línea no encontrada." });

            // Actualizar la línea de detalle
            sql.actualizar_linea_detalle(detalleId, req.ProductoId);

            // Guardar el mapeo para próximas facturas
            var talle = $"{linea.TalleDesde} - {linea.TalleHasta}";
            var existeMapeo = sql.existe_mapeo_producto(ingreso.ProveedorId, linea.CodigoProductoProveedor, linea.Color ?? "", talle);

            if (!existeMapeo)
            {
                sql.MapeosProductoProveedor.Add(new MapeoProductoProveedor
                {
                    ProveedorId = ingreso.ProveedorId,
                    CodigoProductoProveedor = linea.CodigoProductoProveedor,
                    Color = linea.Color ?? "",
                    Talle = talle,
                    ProductoId = req.ProductoId,
                    ArticuloDescripcion = $"{linea.CodigoModelo} {linea.NombreModelo}",
                    UsuarioMapeo = req.Usuario
                });
                await sql.SaveChangesAsync();
            }

            return Ok(await ObtenerPreview(id));
        }

        // -----------------------------------------------------------------
        // POST /api/facturas/{id}/confirmar
        // Aplica el ingreso de stock. Falla si quedan líneas sin mapear.
        // -----------------------------------------------------------------
        public record ConfirmarRequest(int SucursalDestinoId, string Usuario);

        [HttpPost("{id}/confirmar")]
        public async Task<IActionResult> Confirmar(int id, [FromBody] ConfirmarRequest req)
        {
            var pendientes = sql.contar_lineas_sin_producto(id);

            if (pendientes > 0)
            {
                return BadRequest(new
                {
                    error = $"Quedan {pendientes} línea(s) sin producto asignado. Completá el mapeo antes de confirmar."
                });
            }

            try
            {
                await sql.Database.ExecuteSqlInterpolatedAsync(
                    $"EXEC sp_ConfirmarIngresoMercaderia @IngresoId = {id}, @SucursalDestinoId = {req.SucursalDestinoId}, @Usuario = {req.Usuario}");
            }
            catch (Exception ex)
            {
                return UnprocessableEntity(new { error = ex.Message });
            }

            return Ok(await ObtenerPreview(id));
        }

        private static DateTime ParseFechaAr(string? fecha)
        {
            if (fecha != null && DateTime.TryParseExact(fecha, "dd/MM/yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
            {
                return dt;
            }
            return DateTime.UtcNow.Date;
        }

        private static string? NormalizarNumeroFactura(string? numero)
        {
            if (string.IsNullOrWhiteSpace(numero)) return null;

            var normalizado = System.Text.RegularExpressions.Regex.Replace(numero.Trim(), @"\s*[-/]\s*", "-");
            return System.Text.RegularExpressions.Regex.IsMatch(normalizado, @"^\d{3,5}-\d{4,8}$")
                ? normalizado
                : null;
        }
    }
}
