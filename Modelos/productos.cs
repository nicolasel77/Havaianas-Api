namespace Havaianas_Api.Modelos
{
    public class productos
    {
        public int id { get; set; }
        public int orden { get; set; }
        public long codigo_equis { get; set; }
        public int cod_art { get; set; }
        public string descripcion_codigo { get; set; }
        public string categoria { get; set; }
        public int codigo_color { get; set; }
        public string descripcion_color { get; set; }
        public string talles { get; set; }
        public long ean_2025 { get; set; }
        public string? genero { get; set; }
        public string? curva { get; set; }

    }
}
