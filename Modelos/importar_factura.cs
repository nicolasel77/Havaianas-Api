namespace Havaianas_Api.Modelos
{
    public class ImportarFactura
    {
        public DateTime fecha { get; set; }
        public bool check { get; set; }
        public List<LineaFactura> lineas { get; set; } = new();
    }

    public class LineaFactura
    {
        public int cod_art { get; set; }
        public double cantidad { get; set; }
        public double precio { get; set; }
    }

    public class ResultadoImportacionFactura
    {
        public int ingresos_creados { get; set; }
        public List<string> errores { get; set; } = new();
    }
}
