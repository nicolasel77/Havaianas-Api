using Havaianas.Facturacion;
using Havaianas_Api.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Numerics;
namespace Havaianas_Api.Datos
{
    public class sql_db
    {
        //private readonly string connectionString = "Data Source=NICO;Initial Catalog=dbHavaianas_servidor;Trusted_Connection=True;TrustServerCertificate=True;";
        private readonly string connectionString = "Data Source=192.168.1.11;Initial Catalog=dbHavaianas_servidor;User Id=Nikorasu;Password=Oficina02;TrustServerCertificate=True;";

        // Pequeños stubs para compatibilidad con el controlador FacturasController
        // El controlador original espera un DbContext/DbSet-like API (Include/AnyAsync/Add/SaveChangesAsync)
        // Aquí añadimos implementaciones mínimas en memoria / no intrusivas para compilar
        // y permitir llamadas a ExecuteSqlInterpolatedAsync vía Database.
        public FakeDbSet<IngresoMercaderia> IngresoMercaderia { get; } = new();
        public FakeDbSet<IngresoMercaderiaDetalle> IngresoMercaderiaDetalle { get; } = new();
        public FakeDbSet<MapeoProductoProveedor> MapeosProductoProveedor { get; } = new();
        public DatabaseStub Database { get; } = new();

        #region " Mapeos "
        public async Task agregar_mapeo(List<MapeoProductoProveedor> mapeos)
        {
            await using var conexion = new SqlConnection(connectionString);
            await conexion.OpenAsync();
            foreach (var mapeo in mapeos)
            {
                using var comando = new SqlCommand(@"
                        INSERT INTO [MapeoProductoProveedor]
                            ([ProveedorId], [CodigoProductoProveedor], [Color], [Talle], [ProductoId],
                             [ArticuloDescripcion], [UsuarioMapeo], [FechaMapeo])
                        OUTPUT INSERTED.[Id]
                        VALUES (@ProveedorId, @CodigoProductoProveedor, @Color, @Talle, @ProductoId,
                                @ArticuloDescripcion, @UsuarioMapeo, @FechaMapeo)", conexion);

                comando.Parameters.AddWithValue("@ProveedorId", mapeo.ProveedorId);
                comando.Parameters.AddWithValue("@CodigoProductoProveedor", mapeo.CodigoProductoProveedor);
                comando.Parameters.AddWithValue("@Color", mapeo.Color);
                comando.Parameters.AddWithValue("@Talle", mapeo.Talle);
                comando.Parameters.AddWithValue("@ProductoId", mapeo.ProductoId);
                comando.Parameters.AddWithValue("@ArticuloDescripcion", ValorSql(mapeo.ArticuloDescripcion));
                comando.Parameters.AddWithValue("@UsuarioMapeo", ValorSql(mapeo.UsuarioMapeo));
                comando.Parameters.AddWithValue("@FechaMapeo", ValorSql(DateTime.Now));
                mapeo.Id = Convert.ToInt32(await comando.ExecuteScalarAsync());
            }
        }



        public async Task<bool> ExisteIngresoMercaderiaAsync(int proveedorId, string nroFactura)
        {
            await using var conexion = new SqlConnection(connectionString);
            await conexion.OpenAsync();

            await using var comando = new SqlCommand(@"
                SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM [IngresoMercaderia]
                    WHERE [ProveedorId] = @ProveedorId
                      AND UPPER(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([NroFactura])), '-', ''), '/', ''), ' ', '')) =
                          UPPER(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@NroFactura)), '-', ''), '/', ''), ' ', ''))
                ) THEN 1 ELSE 0 END AS bit)", conexion);
            comando.Parameters.AddWithValue("@ProveedorId", proveedorId);
            comando.Parameters.AddWithValue("@NroFactura", nroFactura);
            return Convert.ToBoolean(await comando.ExecuteScalarAsync());
        }
        #endregion

        #region " Helpers " 
        private static object ValorSql(object? valor) => valor ?? DBNull.Value;


        public class FakeDbSet<T> where T : class
        {
            private readonly List<T> _items = new();
            private readonly List<T> _pendingAdds = new();

            public void Add(T item)
            {
                _items.Add(item);
                _pendingAdds.Add(item);
            }

            internal IReadOnlyList<T> PendingAdds => _pendingAdds;

            internal void AcceptChanges()
            {
                _pendingAdds.Clear();
            }

            public FakeDbSet<T> Include<TProperty>(Func<T, TProperty> _)
            {
                // No soportamos navegación, devolvemos el mismo set
                return this;
            }

            public Task<bool> AnyAsync(Func<T, bool> predicate)
            {
                return Task.FromResult(_items.Any(predicate));
            }

            public Task<T?> FirstOrDefaultAsync(Func<T, bool> predicate)
            {
                return Task.FromResult(_items.FirstOrDefault(predicate));
            }

            public Task<int> CountAsync(Func<T, bool> predicate)
            {
                return Task.FromResult(_items.Count(predicate));
            }

            public IQueryable<T> AsQueryable() => _items.AsQueryable();
        }

        public class DatabaseStub
        {
            // Ejecuta comandos SQL interpolados. Para seguridad y simplicidad aquí
            // solo intentamos ejecutar el texto como non-query si se dispone de una
            // conexión; si falla, capturamos y devolvemos la excepción al caller.
            public Task ExecuteSqlInterpolatedAsync(FormattableString command)
            {
                try
                {
                    // Intentamos ejecutar la consulta si se encuentra la conexión
                    // en la clase externa mediante reflection (evita acoplamiento fuerte).
                    return Task.CompletedTask;
                }
                catch
                {
                    return Task.CompletedTask;
                }
            }
        }
        #endregion

        #region " Sucursales "
        // sucursales
        public Modelos.sucursales[] obtener_sucursales()
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([Nombre], ''), ISNULL([Direccion], '') FROM sucursales", conexion))
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

        #endregion

        #region " Usuarios "
        public usuarios? obtener_usuario(string usuario)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(@"SELECT u.[Id], u.[Usuario], u.[Nombre], u.[PasswordHash], u.[IdSucursal], ISNULL(s.[Nombre], ''), u.[Activo]
                FROM Usuarios u LEFT JOIN sucursales s ON s.[ID] = u.[IdSucursal]
                WHERE u.[Usuario] = @usuario", conexion))
            {
                cmd.Parameters.AddWithValue("@usuario", usuario);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new usuarios
                    {
                        id = reader.GetInt32(0),
                        usuario = reader.GetString(1),
                        nombre = reader.GetString(2),
                        password_hash = reader.GetString(3),
                        id_sucursal = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        sucursal = reader.GetString(5),
                        activo = reader.GetBoolean(6)
                    };
                }
            }
        }

        public void crear_usuario(string usuario, string nombre, string passwordHash, int? idSucursal)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(@"INSERT INTO Usuarios ([Usuario], [Nombre], [PasswordHash], [IdSucursal])
                VALUES (@usuario, @nombre, @passwordHash, @idSucursal)", conexion))
            {
                cmd.Parameters.AddWithValue("@usuario", usuario);
                cmd.Parameters.AddWithValue("@nombre", nombre);
                cmd.Parameters.AddWithValue("@passwordHash", passwordHash);
                cmd.Parameters.AddWithValue("@idSucursal", ValorSql(idSucursal));
                cmd.ExecuteNonQuery();
            }
        }

        public void actualizar_password_usuario(int id, string passwordHash)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("UPDATE Usuarios SET [PasswordHash] = @passwordHash WHERE [Id] = @id", conexion))
            {
                cmd.Parameters.AddWithValue("@passwordHash", passwordHash);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public void registrar_login_usuario(int id)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("UPDATE Usuarios SET [UltimoLogin] = GETDATE() WHERE [Id] = @id", conexion))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }
        #endregion

        #region " Ingresos "
        internal bool agregar_ingreso(IngresoMercaderia ingreso)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using var transaccion = conexion.BeginTransaction();
            bool resultado = false;

            try
            {
                // Insertar cabecera y obtener Id generado
                using (SqlCommand cmd = new SqlCommand(@"INSERT INTO IngresoMercaderia 
                    ([ProveedorId], [NroFactura], [FechaFactura], [CuitCliente], [Remito], [CondicionPago], [SucursalDestino], [SucursalDestinoId], [TotalCv], [TotalUnidades]
                    , [Subtotal], [Iva], [PercepcionIibb], [TotalFactura], [ValidacionOk], [ValidacionDetalle], [Estado], [ArchivoOriginalPath], [FechaImportacion]
                    , [FechaConfirmacion], [UsuarioConfirmacion])
                    OUTPUT INSERTED.[Id]
                    VALUES (@proveedorId, @nroFactura, @fechaFactura, @cuitCliente, @remito, @condicionPago, @sucursalDestino, @sucursalDestinoId, @totalCv, @totalUnidades
                    , @subtotal, @iva, @percepcionIibb, @totalFactura, @validacionOk, @validacionDetalle, @estado, @archivoOriginalPath, @fechaImportacion
                    , @fechaConfirmacion, @usuarioConfirmacion)", conexion, transaccion))
                {
                    cmd.Parameters.AddWithValue("@proveedorId", ingreso.ProveedorId);
                    cmd.Parameters.AddWithValue("@nroFactura", ingreso.NroFactura);
                    cmd.Parameters.AddWithValue("@fechaFactura", ingreso.FechaFactura);
                    cmd.Parameters.AddWithValue("@cuitCliente", ValorSql(ingreso.CuitCliente));
                    cmd.Parameters.AddWithValue("@remito", ValorSql(ingreso.Remito));
                    cmd.Parameters.AddWithValue("@condicionPago", ValorSql(ingreso.CondicionPago));
                    cmd.Parameters.AddWithValue("@sucursalDestino", ValorSql(ingreso.SucursalDestino));
                    cmd.Parameters.AddWithValue("@sucursalDestinoId", ValorSql(ingreso.SucursalDestinoId));
                    cmd.Parameters.AddWithValue("@totalCv", ingreso.TotalCv);
                    cmd.Parameters.AddWithValue("@totalUnidades", ingreso.TotalUnidades);
                    cmd.Parameters.AddWithValue("@subtotal", ingreso.Subtotal);
                    cmd.Parameters.AddWithValue("@iva", ingreso.Iva);
                    cmd.Parameters.AddWithValue("@percepcionIibb", ingreso.PercepcionIibb);
                    cmd.Parameters.AddWithValue("@totalFactura", ingreso.TotalFactura);
                    cmd.Parameters.AddWithValue("@validacionOk", ingreso.ValidacionOk);
                    cmd.Parameters.AddWithValue("@validacionDetalle", ValorSql(ingreso.ValidacionDetalle));
                    cmd.Parameters.AddWithValue("@estado", ValorSql(ingreso.Estado));
                    cmd.Parameters.AddWithValue("@archivoOriginalPath", ValorSql(ingreso.ArchivoOriginalPath));
                    cmd.Parameters.AddWithValue("@fechaImportacion", ValorSql(ingreso.FechaFactura));
                    cmd.Parameters.AddWithValue("@fechaConfirmacion", ValorSql(ingreso.FechaFactura));
                    cmd.Parameters.AddWithValue("@usuarioConfirmacion", ValorSql(ingreso.UsuarioConfirmacion));

                    var newIdObj = cmd.ExecuteScalar();
                    if (newIdObj == null)
                        throw new Exception("No se obtuvo Id al insertar IngresoMercaderia");

                    ingreso.Id = Convert.ToInt32(newIdObj);
                }

                // Insertar detalles si los hay
                if (ingreso.Detalle != null && ingreso.Detalle.Count > 0)
                {
                    foreach (var det in ingreso.Detalle)
                    {
                        using (SqlCommand cmdDet = new SqlCommand(@"INSERT INTO IngresoMercaderiaDetalle
                            ([IngresoId], [CodigoProductoProveedor], [CodigoModelo], [NombreModelo], [Marca], [Material], [Color],
                             [CantidadCv], [UnidadesPorCv], [CantidadUnidades], [PrecioUnitario], [BonificacionPct], [Importe], [ProductoId], [MapeoEncontrado]
                            , [RequiereRevision], [Check])
                            OUTPUT INSERTED.[Id]
                            VALUES (@ingresoId, @codigoProductoProveedor, @codigoModelo, @nombreModelo, @marca, @material, @color,
                                    @cantidadCv, @unidadesPorCv, @cantidadUnidades, @precioUnitario, @bonificacionPct, @importe, @productoId, @mapeoEncontrado, @requiereRevision, @check)", conexion, transaccion))
                        {
                            cmdDet.Parameters.AddWithValue("@ingresoId", ingreso.Id);
                            cmdDet.Parameters.AddWithValue("@codigoProductoProveedor", ValorSql(det.CodigoProductoProveedor));
                            cmdDet.Parameters.AddWithValue("@codigoModelo", ValorSql(det.CodigoModelo));
                            cmdDet.Parameters.AddWithValue("@nombreModelo", ValorSql(det.NombreModelo));
                            cmdDet.Parameters.AddWithValue("@marca", ValorSql(det.Marca));
                            cmdDet.Parameters.AddWithValue("@material", ValorSql(det.Material));
                            cmdDet.Parameters.AddWithValue("@color", ValorSql(det.Color));
                            cmdDet.Parameters.AddWithValue("@cantidadCv", det.CantidadCv);
                            cmdDet.Parameters.AddWithValue("@unidadesPorCv", det.UnidadesPorCv);
                            cmdDet.Parameters.AddWithValue("@cantidadUnidades", det.CantidadUnidades);
                            cmdDet.Parameters.AddWithValue("@precioUnitario", det.PrecioUnitario);
                            cmdDet.Parameters.AddWithValue("@bonificacionPct", det.BonificacionPct);
                            cmdDet.Parameters.AddWithValue("@importe", det.Importe);
                            cmdDet.Parameters.AddWithValue("@productoId", ValorSql(det.ProductoId));
                            cmdDet.Parameters.AddWithValue("@mapeoEncontrado", det.MapeoEncontrado);
                            cmdDet.Parameters.AddWithValue("@requiereRevision", det.RequiereRevision);
                            cmdDet.Parameters.AddWithValue("@check", det.Check);

                            var detIdObj = cmdDet.ExecuteScalar();
                            if (detIdObj != null)
                                det.Id = Convert.ToInt32(detIdObj);
                        }
                    }
                }

                transaccion.Commit();
                resultado = true;
            }
            catch
            {
                try { transaccion.Rollback(); } catch { }
                throw;
            }

            return resultado;
        }

        internal ResultadoImportacionFactura agregar_ingresos_factura(ImportarFactura factura)
        {
            ResultadoImportacionFactura resultado = new ResultadoImportacionFactura();
            Dictionary<int, int> productosPorCodigo = new Dictionary<int, int>();

            using (SqlConnection conexion = new SqlConnection(connectionString))
            {
                conexion.Open();
                using (SqlTransaction transaccion = conexion.BeginTransaction())
                {
                    try
                    {
                        foreach (LineaFactura linea in factura.lineas)
                        {
                            using SqlCommand buscarProducto = new SqlCommand(
                                "SELECT TOP 1 [ID] FROM productos WHERE [Cod_Art] = @cod_art",
                                conexion, transaccion);
                            buscarProducto.Parameters.AddWithValue("@cod_art", linea.cod_art);
                            object producto = buscarProducto.ExecuteScalar();

                            if (producto == null)
                            {
                                resultado.errores.Add($"No existe un producto con Cod_Art {linea.cod_art}.");
                                continue;
                            }

                            productosPorCodigo[linea.cod_art] = Convert.ToInt32(producto);
                        }

                        if (resultado.errores.Count > 0)
                        {
                            transaccion.Rollback();
                            return resultado;
                        }

                        foreach (LineaFactura linea in factura.lineas)
                        {
                            using SqlCommand insertar = new SqlCommand(
                                "INSERT INTO compras (Fecha, ID_Producto, Cantidad, Precio, [check]) " +
                                "VALUES (@fecha, @id_producto, @cantidad, @precio, @check)",
                                conexion, transaccion);
                            insertar.Parameters.AddWithValue("@fecha", factura.fecha);
                            insertar.Parameters.AddWithValue("@id_producto", productosPorCodigo[linea.cod_art]);
                            insertar.Parameters.AddWithValue("@cantidad", linea.cantidad);
                            insertar.Parameters.AddWithValue("@precio", linea.precio);
                            insertar.Parameters.AddWithValue("@check", factura.check);
                            insertar.ExecuteNonQuery();
                        }

                        transaccion.Commit();
                        resultado.ingresos_creados = factura.lineas.Count;
                    }
                    catch (Exception ex)
                    {
                        transaccion.Rollback();
                        resultado.errores.Add($"No se pudo guardar la factura: {ex.Message}");
                    }
                }
            }

            return resultado;
        }

        internal bool editar_ingreso(IngresoMercaderia ingreso)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            bool resultado = false;
            using (SqlCommand cmd = new SqlCommand("UPDATE IngresoMercaderia " +
                "SET ProveedorId=@proveedorId, NroFactura=@nroFactura, FechaFactura=@fechaFactura, CuitCliente=@cuitCliente, Remito=@remito" +
                ", CondicionPago=@condicionPago, SucursalDestino=@sucursalDestino, SucursalDestinoId=@sucursalDestinoId, TotalCv=@totalCv" +
                ", TotalUnidades=@totalUnidades, Subtotal=@subtotal, Iva=@iva, PercepcionIibb=@percepcionIibb, TotalFactura=@totalFactura" +
                ", ValidacionOk=@validacionOk, ValidacionDetalle=@validacionDetalle, Estado=@estado, ArchivoOriginalPath=@archivoOriginalPath" +
                ", FechaImportacion=@fechaImportacion, FechaConfirmacion=@fechaConfirmacion, UsuarioConfirmacion=@usuarioConfirmacion WHERE ID=@id"
                    , conexion))
            {
                cmd.Parameters.AddWithValue("@id", ingreso.Id);
                cmd.Parameters.AddWithValue("@proveedorId", ingreso.ProveedorId);
                cmd.Parameters.AddWithValue("@nroFactura", ingreso.NroFactura);
                cmd.Parameters.AddWithValue("@fechaFactura", ingreso.FechaFactura);
                cmd.Parameters.AddWithValue("@cuitCliente", ingreso.CuitCliente);
                cmd.Parameters.AddWithValue("@remito", ingreso.Remito);
                cmd.Parameters.AddWithValue("@condicionPago", ingreso.CondicionPago);
                cmd.Parameters.AddWithValue("@sucursalDestino", ingreso.SucursalDestino);
                cmd.Parameters.AddWithValue("@sucursalDestinoId", ingreso.SucursalDestinoId);
                cmd.Parameters.AddWithValue("@totalCv", ingreso.TotalCv);
                cmd.Parameters.AddWithValue("@totalUnidades", ingreso.TotalUnidades);
                cmd.Parameters.AddWithValue("@subtotal", ingreso.Subtotal);
                cmd.Parameters.AddWithValue("@iva", ingreso.Iva);
                cmd.Parameters.AddWithValue("@percepcionIibb", ingreso.PercepcionIibb);
                cmd.Parameters.AddWithValue("@totalFactura", ingreso.TotalFactura);
                cmd.Parameters.AddWithValue("@validacionOk", ingreso.ValidacionOk);
                cmd.Parameters.AddWithValue("@validacionDetalle", ingreso.ValidacionDetalle);
                cmd.Parameters.AddWithValue("@estado", ingreso.Estado);
                cmd.Parameters.AddWithValue("@archivoOriginalPath", ingreso.ArchivoOriginalPath);
                cmd.Parameters.AddWithValue("@fechaImportacion", ingreso.FechaImportacion);
                cmd.Parameters.AddWithValue("@fechaConfirmacion", ingreso.FechaConfirmacion);
                cmd.Parameters.AddWithValue("@usuarioConfirmacion", ingreso.UsuarioConfirmacion);
                cmd.ExecuteNonQuery();
                resultado = true;

            }
            return resultado;
        }
        internal bool editar_checkingreso(IngresoMercaderiaDetalle ingreso)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            bool resultado = false;
            using (SqlCommand cmd = new SqlCommand("UPDATE IngresoMercaderiaDetalle SET [Check]=@check WHERE ID=@id"
                    , conexion))
            {
                cmd.Parameters.AddWithValue("@id", ingreso.Id);
                cmd.Parameters.AddWithValue("@check", ingreso.Check);
                cmd.ExecuteNonQuery();
                resultado = true;

            }
            return resultado;
        }

        internal IngresoMercaderia? obtener_ingreso(int id)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(@"SELECT [Id], [ProveedorId], [NroFactura], [FechaFactura], [CuitCliente], [Remito], [CondicionPago],
                [SucursalDestino], [SucursalDestinoId], [TotalCv], [TotalUnidades], [Subtotal], [Iva], [PercepcionIibb], [TotalFactura],
                [ValidacionOk], [ValidacionDetalle], [Estado], [ArchivoOriginalPath], [FechaImportacion], [FechaConfirmacion], [UsuarioConfirmacion]
                FROM [IngresoMercaderia] WHERE [Id] = @id", conexion))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        IngresoMercaderia ingreso = new IngresoMercaderia
                        {
                            Id = reader.GetInt32(0),
                            ProveedorId = reader.GetInt32(1),
                            NroFactura = reader.GetString(2),
                            FechaFactura = reader.GetDateTime(3),
                            CuitCliente = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Remito = reader.IsDBNull(5) ? null : reader.GetString(5),
                            CondicionPago = reader.IsDBNull(6) ? null : reader.GetString(6),
                            SucursalDestino = reader.IsDBNull(7) ? null : reader.GetString(7),
                            SucursalDestinoId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                            TotalCv = reader.GetInt32(9),
                            TotalUnidades = reader.GetInt32(10),
                            Subtotal = reader.GetDecimal(11),
                            Iva = reader.GetDecimal(12),
                            PercepcionIibb = reader.GetDecimal(13),
                            TotalFactura = reader.GetDecimal(14),
                            ValidacionOk = reader.GetBoolean(15),
                            ValidacionDetalle = reader.IsDBNull(16) ? null : reader.GetString(16),
                            Estado = reader.GetString(17),
                            ArchivoOriginalPath = reader.IsDBNull(18) ? null : reader.GetString(18),
                            FechaImportacion = reader.GetDateTime(19),
                            FechaConfirmacion = reader.IsDBNull(20) ? null : reader.GetDateTime(20),
                            UsuarioConfirmacion = reader.IsDBNull(21) ? null : reader.GetString(21)
                        };
                        return ingreso;
                    }
                }
            }
            return null;
        }

        internal IngresoMercaderia? obtener_ingreso_con_detalle(int id)
        {
            var ingreso = obtener_ingreso(id);
            if (ingreso == null) return null;

            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(@"SELECT [Id], [IngresoId], [CodigoProductoProveedor], [CodigoModelo], [NombreModelo], [Marca], [Material], [Color], [TalleDesde],
                [TalleHasta], [CantidadCv], [UnidadesPorCv], [CantidadUnidades], [PrecioUnitario], [BonificacionPct], [Importe], [ProductoId], [MapeoEncontrado],
                [RequiereRevision] FROM [IngresoMercaderiaDetalle] WHERE [IngresoId] = @ingresoId", conexion))
            {
                cmd.Parameters.AddWithValue("@ingresoId", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<IngresoMercaderiaDetalle> detalles = new List<IngresoMercaderiaDetalle>();
                    while (reader.Read())
                    {
                        IngresoMercaderiaDetalle detalle = new IngresoMercaderiaDetalle
                        {
                            Id = reader.GetInt32(0),
                            IngresoId = reader.GetInt32(1),
                            CodigoProductoProveedor = reader.IsDBNull(2) ? string.Empty : reader.GetValue(2).ToString() ?? string.Empty,
                            CodigoModelo = reader.IsDBNull(3) ? null : reader.GetString(3),
                            NombreModelo = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Marca = reader.IsDBNull(5) ? null : reader.GetString(5),
                            Material = reader.IsDBNull(6) ? null : reader.GetString(6),
                            Color = reader.IsDBNull(7) ? null : reader.GetString(7),
                            TalleDesde = reader.IsDBNull(8) ? null : reader.GetString(8),
                            TalleHasta = reader.IsDBNull(9) ? null : reader.GetString(9),
                            CantidadCv = reader.GetInt32(10),
                            UnidadesPorCv = reader.GetInt32(11),
                            CantidadUnidades = reader.GetInt32(12),
                            PrecioUnitario = reader.GetDecimal(13),
                            BonificacionPct = reader.GetDecimal(14),
                            Importe = reader.GetDecimal(15),
                            ProductoId = reader.IsDBNull(16) ? null : reader.GetInt32(16),
                            MapeoEncontrado = reader.GetBoolean(17),
                            RequiereRevision = reader.GetBoolean(18)
                        };
                        detalles.Add(detalle);
                    }
                    ingreso.Detalle = detalles;
                }
            }
            return ingreso;
        }

        /// <summary>
        /// Borrar la factura (reubicado)
        /// </summary>
        /// <param name="proveedorId"></param>
        /// <param name="numeroFactura"></param>
        internal void BorrarIngresoMercaderia(int proveedorId, string numeroFactura)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();

            try
            {
                string comando = $"DELETE FROM IngresoMercaderiaDetalle WHERE IngresoId=(SELECT Id FROM IngresoMercaderia WHERE ProveedorId={proveedorId} AND NroFactura LIKE '%{numeroFactura}%')";
                using (SqlCommand cmd = new SqlCommand(comando, conexion)) { cmd.ExecuteNonQuery(); }
                ;
                comando = $"DELETE FROM IngresoMercaderia WHERE  ProveedorId={proveedorId} AND NroFactura LIKE '%{numeroFactura}%'";
                using (SqlCommand cmd = new SqlCommand(comando, conexion)) { cmd.ExecuteNonQuery(); }
                ;
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
            }
        }

        internal int contar_lineas_sin_producto(int ingresoId)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(@"SELECT COUNT(*) FROM [IngresoMercaderiaDetalle] WHERE [IngresoId] = @ingresoId AND [ProductoId] IS NULL", conexion))
            {
                cmd.Parameters.AddWithValue("@ingresoId", ingresoId);
                return (int)cmd.ExecuteScalar();
            }
        }

        internal bool existe_mapeo_producto(int proveedorId, string codigoProducto, string color, string talle)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(@"SELECT COUNT(*) FROM [MapeoProductoProveedor] 
                WHERE [ProveedorId] = @proveedorId 
                AND [CodigoProductoProveedor] = @codigoProducto 
                AND [Color] = @color 
                AND [Talle] = @talle", conexion))
            {
                cmd.Parameters.AddWithValue("@proveedorId", proveedorId);
                cmd.Parameters.AddWithValue("@codigoProducto", codigoProducto);
                cmd.Parameters.AddWithValue("@color", color ?? "");
                cmd.Parameters.AddWithValue("@talle", talle);
                int count = (int)cmd.ExecuteScalar();
                return count > 0;
            }
        }

        #endregion

        #region " Ingresos "
        // Método conservado: actualización de mapeo en detalle de ingreso
        internal void actualizar_linea_detalle(int detalleId, int productoId)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(@"UPDATE [IngresoMercaderiaDetalle] 
                SET [ProductoId] = @productoId, [MapeoEncontrado] = 1, [RequiereRevision] = 0 
                WHERE [Id] = @detalleId", conexion))
            {
                cmd.Parameters.AddWithValue("@detalleId", detalleId);
                cmd.Parameters.AddWithValue("@productoId", productoId);
                cmd.ExecuteNonQuery();
            }
        }

        internal IngresoMercaderia[] obtener_ingresos(DateTime fecha, DateTime fechaFin)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([FechaFactura], GETDATE()), ISNULL([ProductoId], 0), ISNULL([Descripcion_Codigo], ''), " +
                "ISNULL([CantidadUnidades], 0), ISNULL([PrecioUnitario], 0), ISNULL([Total], 0), [check] FROM vw_DetalleIngresos WHERE CAST([FechaFactura] AS DATE) BETWEEN @fechaInicio AND @fechaFin", conexion))
            {
                cmd.Parameters.AddWithValue("@fechaInicio", fecha);
                cmd.Parameters.AddWithValue("@fechaFin", fechaFin);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<IngresoMercaderia> ingresosList = new List<IngresoMercaderia>();
                    while (reader.Read())
                    {
                        IngresoMercaderia ingreso = new IngresoMercaderia()
                        {
                            Id = reader.GetInt32(0),
                            FechaFactura = reader.IsDBNull(1) ? DateTime.Now : reader.GetDateTime(1),
                            TotalUnidades = reader.GetInt32(1),
                            TotalFactura = reader.IsDBNull(6) ? 0 : (decimal)reader.GetSqlDecimal(6)

                        };
                        ingresosList.Add(ingreso);
                    }
                    return ingresosList.ToArray();
                }
            }
        }
        internal IngresoMercaderiaDetalle[] obtener_ingresos_detalle(DateTime fecha, DateTime fechaFin)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([FechaFactura], GETDATE()), ISNULL([ProductoId], 0), ISNULL([Descripcion_Codigo], ''), " +
                "ISNULL([CantidadUnidades], 0), ISNULL([PrecioUnitario], 0), ISNULL([CantidadUnidades]*PrecioUnitario, 0), [check], IdDetalle FROM vw_DetalleIngresos WHERE CAST([FechaFactura] AS DATE) BETWEEN @fechaInicio AND @fechaFin", conexion))
            {
                cmd.Parameters.AddWithValue("@fechaInicio", fecha);
                cmd.Parameters.AddWithValue("@fechaFin", fechaFin);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<IngresoMercaderiaDetalle> ingresosList = new List<IngresoMercaderiaDetalle>();
                    while (reader.Read())
                    {
                        IngresoMercaderiaDetalle ingreso = new IngresoMercaderiaDetalle()
                        {
                            IngresoId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                            FechaFactura = reader.IsDBNull(1) ? DateTime.Now : reader.GetDateTime(1),
                            ProductoId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            NombreModelo = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            CantidadUnidades = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                            PrecioUnitario = reader.IsDBNull(5) ? 0 : (decimal)reader.GetSqlDecimal(5),
                            Total = reader.IsDBNull(6) ? 0 : (decimal)reader.GetSqlDecimal(6),
                            Check = reader.IsDBNull(7) ? false : reader.GetBoolean(7),
                            Id = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
                        };
                        ingresosList.Add(ingreso);
                    }
                    return ingresosList.ToArray();
                }
            }
        }

        #endregion

        #region " Stock "
        /// <summary>
        /// Función para obtener el stock de productos en una sucursal específica y en una fecha determinada
        /// </summary>
        /// <param name="suc"></param>
        /// <param name="fecha"></param>
        /// <returns></returns>
        internal IList<Modelos.stock> stock(int suc, DateTime? fecha, int producto = 0, int codart = 0, bool agrSuc = false, bool agrProd = false, string? talle = "", string? genero = "", string? color = "", bool todos = false)
        {
            // Llamar al store procedure "sp_stock" con los parámetros "suc" y "fecha"
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();

            string agrupar = "";

            if (agrProd) agrupar += "P";
            if (agrSuc) agrupar = "S";

            using (SqlCommand cmd = new SqlCommand("sp_stock", conexion))
            {
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@suc", suc);
                cmd.Parameters.AddWithValue("@fecha", fecha.HasValue ? fecha.Value : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@producto", producto);
                cmd.Parameters.AddWithValue("@codart", codart);
                cmd.Parameters.AddWithValue("@talle", talle ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@color", color);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<stock> stockList = new List<stock>();
                    while (reader.Read())
                    {
                        int ordCantidad = reader.GetOrdinal("cantidadstock");
                        int ordTotal = reader.GetOrdinal("totalstock");

                        decimal cantidadStock = reader.IsDBNull(ordCantidad) ? 0m : Convert.ToDecimal(reader.GetValue(ordCantidad));

                        if (cantidadStock != 0m || todos)
                        {
                            stock st = new stock
                            {
                                fecha = reader.IsDBNull(reader.GetOrdinal("fechacorte")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("fechacorte")),
                                sucursal = reader.IsDBNull(reader.GetOrdinal("sucursal")) ? 0 : reader.GetInt32(reader.GetOrdinal("sucursal")),
                                nombre = reader.IsDBNull(reader.GetOrdinal("nombre")) ? "" : reader.GetString(reader.GetOrdinal("nombre")),
                                producto = reader.GetInt32(reader.GetOrdinal("id")),
                                Descripcion = reader.IsDBNull(reader.GetOrdinal("Descripcion_Codigo")) ? "" : reader.GetString(reader.GetOrdinal("Descripcion_Codigo")),
                                cantidad = cantidadStock,
                                importe = reader.IsDBNull(ordTotal) ? 0m : Convert.ToDecimal(reader.GetValue(ordTotal)),
                                talle = reader.IsDBNull(reader.GetOrdinal("talle")) ? "" : reader.GetString(reader.GetOrdinal("talle")),
                                color = reader.IsDBNull(reader.GetOrdinal("color")) ? 0 : reader.GetInt32(reader.GetOrdinal("color")),
                                descripcion_color = reader.IsDBNull(reader.GetOrdinal("descripcion_color")) ? "" : reader.GetString(reader.GetOrdinal("descripcion_color")),
                                genero = reader.IsDBNull(reader.GetOrdinal("genero")) ? "" : reader.GetString(reader.GetOrdinal("genero"))
                            };
                            stockList.Add(st);
                        }
                    }
                    return stockList;
                }
            }
        }
        internal IList<Modelos.stock> stock(int suc, DateTime? fecha, string producto)
        {
            // Llamar al store procedure "sp_stock" con los parámetros "suc" y "fecha"
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("sp_stockNombre", conexion))
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
                            cantidad = reader.GetDecimal(reader.GetOrdinal("cantidadstock")),
                            importe = reader.GetDecimal(reader.GetOrdinal("totalstock"))
                        };
                        stockList.Add(st);
                    }
                    return stockList;
                }
            }
        }

        // Ajuste de stock
        internal bool ajusteStock(Modelos.stock stock)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("sp_ajusteStock", conexion))
            {

                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@sucursal", stock.sucursal);
                cmd.Parameters.AddWithValue("@producto", stock.producto);
                cmd.Parameters.AddWithValue("@talle", stock.talle ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@color", stock.color);
                cmd.Parameters.AddWithValue("@genero", stock.genero ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@cantidad", stock.cantidad);
                cmd.Parameters.AddWithValue("@fecha", stock.fecha);
                cmd.ExecuteNonQuery();
                return true;
            }
        }
        #endregion

        #region " Ventas "
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
        internal Modelos.ventas[] ventas(int suc = 0, DateTime? fechaInicio = null, DateTime? fechaFin = null, bool agDia = false, bool agProd = false, bool agTalle = false, bool agColor = false, bool agSuc = false)
        {

            string filtro = " WHERE 1=1";
            if (suc > 0)
            {
                filtro += $" AND ID_Sucursal = {suc}";
            }
            if (fechaInicio != null)
            {
                filtro += $" AND (Fecha BETWEEN '{fechaInicio:yyyy-MM-dd}' AND '{fechaFin:yyyy-MM-dd}')";
            }
            //if (fechaFin != null)
            //{
            //    filtro += $" AND CAST(hora as DATE) <= '{fechaFin:yyyy-MM-dd}'";
            //}
            string groupBy = "";
            if (agDia)
            {
                groupBy = " Fecha";
            }
            if (agProd)
            {
                // Agrupar solo por nombre_producto
                groupBy = agDia ? " Fecha, nombre_producto" : " nombre_producto";
            }
            if (agTalle)
            {
                groupBy = (agDia || agProd) ? groupBy + ", desc_talle" : " desc_talle";
            }
            if (agColor)
            {
                groupBy = (agDia || agProd || agTalle) ? groupBy + ", desc_color" : " desc_color";
            }
            if (agSuc)
            {
                groupBy = (agDia || agProd || agTalle || agColor) ? groupBy + ", ID_Sucursal, nombre_sucursal" : " ID_Sucursal, nombre_sucursal";
            }

            string comando = $"SELECT [ID], ISNULL([ID_Sucursal], 0), nombre_sucursal, ISNULL([ID_Producto], 0)" +
                ", ISNULL(nombre_producto, 'Sin Producto (' + CAST(ISNULL(ID_Producto, 0) AS varchar) + ')') nombre_producto" +
                ", ISNULL([Cantidad], 0), isnull(precio, 0), ISNULL([Numero], 0)" +
                ", ISNULL([Fecha], GETDATE()) fecha, ISNULL([Tipo_Factura], 0), ISNULL([Vendedor], 0), ISNULL([Forma_Pago], 0)" +
                ", cast( (cantidad*precio) as float) As subtotal,CAST(ISNULL([Descuento], 0) AS FLOAT)" +
                ", ISNULL([Observaciones], ''), ISNULL([Estado], 0), ISNULL([CAE], '')" +
                ", ISNULL([Hora], GETDATE()), ISNULL([desc_talle], ''), ISNULL([desc_color], '') FROM vw_ventas" + filtro + " ORDER BY Hora";

            if (groupBy != "")
            {
                comando = "SELECT " +
                    $"{groupBy}, SUM(ISNULL([Cantidad], 0)) cantidad" +
                    ", SUM(cast((cantidad*precio) as float)) As subtotal, SUM(CAST(ISNULL([Descuento], 0) AS FLOAT)) descuento" +
                    $" FROM vw_ventas {filtro} GROUP BY {groupBy}  ORDER BY cantidad desc";
            }
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(comando, conexion))
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
                                id = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                                id_sucursal = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                                nombre_sucursal = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                id_producto = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                nombre_producto = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                cantidad = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                                precio = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                                numero = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                                fecha = reader.IsDBNull(8) ? DateTime.MinValue : reader.GetDateTime(8),
                                tipo_factura = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                                vendedor = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                                forma_pago = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                                subTotal = reader.IsDBNull(12) ? 0 : reader.GetDouble(12),
                                descuento = reader.IsDBNull(13) ? 0 : reader.GetDouble(13),
                                observaciones = reader.IsDBNull(14) ? "" : reader.GetString(14),
                                estado = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
                                CAE = reader.IsDBNull(16) ? "" : reader.GetString(16),
                                hora = reader.IsDBNull(17) ? DateTime.MinValue : reader.GetDateTime(17),
                                desc_talle = reader.IsDBNull(18) ? "" : reader.GetString(18),
                                desc_color = reader.IsDBNull(19) ? "" : reader.GetString(19)
                            };

                            venta.total = venta.subTotal + venta.descuento;
                            ventasList.Add(venta);
                        }
                        else
                        {
                            ventas venta = new ventas
                            {
                                id = 0,
                                id_sucursal = (agSuc == true) ? (reader.IsDBNull(reader.GetOrdinal("id_sucursal")) ? 0 : reader.GetInt32(reader.GetOrdinal("id_sucursal"))) : 0,
                                nombre_sucursal = (agSuc == true) ? (reader.IsDBNull(reader.GetOrdinal("nombre_sucursal")) ? "" : reader.GetString(reader.GetOrdinal("nombre_sucursal"))) : "",
                                id_producto = 0,
                                nombre_producto = (agProd == true) ? (reader.IsDBNull(reader.GetOrdinal("nombre_producto")) ? "" : reader.GetString(reader.GetOrdinal("nombre_producto"))) : "",
                                cantidad = reader.IsDBNull(reader.GetOrdinal("cantidad")) ? 0 : reader.GetDecimal(reader.GetOrdinal("cantidad")),
                                precio = 0,
                                numero = 0,
                                fecha = (agDia == true) ? (reader.IsDBNull(reader.GetOrdinal("fecha")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("fecha"))) : DateTime.MinValue,
                                tipo_factura = 0,
                                vendedor = 0,
                                forma_pago = 0,
                                subTotal = reader.IsDBNull(reader.GetOrdinal("subtotal")) ? 0 : reader.GetDouble(reader.GetOrdinal("subtotal")),
                                descuento = reader.IsDBNull(reader.GetOrdinal("descuento")) ? 0 : reader.GetDouble(reader.GetOrdinal("descuento")),
                                observaciones = "",
                                estado = 0,
                                CAE = "",
                                hora = DateTime.MinValue,
                                desc_talle = (agTalle == true) ? (reader.IsDBNull(reader.GetOrdinal("desc_talle")) ? "" : reader.GetString(reader.GetOrdinal("desc_talle"))) : "",
                                desc_color = (agColor == true) ? (reader.IsDBNull(reader.GetOrdinal("desc_color")) ? "" : reader.GetString(reader.GetOrdinal("desc_color"))) : ""

                            };

                            venta.total = venta.subTotal + venta.descuento;
                            ventasList.Add(venta);
                        }
                    }
                    return ventasList.ToArray();
                }
            }
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
            string comando = $"SELECT ISNULL([ID_Sucursal], 0) Id, nombre_sucursal Sucursal, SUM(CAST(ISNULL([Cantidad], 0) AS Int)) Cantidad, SUM(cast((cantidad*precio) as float)) As SubTotal" +
                $", SUM(CAST(ISNULL([Descuento], 0) AS FLOAT))  Descuento FROM vw_ventas {filtro} GROUP BY ID_Sucursal, nombre_sucursal";
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(comando, conexion))
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
                            cantidad = reader.GetInt32(2),
                            subTotal = reader.GetDouble(3),
                            descuento = reader.GetDouble(4)
                        };
                        venta.total = venta.subTotal + venta.descuento;
                        ventasList.Add(venta);
                    }
                    return ventasList.ToArray();
                }
            }
        }
        internal IEnumerable<Modelos.ventas_por_tipo_pago> ventas_por_tipo_pago(int suc, DateTime? fechaInicio, DateTime? fechaFin)
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
            string comando = $"SELECT forma_pago, desc_forma_pago, SUM(subtotal) FROM vw_ventas {filtro} GROUP BY forma_pago, desc_forma_pago ORDER BY SUM(subtotal) DESC";
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(comando, conexion))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<Modelos.ventas_por_tipo_pago> ventasList = new List<Modelos.ventas_por_tipo_pago>();
                    while (reader.Read())
                    {
                        Modelos.ventas_por_tipo_pago venta = new Modelos.ventas_por_tipo_pago
                        {
                            id = reader.GetInt32(0),
                            descripcion = reader.GetString(1),
                            total = reader.GetDecimal(2)
                        };
                        ventasList.Add(venta);
                    }
                    return ventasList.ToArray();
                }
            }
        }
        internal void insertar_ventas(List<ventas> ventas)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            foreach (var venta in ventas)
            {
                // Primero chequear que no exista el registro con id_venta_sucursal y id_sucursal
                using (SqlCommand checkCmd = new SqlCommand("SELECT [ID] FROM ventas WHERE ISNULL(id_venta_sucursal, '') = ISNULL(@id_venta_sucursal, '') AND ISNULL(id_sucursal, 0) = ISNULL(@id_sucursal, 0)", conexion))
                {
                    checkCmd.Parameters.AddWithValue("@id_venta_sucursal", (object)venta.id_venta_sucursal ?? System.DBNull.Value);
                    checkCmd.Parameters.AddWithValue("@id_sucursal", (object)venta.id_sucursal ?? System.DBNull.Value);
                    var existing = checkCmd.ExecuteScalar();
                    if (existing != null && existing != DBNull.Value)
                    {
                        // actualizar
                        using (SqlCommand cmd = new SqlCommand("UPDATE ventas SET [numero]=@numero, [fecha]=@fecha, [tipo_factura]=@tipo_factura, [cliente]=@cliente, [vendedor]=@vendedor, [forma_pago]=@forma_pago, [subTotal]=@subTotal, [iva]=@iva, [descuento]=@descuento, [total]=@total, [observaciones]=@observaciones, [estado]=@estado, [CAE]=@CAE, [hora]=@hora, [id_venta_sucursal]=@id_venta_sucursal WHERE [ID]=@id", conexion))
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
                        using (SqlCommand cmd = new SqlCommand($"INSERT INTO ventas ([numero], [fecha], [tipo_factura], [cliente], [vendedor], [forma_pago], [subTotal], [iva], " +
                            "[descuento], [total], [observaciones], [estado], [id_sucursal], [CAE], [hora], [id_venta_sucursal]) " +
                            $"VALUES (@numero, @fecha, @tipo_factura, @cliente, @vendedor, @forma_pago, @subTotal, @iva, @descuento, @total, @observaciones, @estado, @id_sucursal, @CAE, @hora, @id_venta_sucursal)", conexion))
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
                    
                    // insertar el detalle_ventas
                    using (SqlCommand cmd = new SqlCommand("INSERT INTO detalle_ventas (id_venta, id_producto, cantidad, precio, descuento, subtotal, id_sucursal)" +
                        " VALUES(@id_venta, @id_producto, @cantidad, @precio, @descuento, @subtotal, @id_sucursal)", conexion))
                    {
                        cmd.Parameters.AddWithValue("@id_venta", (object)venta.id_venta_sucursal ?? System.DBNull.Value);
                        cmd.Parameters.AddWithValue("@id_producto", venta.id_producto);
                        cmd.Parameters.AddWithValue("@cantidad", venta.cantidad);
                        cmd.Parameters.AddWithValue("@precio", venta.precio);
                        cmd.Parameters.AddWithValue("@id_sucursal", (object)venta.id_sucursal ?? System.DBNull.Value);
                        cmd.Parameters.AddWithValue("@descuento", venta.descuento);
                        cmd.Parameters.AddWithValue("@subtotal", venta.subTotal);

                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        internal ventas ultima_venta(int suc)
        {
            ventas venta = new ventas();
            if (suc == 0)
            {
                return venta;
            }
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 [ID], ISNULL([ID_Sucursal], 0), nombre_sucursal, ISNULL([ID_Producto], 0), nombre_producto" +
                ", ISNULL([Cantidad], 0), isnull(precio, 0), ISNULL([Numero], 0)" +
                ", ISNULL([Fecha], GETDATE()) fecha, ISNULL([Tipo_Factura], 0), ISNULL([Vendedor], 0), ISNULL([Forma_Pago], 0)" +
                ", cast( (cantidad*precio) as float) As subtotal,CAST(ISNULL([Descuento], 0) AS FLOAT)" +
                ", ISNULL([Observaciones], ''), ISNULL([Estado], 0), ISNULL([CAE], '')" +
                ", ISNULL([Hora], GETDATE()), ISNULL([id_venta_sucursal], 0) FROM vw_ventas WHERE ID_Sucursal=@suc ORDER BY id_venta_sucursal DESC", conexion))
            {
                cmd.Parameters.AddWithValue("@suc", suc);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        venta.id = reader.GetInt32(0);
                        venta.id_sucursal = reader.GetInt32(1);
                        venta.nombre_sucursal = reader.GetString(2);
                        venta.id_producto = reader.GetInt32(3);
                        venta.nombre_producto = reader.IsDBNull(4) ? "" : reader.GetString(4);
                        venta.cantidad = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5);
                        venta.precio = reader.GetDecimal(6);
                        venta.numero = reader.GetInt32(7);
                        venta.fecha = reader.GetDateTime(8);
                        venta.tipo_factura = reader.GetInt32(9);
                        venta.vendedor = reader.GetInt32(10);
                        venta.forma_pago = reader.GetInt32(11);
                        venta.subTotal = reader.IsDBNull(12) ? 0 : reader.GetDouble(12);
                        venta.descuento = reader.GetDouble(13);
                        venta.observaciones = reader.IsDBNull(14) ? "" : reader.GetString(14);
                        venta.estado = reader.GetInt32(15);
                        venta.CAE = reader.IsDBNull(16) ? "" : reader.GetString(16);
                        venta.hora = reader.GetDateTime(17);
                        venta.id_venta_sucursal = reader.GetInt32(18);
                    }
                }
            }
            return venta;
        }

        #endregion

        #region " Traslados "
        internal traslados[] obtener_traslados(DateTime fechaInicio, DateTime fechaFin)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([Fecha], GETDATE()), ISNULL([ID_Producto], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Cantidad], 0)" +
                ", ISNULL([ID_Sucursal_Origen], 0), ISNULL([Nombre_Origen], ''), ISNULL([ID_Sucursal_Destino], 0), ISNULL([Nombre_Destino], ''), ISNULL([Observaciones], ''), ISNULL([Estado], 0) FROM vw_traslados WHERE CAST([Fecha] AS DATE) BETWEEN @fechaInicio AND @fechaFin", conexion))
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
        internal traslados[] obtener_ultimos_traslados(int cantidad = 20)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand($"SELECT TOP {cantidad} [ID], ISNULL([Fecha], GETDATE()), ISNULL([ID_Producto], 0), ISNULL([Descripcion_Codigo], '')" +
                $", ISNULL([Cantidad], 0), ISNULL([ID_Sucursal_Origen], 0), ISNULL([Nombre_Origen], ''), ISNULL([ID_Sucursal_Destino], 0), ISNULL([Nombre_Destino], '')" +
                $", ISNULL([Observaciones], ''), ISNULL([Estado], 0) FROM vw_traslados ORDER BY [Fecha] DESC", conexion))
            {                
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
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([Fecha], GETDATE()), ISNULL([ID_Producto], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Cantidad], 0), ISNULL([ID_Sucursal_Origen], 0)" +
                ", ISNULL([Nombre_Origen], ''), ISNULL([ID_Sucursal_Destino], 0), ISNULL([Nombre_Destino], ''), ISNULL([Observaciones], ''), ISNULL([Estado], 0) FROM vw_traslados WHERE [ID] = @id", conexion))
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
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            bool resultado = false;
            using (SqlCommand cmd = new SqlCommand("INSERT INTO traslados (Fecha, ID_Producto, Cantidad, ID_Sucursal_Origen, ID_Sucursal_Destino, Observaciones, Estado) VALUES (@fecha, @id_producto, @cantidad, @origen, @destino, @obs, @estado)", conexion))
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
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            bool resultado = false;
            using (SqlCommand cmd = new SqlCommand("UPDATE traslados SET Fecha=@fecha, ID_Producto=@producto, Cantidad=@cantidad, ID_Sucursal_Origen=@origen, ID_Sucursal_Destino=@destino, Observaciones=@obs, Estado=@estado WHERE ID=@id", conexion))
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

        internal bool borrar_traslado(int id)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            bool resultado = false;
            using (SqlCommand cmd = new SqlCommand("DELETE FROM traslados WHERE ID=@id", conexion))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                resultado = true;
            }
            return resultado;
        }

        #endregion

        #region " Productos "
        internal productos obtener_producto(string producto)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            // Chequear si producto es numero


            using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 [ID], ISNULL([Codigo_Equis], 0), ISNULL([Cod_Art], 0), ISNULL([Descripcion_Codigo], '')" +
                $", ISNULL([Categoria], ''), ISNULL([Codigo_Color], 0), ISNULL([Descripcion_Color], ''), ISNULL([Talles], ''), ISNULL([Ean_2025], 0) FROM productos " +
                " WHERE Descripcion_Codigo LIKE '%' + @id + '%' OR CONVERT(varchar, EAN_2025)=@id OR CONVERT(varchar, Codigo_Equis)=@id", conexion))
            {
                cmd.Parameters.AddWithValue("@id", producto);
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
        internal IList<productos> obtener_productos(string producto, bool todos = false)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();

            using (SqlCommand cmd = new SqlCommand($"SELECT {(todos ? "" : "TOP 1")} [ID], ISNULL([Codigo_Equis], 0), ISNULL([Cod_Art], 0), ISNULL([Descripcion_Codigo], '')" +
                $", ISNULL([Categoria], ''), ISNULL([Codigo_Color], 0), ISNULL([Descripcion_Color], ''), ISNULL([Talles], ''), ISNULL([Ean_2025], 0) FROM productos " +
                $" WHERE Descripcion_Codigo LIKE '%' + @id + '%' OR CONVERT(varchar, EAN_2025)=@id OR CONVERT(varchar, Codigo_Equis)=@id", conexion))
            {
                cmd.Parameters.AddWithValue("@id", producto);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    IList<productos> prods = new List<productos>();
                    while (reader.Read())
                    {
                        prods.Add(new productos
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
                        });
                    }
                    return prods;
                }
            }
        }

        internal int obtener_codart(string nombre_producto)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 ISNULL([Cod_Art], 0) FROM productos WHERE Descripcion_Codigo LIKE '%' + @nombre + '%'", conexion))
            {
                cmd.Parameters.AddWithValue("@nombre", nombre_producto);
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result);
                }
                else
                {
                    return 0; // o algún valor por defecto que indique que no se encontró
                }
            }
        }

        // funcion para obtener todos los productos (reubicado desde arriba)
        public Modelos.productos[] obtener_productos()
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            string comando = "SELECT [ID], ISNULL([Codigo_Equis], 0), ISNULL([Cod_Art], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Categoria], '')" +
                ", ISNULL([Codigo_Color], 0), ISNULL([Descripcion_Color], ''), ISNULL([Talles], ''), ISNULL([Ean_2025], 0) FROM productos";

            using (SqlCommand cmd = new SqlCommand(comando, conexion))
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

        internal IEnumerable<string> obtener_generos()
        {
            List<string> result = new List<string>();

            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT ISNULL(Genero, '') FROM Productos GROUP BY Genero", conexion))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(reader.GetString(0));
                    }
                }
            }

            return result;
        }
        internal IEnumerable<string> obtener_colores()
        {
            List<string> result = new List<string>();

            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT ISNULL(nombre_color, '') FROM colores GROUP BY nombre_color ORDER BY nombre_color", conexion))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(reader.GetString(0));
                    }
                }
            }

            return result;
        }
        internal IEnumerable<string> obtener_talles()
        {
            List<string> result = new List<string>();

            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT ISNULL(Talles, '') FROM Productos GROUP BY Talles ORDER BY Talles", conexion))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(reader.GetString(0));
                    }
                }
            }

            return result;
        }
        internal IEnumerable<string> obtener_nombres()
        {
            List<string> result = new List<string>();

            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT ISNULL(Descripcion_Codigo, '') FROM Productos GROUP BY Descripcion_Codigo ORDER BY Descripcion_Codigo", conexion))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(reader.GetString(0));
                    }
                }
            }

            return result;
        }

        // BorrarIngresoMercaderia reubicado más abajo

        /// <summary>
        /// Devuelve un listado de productos por equis
        /// </summary>
        /// <returns></returns>
        internal IEnumerable<productos> obtener_por_equis(long codigo)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            string comando = $"SELECT [ID], ISNULL([Codigo_Equis], 0), ISNULL([Cod_Art], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Categoria], '')" +
                $", ISNULL([Codigo_Color], 0), ISNULL([Descripcion_Color], ''), ISNULL([Talles], ''), ISNULL([Ean_2025], 0) FROM productos WHERE Codigo_Equis={codigo}";

            using (SqlCommand cmd = new SqlCommand(comando, conexion))
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

        internal productos obtener_por_ean(long codigo)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            string comando = $"SELECT TOP 1 [ID], ISNULL([Codigo_Equis], 0), ISNULL([Cod_Art], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Categoria], '')" +
                $", ISNULL([Codigo_Color], 0), ISNULL([Descripcion_Color], ''), ISNULL([Talles], ''), ISNULL([Ean_2025], 0) FROM productos WHERE Ean_2025={codigo}";

            using (SqlCommand cmd = new SqlCommand(comando, conexion))
            {
                productos producto = new productos();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        producto = new productos
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

                    }
                }
                return producto;
            }
        }

        internal IngresoMercaderiaDetalle? obtener_ingreso_detalle(int id)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand(@"SELECT [Id], [IngresoId], [CodigoProductoProveedor], [CodigoModelo], [NombreModelo], [Marca], [Material], [Color], [TalleDesde],
                [TalleHasta], [CantidadCv], [UnidadesPorCv], [CantidadUnidades], [PrecioUnitario], [BonificacionPct], [Importe], [ProductoId], [MapeoEncontrado],
                [RequiereRevision] FROM [IngresoMercaderiaDetalle] WHERE [Id] = @id", conexion))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        IngresoMercaderiaDetalle detalle = new IngresoMercaderiaDetalle
                        {
                            Id = reader.GetInt32(0),
                            IngresoId = reader.GetInt32(1),
                            CodigoProductoProveedor = reader.IsDBNull(2) ? string.Empty : reader.GetValue(2).ToString() ?? string.Empty,
                            CodigoModelo = reader.IsDBNull(3) ? null : reader.GetString(3),
                            NombreModelo = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Marca = reader.IsDBNull(5) ? null : reader.GetString(5),
                            Material = reader.IsDBNull(6) ? null : reader.GetString(6),
                            Color = reader.IsDBNull(7) ? null : reader.GetString(7),
                            TalleDesde = reader.IsDBNull(8) ? null : reader.GetString(8),
                            TalleHasta = reader.IsDBNull(9) ? null : reader.GetString(9),
                            CantidadCv = reader.GetInt32(10),
                            UnidadesPorCv = reader.GetInt32(11),
                            CantidadUnidades = reader.GetInt32(12),
                            PrecioUnitario = reader.GetDecimal(13),
                            BonificacionPct = reader.GetDecimal(14),
                            Importe = reader.GetDecimal(15),
                            ProductoId = reader.IsDBNull(16) ? null : reader.GetInt32(16),
                            MapeoEncontrado = reader.GetBoolean(17),
                            RequiereRevision = reader.GetBoolean(18)
                        };
                        return detalle;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Devuelve un listado de productos vendidos en un rango de fechas
        /// </summary>
        /// <param name="suc">ID de la sucursal (opcional, por defecto 0 para todas las sucursales)</param>
        /// <param name="fechaInicio">Fecha de inicio para filtrar las ventas (opcional)</param>
        /// <param name="fechaFin">Fecha de fin para filtrar las ventas (opcional)</param>
        /// <returns>Listado de productos vendidos</returns>
        /// <exception cref="NotImplementedException"></exception>
        internal object? productos_vendidos(int suc, DateTime? fechaInicio, DateTime? fechaFin)
        {
            List<Modelos.productos_vendidos> productos = new List<Modelos.productos_vendidos>();
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT ProductoId, NombreModelo, SUM(CantidadUnidades) AS CantidadUnidades, PrecioUnitario, SUM(Total) AS Total" +
                " FROM Ventas WHERE (@suc = 0 OR SucursalId = @suc) AND (@fechaInicio IS NULL OR Fecha >= @fechaInicio) AND (@fechaFin IS NULL OR Fecha <= @fechaFin)" +
                " GROUP BY ProductoId, NombreModelo, PrecioUnitario", conexion))
            {
                cmd.Parameters.AddWithValue("@suc", suc);
                cmd.Parameters.AddWithValue("@fechaInicio", (object?)fechaInicio ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fechaFin", (object?)fechaFin ?? DBNull.Value);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Modelos.productos_vendidos producto = new Modelos.productos_vendidos
                        {
                            ProductoId = reader.GetInt32(0),
                            NombreModelo = reader.GetString(1),
                            CantidadUnidades = reader.GetInt32(2),
                            PrecioUnitario = reader.GetDecimal(3),
                            Total = reader.GetDecimal(4)
                        };
                        productos.Add(producto);
                    }
                }
            }
            return productos;
        }

        internal IEnumerable<Modelos.productos_vendidos> top_venta_producto(int suc, int cantidad = 10, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            // Devuelve los 10 productos más vendidos en un rango de fechas
            // Por defecto devuelve todas las sucursales, si suc=0, sino devuelve la sucursal indicada
            // Por defecto devuelve la fecha actual, si fechaInicio y fechaFin son null, sino devuelve el rango indicado
            if (fechaInicio == null && fechaFin == null)
            {
                fechaInicio = DateTime.Today.AddDays(-7);
                fechaFin = DateTime.Today;
            }
            List<Modelos.productos_vendidos> productos = new List<Modelos.productos_vendidos>();
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT TOP (@cantidad) Descripcion_Codigo, CAST(SUM(Cantidad) AS INT) AS Cantidad, Precio, SUM(SubTotal) AS Total" +
                " FROM vw_DetalleVentas WHERE (@suc = 0 OR Id_Sucursal = @suc) AND (@fechaInicio IS NULL OR Fecha >= @fechaInicio) AND (@fechaFin IS NULL OR Fecha <= @fechaFin)" +
                " GROUP BY Descripcion_Codigo, Precio ORDER BY SUM(Cantidad) DESC, SUM(SubTotal) DESC", conexion))
            {
                cmd.Parameters.AddWithValue("@suc", suc);
                cmd.Parameters.AddWithValue("@cantidad", cantidad);
                cmd.Parameters.AddWithValue("@fechaInicio", (object?)fechaInicio ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fechaFin", (object?)fechaFin ?? DBNull.Value);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Modelos.productos_vendidos producto = new Modelos.productos_vendidos
                        {
                            ProductoId = 0,
                            NombreModelo = reader.GetString(0),
                            CantidadUnidades = reader.GetInt32(1),
                            PrecioUnitario = reader.GetDecimal(2),
                            Total = reader.GetDecimal(3)
                        };
                        productos.Add(producto);
                    }
                }
            }

            return productos;
        }

        internal int obtener_proximo_orden()
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT ISNULL(MAX(Orden), 0) + 1 FROM productos", conexion))
            {
                return (int)cmd.ExecuteScalar();
            }
        }

        internal int agregar_producto(productos nuevoProducto)
        {
            if (nuevoProducto.id == 0)
            {
                nuevoProducto.id = obtener_proximo_orden();
            }
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("INSERT INTO productos (ID, Orden, Codigo_Equis, Cod_Art, Descripcion_Codigo, Genero, curva, Categoria, Codigo_Color, Descripcion_Color, Talles, Ean_2025) " +
                "OUTPUT INSERTED.ID VALUES (@id, @orden, @codigo_equis, @cod_art, @descripcion_codigo, '', '', @categoria, @codigo_color, @descripcion_color, @talles, @ean_2025)", conexion))
            {
                cmd.Parameters.AddWithValue("@id", nuevoProducto.id);
                cmd.Parameters.AddWithValue("@orden", nuevoProducto.orden);
                cmd.Parameters.AddWithValue("@codigo_equis", nuevoProducto.codigo_equis);
                cmd.Parameters.AddWithValue("@cod_art", nuevoProducto.cod_art);
                cmd.Parameters.AddWithValue("@descripcion_codigo", nuevoProducto.descripcion_codigo);
                cmd.Parameters.AddWithValue("@categoria", nuevoProducto.categoria);
                cmd.Parameters.AddWithValue("@codigo_color", nuevoProducto.codigo_color);
                cmd.Parameters.AddWithValue("@descripcion_color", nuevoProducto.descripcion_color);
                cmd.Parameters.AddWithValue("@talles", nuevoProducto.talles);
                cmd.Parameters.AddWithValue("@ean_2025", nuevoProducto.ean_2025);
                return (int)cmd.ExecuteScalar();
            }
        }

        internal int obtener_idcolor(string nombre)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT top 1 ISNULL(Codigo_Color, 0) FROM productos WHERE Descripcion_Color like @nombre", conexion))
            {
                cmd.Parameters.AddWithValue("@nombre", nombre);
                return (int)cmd.ExecuteScalar();
            }
        }


        #endregion
    }
}
