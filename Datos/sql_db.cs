using Havaianas_Api.Modelos;
using Microsoft.Data.SqlClient;
namespace Havaianas_Api.Datos
{    
    public class sql_db
    {
        SqlConnection sql = new SqlConnection("Data Source=192.168.1.11;Initial Catalog=dbHavaianas;User Id=Nikorasu;Password=Oficina02;TrustServerCertificate=True;");
        // funcion para obtener todos los productos
        public Modelos.productos[] obtener_productos()
        {            
            sql.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([Codigo_Equis], 0), ISNULL([Cod_Art], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Categoria], ''), ISNULL([Codigo_Color], 0), ISNULL([Descripcion_Color], ''), ISNULL([Talles], ''), ISNULL([Ean_2025], 0) FROM productos", sql))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<productos> productosList = new List<productos>();
                    while (reader.Read())
                    {
                        productos prod = new productos
                        {
                            id = reader.GetInt32(0),
                            codigo_equis = reader.GetInt64(1),
                            cod_art = reader.GetInt32(2),
                            descripcion_codigo = reader.GetString(3),
                            categoria = reader.GetString(4),
                            codigo_color = reader.GetInt32(5),
                            descripcion_color = reader.GetString(6),
                            talles = reader.GetString(7),
                            ean_2025 = reader.GetInt64(8)
                        };
                        productosList.Add(prod);
                    }
                    return productosList.ToArray();
                }            
            }
        }

        //Agregar un nuevo producto
        public Modelos.productos agregar_producto(Modelos.productos nuevo_producto)
        {
            // En un escenario real, aquí se insertaría el nuevo producto en la base de datos.
            // Para este ejemplo, simplemente devolvemos el producto con un ID simulado.
            nuevo_producto.id = new Random().Next(1000, 9999); // Simular la asignación de un ID único
            return nuevo_producto;
        }
    }
}
