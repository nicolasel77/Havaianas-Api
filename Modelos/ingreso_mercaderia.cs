namespace Havaianas.Facturacion
{
    public class IngresoMercaderia
    {
        public int Id { get; set; }
        public int ProveedorId { get; set; }
        public string NroFactura { get; set; } = string.Empty;
        public DateTime FechaFactura { get; set; }
        public string? CuitCliente { get; set; }
        public string? Remito { get; set; }
        public string? CondicionPago { get; set; }
        public string? SucursalDestino { get; set; }
        public int? SucursalDestinoId { get; set; }
        public int TotalCv { get; set; }
        public int TotalUnidades { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Iva { get; set; }
        public decimal PercepcionIibb { get; set; }
        public decimal TotalFactura { get; set; }
        public bool ValidacionOk { get; set; }
        public string? ValidacionDetalle { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string? ArchivoOriginalPath { get; set; }
        public DateTime FechaImportacion { get; set; }
        public DateTime? FechaConfirmacion { get; set; }
        public string? UsuarioConfirmacion { get; set; }
        public List<IngresoMercaderiaDetalle> Detalle { get; set; } = new();
    }

    public class IngresoMercaderiaDetalle
    {
        public int Id { get; set; }
        public int IngresoId { get; set; }
        public string CodigoProductoProveedor { get; set; } = string.Empty;
        public string? CodigoModelo { get; set; }
        public string? NombreModelo { get; set; }
        public string? Marca { get; set; }
        public string? Material { get; set; }
        public string? Color { get; set; }
        public string? TalleDesde { get; set; }
        public string? TalleHasta { get; set; }
        public int CantidadCv { get; set; }
        public int UnidadesPorCv { get; set; }
        public int CantidadUnidades { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal BonificacionPct { get; set; }
        public decimal Importe { get; set; }
        public int? ProductoId { get; set; }
        public bool MapeoEncontrado { get; set; }
        public bool RequiereRevision { get; set; }
        public DateTime FechaFactura { get; set; }
        public decimal Total { get; set; }
        public bool Check { get; set; }

    }

    public class MapeoProductoProveedor
    {
        public int Id { get; set; }
        public int ProveedorId { get; set; }
        public string CodigoProductoProveedor { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string Talle { get; set; } = string.Empty;
        public int ProductoId { get; set; }
        public string? ArticuloDescripcion { get; set; }
        public string? UsuarioMapeo { get; set; }
    }
}
