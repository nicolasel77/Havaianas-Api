namespace Havaianas_Api.Modelos
{
    public class Ingresos
    {
        public int id { get; set; }                
        public DateTime fecha { get; set; }
        public int id_producto { get; set; }
        public string nombre_producto { get; set; }        
        public double total { get; set; }
        public decimal cantidad { get; internal set; }
        public decimal precio { get; internal set; }
    }
}
