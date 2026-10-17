namespace Havaianas_Api.Modelos
{
    public class stock
    {
        public DateTime fecha { get; set; }
        public int sucursal { get; set; }
        public string? nombre { get; set; }
        public int producto { get; set; }
        public string? Descripcion { get; set; }
        public decimal cantidad { get; set; }
        public decimal importe { get; set; }
        public string talle { get; set; } = string.Empty;
        public string genero { get; set; } = string.Empty;
        public int color { get; set; }
        public string descripcion_color { get; set; } = string.Empty;
    }
}