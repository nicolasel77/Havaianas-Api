namespace Havaianas_Api.Modelos
{
    public class productos_vendidos
    {
        public long ProductoId { get; set; }
        public string NombreModelo { get; set; }
        public int CantidadUnidades { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Total { get; set; } = 0;
    }
}