using Havaianas_Api.Modelos;
using Microsoft.Data.SqlClient;
using System.Numerics;
namespace Havaianas_Api.Datos
{
    public class sql_db
    {
        SqlConnection sql = new SqlConnection("Data Source=NICO;Initial Catalog=dbHavaianas_servidor;Trusted_Connection=True;TrustServerCertificate=True;");
        //SqlConnection sql = new SqlConnection("Data Source=192.168.1.11;Initial Catalog=dbHavaianas_servidor;User Id=Nikorasu;Password=Oficina02;TrustServerCertificate=True;");
        // funcion para obtener todos los productos
        public Modelos.productos[] obtener_productos()
        {
            sql.Open();
            string comando = "SELECT [ID], ISNULL([Codigo_Equis], 0), ISNULL([Cod_Art], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Categoria], '')" +
                ", ISNULL([Codigo_Color], 0), ISNULL([Descripcion_Color], ''), ISNULL([Talles], ''), ISNULL([Ean_2025], 0) FROM productos";

            using (SqlCommand cmd = new SqlCommand(comando, sql))
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



        // sucursales
        public Modelos.sucursales[] obtener_sucursales()
        {
            sql.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([Nombre], ''), ISNULL([Direccion], '') FROM sucursales", sql))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<sucursales> sucursalesList = new List<sucursales>();
                    while (reader.Read())
                    {
                        sucursales sucursal = new sucursales
                        {
                            id = reader.GetInt32(0),
                            nombre = reader.GetString(1),
                            direccion = reader.GetString(2)
                        };
                        sucursalesList.Add(sucursal);
                    }
                    return sucursalesList.ToArray();
                }
            }
        }

        internal bool agregar_ingreso(IngresoMercaderia ingreso)
        {
            sql.Open();
            bool resultado = false;
            using (SqlCommand cmd = new SqlCommand("INSERT INTO compras (Fecha, ID_Producto, Cantidad, Precio) " +
                "VALUES (@fecha, @id_producto, @cantidad, @precio)", sql))
            {
                cmd.Parameters.AddWithValue("@fecha", ingreso.fecha);
                cmd.Parameters.AddWithValue("@id_producto", ingreso.id_producto);
                cmd.Parameters.AddWithValue("@cantidad", ingreso.cantidad);
                cmd.Parameters.AddWithValue("@precio", ingreso.precio);
                cmd.ExecuteNonQuery();
                resultado = true;
            }
            return resultado;
        }

        internal bool editar_ingreso(IngresoMercaderia ingreso)
        {
            sql.Open();
            bool resultado = false;
            using (SqlCommand cmd = new SqlCommand("UPDATE compras SET Fecha=@fecha, ID_Producto=@producto, Cantidad=@cantidad, Precio=@precio WHERE ID=@id"
                    , sql))
            {
                cmd.Parameters.AddWithValue("@id", ingreso.id);
                cmd.Parameters.AddWithValue("@fecha", ingreso.fecha);
                cmd.Parameters.AddWithValue("@producto", ingreso.id_producto);
                cmd.Parameters.AddWithValue("@cantidad", ingreso.cantidad);
                cmd.Parameters.AddWithValue("@precio", ingreso.precio);
                cmd.ExecuteNonQuery();
                resultado =true;

            }
            return resultado;
        }

        internal IngresoMercaderia obtener_ingreso(int id)
        {
            sql.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([Fecha], GETDATE()), ISNULL([ID_Producto], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Total], 0)" +
                ", ISNULL([Cantidad], 0), ISNULL([Precio], 0) FROM vw_ingresos WHERE [ID] = @id", sql))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        IngresoMercaderia ingreso = new IngresoMercaderia
                        {
                            id = reader.GetInt32(0),
                            fecha = reader.GetDateTime(1),
                            id_producto = reader.GetInt32(2),
                            nombre_producto = reader.GetString(3),
                            total = reader.GetDouble(4),
                            cantidad = (double)reader.GetSqlDouble(5),
                            precio = (double)reader.GetSqlDouble(6)
                        };
                        return ingreso;
                    }
                }
            }
            return null;
        }

        internal IngresoMercaderia[] obtener_ingresos(DateTime fecha, DateTime fechaFin)
        {
            sql.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([Fecha], GETDATE()), ISNULL([ID_Producto], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Total], 0)" +
                ", ISNULL([Cantidad], 0), ISNULL([Precio], 0) FROM vw_ingresos WHERE CAST([Fecha] AS DATE) BETWEEN @fechaInicio AND @fechaFin", sql))
            {
                cmd.Parameters.AddWithValue("@fechaInicio", fecha);
                cmd.Parameters.AddWithValue("@fechaFin", fechaFin);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<IngresoMercaderia> ingresosList = new List<IngresoMercaderia>();
                    while (reader.Read())
                    {
                        IngresoMercaderia ingreso = new IngresoMercaderia
                        {
                            id = reader.GetInt32(0),
                            fecha = reader.GetDateTime(1),
                            id_producto = reader.GetInt32(2),
                            nombre_producto = reader.GetString(3),
                            total = reader.GetDouble(4),
                            cantidad = (double)reader.GetSqlDouble(5),
                            precio = (double)reader.GetSqlDouble(6)
                        };
                        ingresosList.Add(ingreso);
                    }
                    return ingresosList.ToArray();
                }
            }
        }

        internal productos obtener_producto(string id)
        {
            sql.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 [ID], ISNULL([Codigo_Equis], 0), ISNULL([Cod_Art], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Categoria], '')" +
                ", ISNULL([Codigo_Color], 0), ISNULL([Descripcion_Color], ''), ISNULL([Talles], ''), ISNULL([Ean_2025], 0) FROM productos " +
                "WHERE Descripcion_Codigo LIKE '%' + @id + '%' OR CONVERT(varchar, EAN_2025)=@id OR CONVERT(varchar, Codigo_Equis)=@id", sql))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
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
                        return prod;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Función para obtener el stock de productos en una sucursal específica y en una fecha determinada
        /// </summary>
        /// <param name="suc"></param>
        /// <param name="fecha"></param>
        /// <returns></returns>
        internal IList<Modelos.stock> stock(int suc, DateTime? fecha, int producto = 0)
        {
            // Llamar al store procedure "sp_stock" con los parámetros "suc" y "fecha"
            sql.Open();
            using (SqlCommand cmd = new SqlCommand("sp_stock", sql))
            {
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@suc", suc);
                cmd.Parameters.AddWithValue("@fecha", fecha.HasValue ? fecha.Value : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@producto", producto);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<stock> stockList = new List<stock>();
                    while (reader.Read())
                    {
                        stock st = new stock
                        {
                            sucursal = reader.GetInt32(reader.GetOrdinal("sucursal")),
                            nombre = reader.GetString(reader.GetOrdinal("nombre")),
                            producto = reader.GetInt32(reader.GetOrdinal("ID")),
                            Descripcion = reader.GetString(reader.GetOrdinal("Descripcion_Codigo")),
                            cantidad = reader.GetDouble(reader.GetOrdinal("cantidadstock")),
                            importe = reader.GetDouble(reader.GetOrdinal("totalstock"))
                        };
                        stockList.Add(st);
                    }
                    return stockList;
                }
            }
        }

        /// <summary>
        /// Función para obtener las ventas con filtros opcionales por sucursal, fecha, agrupado por día, producto o sucursal
        /// </summary>
        /// <param name="suc">ID de la sucursal (opcional, por defecto 0 para todas las sucursales)</param>
        /// <param name="fechaInicio">Fecha de inicio para filtrar las ventas (opcional)</param>
        /// <param name="fechaFin">Fecha de fin para filtrar las ventas (opcional)</param>
        /// <param name="agDia">Agregar agrupación por día (opcional, por defecto false)</param>
        /// <param name="agProd">Agregar agrupación por producto (opcional, por defecto false)</param>
        /// <param name="agSuc">Agregar agrupación por sucursal (opcional, por defecto false)</param>
        /// <returns></returns>
        internal Modelos.ventas[] ventas(int suc = 0, DateTime? fechaInicio = null, DateTime? fechaFin = null, bool agDia = false, bool agProd = false, bool agSuc = false)
        {

            string filtro = " WHERE 1=1";
            if (suc > 0)
            {
                filtro += $" AND ID_Sucursal = {suc}";
            }
            if (fechaInicio != null)
            {
                filtro += $" AND Fecha >= '{fechaInicio.Value:yyyy-MM-dd}'";
            }
            if (fechaFin != null)
            {
                filtro += $" AND Fecha <= '{fechaFin.Value:yyyy-MM-dd}'";
            }
            string groupBy = "";
            if (agDia)
            {
                groupBy = " Fecha";
            }
            if (agProd)
            {
                groupBy = agDia ? " Fecha, ID_Producto, nombre_producto" : " ID_Producto, nombre_producto";
            }
            if (agSuc)
            {
                groupBy = (agDia || agProd) ? groupBy + ", ID_Sucursal, nombre_sucursal" : " ID_Sucursal, nombre_sucursal";
            }

            string comando = $"SELECT [ID], ISNULL([ID_Sucursal], 0), nombre_sucursal, ISNULL([ID_Producto], 0), nombre_producto" +
                ", ISNULL([Cantidad], 0), isnull(precio, 0), ISNULL([Numero], 0)" +
                ", ISNULL([Fecha], GETDATE()) fecha, ISNULL([Tipo_Factura], 0), ISNULL([Vendedor], 0), ISNULL([Forma_Pago], 0)" +
                ", cast( (cantidad*precio) as float) As subtotal,CAST(ISNULL([Descuento], 0) AS FLOAT)" +
                ", ISNULL([Observaciones], ''), ISNULL([Estado], 0), ISNULL([CAE], '')" +
                ", ISNULL([Hora], GETDATE()) FROM vw_ventas" + filtro;

            if (groupBy != "")
            {
                comando = "SELECT " +
                    $"{groupBy}, SUM(ISNULL([Cantidad], 0)) cantidad" +
                    ", SUM(cast((cantidad*precio) as float)) As subtotal, SUM(CAST(ISNULL([Descuento], 0) AS FLOAT)) descuento" +
                    $" FROM vw_ventas {filtro} GROUP BY {groupBy}";
            }
            sql.Open();
            using (SqlCommand cmd = new SqlCommand(comando, sql))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<ventas> ventasList = new List<ventas>();
                    while (reader.Read())
                    {
                        if (groupBy == "")
                        {
                            ventas venta = new ventas
                            {
                                id = reader.GetInt32(0),
                                id_sucursal = reader.GetInt32(1),
                                nombre_sucursal = reader.GetString(2),
                                id_producto = reader.GetInt32(3),
                                nombre_producto = reader.GetString(4),
                                cantidad = reader.GetDecimal(5),
                                precio = reader.GetDecimal(6),
                                numero = reader.GetInt32(7),
                                fecha = reader.GetDateTime(8),
                                tipo_factura = reader.GetInt32(9),
                                vendedor = reader.GetInt32(10),
                                forma_pago = reader.GetInt32(11),
                                subTotal = reader.GetDouble(12),
                                descuento = reader.GetDouble(13),
                                observaciones = reader.GetString(14),
                                estado = reader.GetInt32(15),
                                CAE = reader.GetString(16),
                                hora = reader.GetDateTime(17)
                            };

                            venta.total = venta.subTotal - venta.descuento;
                            ventasList.Add(venta);
                        }
                        else
                        {
                            ventas venta = new ventas
                            {
                                id = 0,
                                id_sucursal = (agSuc == true) ? reader.GetInt32(reader.GetOrdinal("id_sucursal")) : 0,
                                nombre_sucursal = (agSuc == true) ? reader.GetString(reader.GetOrdinal("nombre_sucursal")) : "",
                                id_producto = (agProd == true) ? reader.GetInt32(reader.GetOrdinal("id_producto")) : 0,
                                nombre_producto = (agProd == true) ? reader.GetString(reader.GetOrdinal("nombre_producto")) : "",
                                cantidad = reader.GetDecimal(reader.GetOrdinal("cantidad")),
                                precio = 0,
                                numero = 0,
                                fecha = (agDia == true) ? reader.GetDateTime(reader.GetOrdinal("fecha")) : DateTime.Today,
                                tipo_factura = 0,
                                vendedor = 0,
                                forma_pago = 0,
                                subTotal = reader.GetDouble(reader.GetOrdinal("subtotal")),
                                descuento = 0,
                                observaciones = "",
                                estado = 0,
                                CAE = "",
                                hora = DateTime.Now
                            };

                            venta.total = venta.subTotal - venta.descuento;
                            ventasList.Add(venta);
                        }
                    }
                    return ventasList.ToArray();
                }
            }
        }

        internal traslados[] obtener_traslados(DateTime fechaInicio, DateTime fechaFin)
        {
            sql.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([Fecha], GETDATE()), ISNULL([ID_Producto], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Cantidad], 0)" +
                ", ISNULL([ID_Sucursal_Origen], 0), ISNULL([Nombre_Origen], ''), ISNULL([ID_Sucursal_Destino], 0), ISNULL([Nombre_Destino], ''), ISNULL([Observaciones], ''), ISNULL([Estado], 0) FROM vw_traslados WHERE CAST([Fecha] AS DATE) BETWEEN @fechaInicio AND @fechaFin", sql))
            {
                cmd.Parameters.AddWithValue("@fechaInicio", fechaInicio);
                cmd.Parameters.AddWithValue("@fechaFin", fechaFin);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<traslados> list = new List<traslados>();
                    while (reader.Read())
                    {
                        traslados t = new traslados
                        {
                            id = reader.GetInt32(0),
                            fecha = reader.GetDateTime(1),
                            id_producto = reader.GetInt32(2),
                            nombre_producto = reader.GetString(3),
                            cantidad = (double)reader.GetSqlDouble(4),
                            id_sucursal_origen = reader.GetInt32(5),
                            nombre_sucursal_origen = reader.GetString(6),
                            id_sucursal_destino = reader.GetInt32(7),
                            nombre_sucursal_destino = reader.GetString(8),
                            observaciones = reader.GetString(9),
                            estado = reader.GetInt32(10)
                        };
                        list.Add(t);
                    }
                    return list.ToArray();
                }
            }
        }

        internal traslados obtener_traslado(int id)
        {
            sql.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([Fecha], GETDATE()), ISNULL([ID_Producto], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Cantidad], 0), ISNULL([ID_Sucursal_Origen], 0)" +
                ", ISNULL([Nombre_Origen], ''), ISNULL([ID_Sucursal_Destino], 0), ISNULL([Nombre_Destino], ''), ISNULL([Observaciones], ''), ISNULL([Estado], 0) FROM vw_traslados WHERE [ID] = @id", sql))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        traslados t = new traslados
                        {
                            id = reader.GetInt32(0),
                            fecha = reader.GetDateTime(1),
                            id_producto = reader.GetInt32(2),
                            nombre_producto = reader.GetString(3),
                            cantidad = (double)reader.GetSqlDouble(4),
                            id_sucursal_origen = reader.GetInt32(5),
                            nombre_sucursal_origen = reader.GetString(6),
                            id_sucursal_destino = reader.GetInt32(7),
                            nombre_sucursal_destino = reader.GetString(8),
                            observaciones = reader.GetString(9),
                            estado = reader.GetInt32(10)
                        };
                        return t;
                    }
                }
            }
            return null;
        }

        internal bool agregar_traslado(traslados traslado)
        {
            sql.Open();
            bool resultado = false;
            using (SqlCommand cmd = new SqlCommand("INSERT INTO traslados (Fecha, ID_Producto, Cantidad, ID_Sucursal_Origen, ID_Sucursal_Destino, Observaciones, Estado) VALUES (@fecha, @id_producto, @cantidad, @origen, @destino, @obs, @estado)", sql))
            {
                cmd.Parameters.AddWithValue("@fecha", traslado.fecha);
                cmd.Parameters.AddWithValue("@id_producto", traslado.id_producto);
                cmd.Parameters.AddWithValue("@cantidad", traslado.cantidad);
                cmd.Parameters.AddWithValue("@origen", traslado.id_sucursal_origen);
                cmd.Parameters.AddWithValue("@destino", traslado.id_sucursal_destino);
                cmd.Parameters.AddWithValue("@obs", traslado.observaciones);
                cmd.Parameters.AddWithValue("@estado", traslado.estado);
                cmd.ExecuteNonQuery();
                resultado = true;
            }
            return resultado;
        }

        internal bool editar_traslado(traslados traslado)
        {
            sql.Open();
            bool resultado = false;
            using (SqlCommand cmd = new SqlCommand("UPDATE traslados SET Fecha=@fecha, ID_Producto=@producto, Cantidad=@cantidad, ID_Sucursal_Origen=@origen, ID_Sucursal_Destino=@destino, Observaciones=@obs, Estado=@estado WHERE ID=@id", sql))
            {
                cmd.Parameters.AddWithValue("@id", traslado.id);
                cmd.Parameters.AddWithValue("@fecha", traslado.fecha);
                cmd.Parameters.AddWithValue("@producto", traslado.id_producto);
                cmd.Parameters.AddWithValue("@cantidad", traslado.cantidad);
                cmd.Parameters.AddWithValue("@origen", traslado.id_sucursal_origen);
                cmd.Parameters.AddWithValue("@destino", traslado.id_sucursal_destino);
                cmd.Parameters.AddWithValue("@obs", traslado.observaciones);
                cmd.Parameters.AddWithValue("@estado", traslado.estado);
                cmd.ExecuteNonQuery();
                resultado = true;
            }
            return resultado;
        }

        internal Modelos.ventas[] ventas_resumen_mini(int suc = 0, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            string filtro = " WHERE 1=1";
            if (suc > 0)
            {
                filtro += $" AND ID_Sucursal = {suc}";
            }
            if (fechaInicio != null)
            {
                filtro += $" AND Fecha >= '{fechaInicio.Value:yyyy-MM-dd}'";
            }
            if (fechaFin != null)
            {
                filtro += $" AND Fecha <= '{fechaFin.Value:yyyy-MM-dd}'";
            }
            string comando = $"SELECT ISNULL([ID_Sucursal], 0), nombre_sucursal, SUM(ISNULL([Cantidad], 0)), SUM(cast((cantidad*precio) as float)) As subtotal" +
                $", SUM(CAST(ISNULL([Descuento], 0) AS FLOAT)) FROM vw_ventas {filtro} GROUP BY ID_Sucursal, nombre_sucursal";
            sql.Open();
            using (SqlCommand cmd = new SqlCommand(comando, sql))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<ventas> ventasList = new List<ventas>();
                    while (reader.Read())
                    {
                        ventas venta = new ventas
                        {
                            id_sucursal = reader.GetInt32(0),
                            nombre_sucursal = reader.GetString(1),
                            cantidad = 0,
                            subTotal = 0,
                            descuento = 0
                        };
                        venta.total = venta.subTotal - venta.descuento;
                        ventasList.Add(venta);
                    }
                    return ventasList.ToArray();
                }
            }
        }

        internal void insertar_ventas(List<ventas> ventas)
        {
            sql.Open();
            foreach (var venta in ventas)
            {
                // Primero chequear que no exista el registro con id_venta_sucursal y id_sucursal
                using (SqlCommand checkCmd = new SqlCommand("SELECT [ID] FROM ventas WHERE ISNULL(id_venta_sucursal, '') = ISNULL(@id_venta_sucursal, '') AND ISNULL(id_sucursal, 0) = ISNULL(@id_sucursal, 0)", sql))
                {
                    checkCmd.Parameters.AddWithValue("@id_venta_sucursal", (object)venta.id_venta_sucursal ?? System.DBNull.Value);
                    checkCmd.Parameters.AddWithValue("@id_sucursal", (object)venta.id_sucursal ?? System.DBNull.Value);
                    var existing = checkCmd.ExecuteScalar();
                    if (existing != null && existing != DBNull.Value)
                    {
                        // actualizar
                        using (SqlCommand cmd = new SqlCommand("UPDATE ventas SET [numero]=@numero, [fecha]=@fecha, [tipo_factura]=@tipo_factura, [cliente]=@cliente, [vendedor]=@vendedor, [forma_pago]=@forma_pago, [subTotal]=@subTotal, [iva]=@iva, [descuento]=@descuento, [total]=@total, [observaciones]=@observaciones, [estado]=@estado, [CAE]=@CAE, [hora]=@hora, [id_venta_sucursal]=@id_venta_sucursal WHERE [ID]=@id", sql))
                        {
                            cmd.Parameters.AddWithValue("@id", Convert.ToInt32(existing));
                            cmd.Parameters.AddWithValue("@numero", venta.numero);
                            cmd.Parameters.AddWithValue("@fecha", venta.fecha);
                            cmd.Parameters.AddWithValue("@tipo_factura", venta.tipo_factura);
                            cmd.Parameters.AddWithValue("@cliente", venta.cliente);
                            cmd.Parameters.AddWithValue("@vendedor", venta.vendedor);
                            cmd.Parameters.AddWithValue("@forma_pago", venta.forma_pago);
                            cmd.Parameters.AddWithValue("@subTotal", venta.subTotal);
                            cmd.Parameters.AddWithValue("@iva", venta.iva);
                            cmd.Parameters.AddWithValue("@descuento", venta.descuento);
                            cmd.Parameters.AddWithValue("@total", venta.total);
                            cmd.Parameters.AddWithValue("@observaciones", venta.observaciones);
                            cmd.Parameters.AddWithValue("@estado", venta.estado);
                            cmd.Parameters.AddWithValue("@CAE", venta.CAE);
                            cmd.Parameters.AddWithValue("@hora", venta.hora);
                            cmd.Parameters.AddWithValue("@id_venta_sucursal", (object)venta.id_venta_sucursal ?? System.DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        // insertar
                        using (SqlCommand cmd = new SqlCommand("INSERT INTO ventas ([numero], [fecha], [tipo_factura], [cliente], [vendedor], [forma_pago], [subTotal], [iva], [descuento], [total], [observaciones], [estado], [id_sucursal], [CAE], [hora], [id_venta_sucursal]) VALUES (@numero, @fecha, @tipo_factura, @cliente, @vendedor, @forma_pago, @subTotal, @iva, @descuento, @total, @observaciones, @estado, @id_sucursal, @CAE, @hora, @id_venta_sucursal)", sql))
                        {
                            cmd.Parameters.AddWithValue("@numero", venta.numero);
                            cmd.Parameters.AddWithValue("@fecha", venta.fecha);
                            cmd.Parameters.AddWithValue("@tipo_factura", venta.tipo_factura);
                            cmd.Parameters.AddWithValue("@cliente", venta.cliente);
                            cmd.Parameters.AddWithValue("@vendedor", venta.vendedor);
                            cmd.Parameters.AddWithValue("@forma_pago", venta.forma_pago);
                            cmd.Parameters.AddWithValue("@subTotal", venta.subTotal);
                            cmd.Parameters.AddWithValue("@iva", venta.iva);
                            cmd.Parameters.AddWithValue("@descuento", venta.descuento);
                            cmd.Parameters.AddWithValue("@total", venta.total);
                            cmd.Parameters.AddWithValue("@observaciones", venta.observaciones);
                            cmd.Parameters.AddWithValue("@estado", venta.estado);
                            cmd.Parameters.AddWithValue("@id_sucursal", (object)venta.id_sucursal ?? System.DBNull.Value);
                            cmd.Parameters.AddWithValue("@CAE", venta.CAE);
                            cmd.Parameters.AddWithValue("@hora", venta.hora);
                            cmd.Parameters.AddWithValue("@id_venta_sucursal", (object)venta.id_venta_sucursal ?? System.DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }
    }
}
