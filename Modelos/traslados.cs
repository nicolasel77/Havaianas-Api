namespace Havaianas_Api.Modelos
{
    public class traslados
    {
        public int id { get; set; }
        public DateTime fecha { get; set; }
        public int id_producto { get; set; }
        public string nombre_producto { get; set; } = string.Empty;
        public double cantidad { get; set; }
        public int id_sucursal_origen { get; set; }
        public string nombre_sucursal_origen { get; set; } = string.Empty;
        public int id_sucursal_destino { get; set; }
        public string nombre_sucursal_destino { get; set; } = string.Empty;
        public string observaciones { get; set; } = string.Empty;
        public int estado { get; set; }
    }
}
