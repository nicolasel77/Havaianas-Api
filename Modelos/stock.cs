namespace Havaianas_Api.Modelos
{
    public class stock
    {
        public DateTime fecha { get; set; }
        public int sucursal { get; set; }
        public string ? nombre { get; set; }
        public int producto { get; set; }
        public string ? Descripcion { get; set; }
        public double cantidad { get; set; }
        public double importe { get; set; }
    }
}