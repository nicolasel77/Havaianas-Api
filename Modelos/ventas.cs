using System.Security.Cryptography;

namespace Havaianas_Api.Modelos
{
    public class ventas
    {
        public int id { get; set; }
        public int id_sucursal { get; set; }
        public string nombre_sucursal { get; set; }
        public int id_producto { get; set; }
        public string nombre_producto { get; set; }
        public int numero { get; set; }
        public DateTime fecha { get; set; }
        public int tipo_factura { get; set; }
        public string desc_factura { get; set; }
        public int cliente { get; set; }
        public string nombre_cliente { get; set; }
        public int vendedor { get; set; }
        public string nombre_vendedor { get; set; }
        public int forma_pago { get; set; }
        public string desc_formaPago { get; set; }
        public double subTotal { get; set; }
        public double iva { get; set; }
        public double descuento { get; set; }
        public double total { get; set; }
        public string observaciones { get; set; }
        public int estado { get; set; }
        public string CAE { get; set; }
        public DateTime hora { get; set; }
        public decimal cantidad { get; set; }
        public decimal precio { get;  set; }
        public int id_venta_sucursal { get; set; }
    }
}
