namespace Havaianas_Api.Modelos
{
    // Una parte del pago de una venta cobrada con varias formas de pago (pago combinado)
    public class pago_venta
    {
        public int forma_pago { get; set; }
        public decimal importe { get; set; }
    }
}
