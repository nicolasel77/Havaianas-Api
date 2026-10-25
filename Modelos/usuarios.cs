namespace Havaianas_Api.Modelos
{
    public class usuarios
    {
        public int id { get; set; }
        public string usuario { get; set; } = "";
        public string nombre { get; set; } = "";
        public string password_hash { get; set; } = "";
        public int? id_sucursal { get; set; }
        public string sucursal { get; set; } = "";
        public bool activo { get; set; }
    }

    public record LoginRequest(string Usuario, string Password);

    public record LoginResponse(string Token, DateTime Expira, string Usuario, string Nombre, int? IdSucursal, string Sucursal);
}
