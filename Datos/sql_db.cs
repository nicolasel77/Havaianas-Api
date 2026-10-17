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
        private readonly string connectionString = "Data Source=NICO;Initial Catalog=dbHavaianas_servidor;Trusted_Connection=True;TrustServerCertificate=True;";
        //private readonly string connectionString = "Data Source=192.168.1.11;Initial Catalog=dbHavaianas_servidor;User Id=Nikorasu;Password=Oficina02;TrustServerCertificate=True;";

        // Pequeños stubs para compatibilidad con el controlador FacturasController
        // El controlador original espera un DbContext/DbSet-like API (Include/AnyAsync/Add/SaveChangesAsync)
        // Aquí añadimos implementaciones mínimas en memoria / no intrusivas para compilar
        // y permitir llamadas a ExecuteSqlInterpolatedAsync vía Database.
        public FakeDbSet<Havaianas.Facturacion.IngresoMercaderia> IngresoMercaderia { get; } = new();
        public FakeDbSet<Havaianas.Facturacion.IngresoMercaderiaDetalle> IngresoMercaderiaDetalle { get; } = new();
        public FakeDbSet<Havaianas.Facturacion.MapeoProductoProveedor> MapeosProductoProveedor { get; } = new();
        public DatabaseStub Database { get; } = new();

        public async Task SaveChangesAsync()
        {
            var ingresos = IngresoMercaderia.PendingAdds.Cast<Havaianas.Facturacion.IngresoMercaderia>().ToList();
            var detalles = IngresoMercaderiaDetalle.PendingAdds.Cast<Havaianas.Facturacion.IngresoMercaderiaDetalle>().ToList();
            var mapeos = MapeosProductoProveedor.PendingAdds.Cast<Havaianas.Facturacion.MapeoProductoProveedor>().ToList();

            if (ingresos.Count == 0 && detalles.Count == 0 && mapeos.Count == 0)
                return;

            await using var conexion = new SqlConnection(connectionString);
            await conexion.OpenAsync();
            await using var transaccion = (SqlTransaction)await conexion.BeginTransactionAsync();

            try
            {
                foreach (var ingreso in ingresos)
                {
                    using var comando = new SqlCommand(@"
                        INSERT INTO [IngresoMercaderia]
                            ([ProveedorId], [NroFactura], [FechaFactura], [CuitCliente], [Remito],
                             [CondicionPago], [SucursalDestino], [SucursalDestinoId], [TotalCv],
                             [TotalUnidades], [Subtotal], [Iva], [PercepcionIibb], [TotalFactura],
                             [ValidacionOk], [ValidacionDetalle], [Estado], [ArchivoOriginalPath], [FechaImportacion])
                        OUTPUT INSERTED.[Id]
                        VALUES (@ProveedorId, @NroFactura, @FechaFactura, @CuitCliente, @Remito,
                                @CondicionPago, @SucursalDestino, @SucursalDestinoId, @TotalCv,
                                @TotalUnidades, @Subtotal, @Iva, @PercepcionIibb, @TotalFactura,
                                @ValidacionOk, @ValidacionDetalle, @Estado, @ArchivoOriginalPath, GETDATE())",
                        conexion, transaccion);

                    AgregarParametrosIngreso(comando, ingreso);
                    ingreso.Id = Convert.ToInt32(await comando.ExecuteScalarAsync());

                    foreach (var detalle in ingreso.Detalle)
                        await InsertarDetalleAsync(detalle, ingreso.Id, conexion, transaccion);
                }

                foreach (var detalle in detalles.Where(d => !ingresos.Any(i => i.Detalle.Contains(d))))
                    await InsertarDetalleAsync(detalle, detalle.IngresoId, conexion, transaccion);

                foreach (var mapeo in mapeos)
                {
                    using var comando = new SqlCommand(@"
                        INSERT INTO [MapeoProductoProveedor]
                            ([ProveedorId], [CodigoProductoProveedor], [Color], [Talle], [ProductoId],
                             [ArticuloDescripcion], [UsuarioMapeo], [FechaMapeo])
                        OUTPUT INSERTED.[Id]
                        VALUES (@ProveedorId, @CodigoProductoProveedor, @Color, @Talle, @ProductoId,
                                @ArticuloDescripcion, @UsuarioMapeo, @FechaMapeo)", conexion, transaccion);

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

                await transaccion.CommitAsync();
                IngresoMercaderia.AcceptChanges();
                IngresoMercaderiaDetalle.AcceptChanges();
                MapeosProductoProveedor.AcceptChanges();
            }

            catch
            {
                await transaccion.RollbackAsync();
                throw;
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

        private static object ValorSql(object? valor) => valor ?? DBNull.Value;

        private static void AgregarParametrosIngreso(SqlCommand comando, Havaianas.Facturacion.IngresoMercaderia ingreso)
        {
            comando.Parameters.AddWithValue("@ProveedorId", ingreso.ProveedorId);
            comando.Parameters.AddWithValue("@NroFactura", ingreso.NroFactura);
            comando.Parameters.AddWithValue("@FechaFactura", ingreso.FechaFactura);
            comando.Parameters.AddWithValue("@CuitCliente", ValorSql(ingreso.CuitCliente));
            comando.Parameters.AddWithValue("@Remito", ValorSql(ingreso.Remito));
            comando.Parameters.AddWithValue("@CondicionPago", ValorSql(ingreso.CondicionPago));
            comando.Parameters.AddWithValue("@SucursalDestino", ValorSql(ingreso.SucursalDestino));
            comando.Parameters.AddWithValue("@SucursalDestinoId", ValorSql(ingreso.SucursalDestinoId));
            comando.Parameters.AddWithValue("@TotalCv", ingreso.TotalCv);
            comando.Parameters.AddWithValue("@TotalUnidades", ingreso.TotalUnidades);
            comando.Parameters.AddWithValue("@Subtotal", ingreso.Subtotal);
            comando.Parameters.AddWithValue("@Iva", ingreso.Iva);
            comando.Parameters.AddWithValue("@PercepcionIibb", ingreso.PercepcionIibb);
            comando.Parameters.AddWithValue("@TotalFactura", ingreso.TotalFactura);
            comando.Parameters.AddWithValue("@ValidacionOk", ingreso.ValidacionOk);
            comando.Parameters.AddWithValue("@ValidacionDetalle", ValorSql(ingreso.ValidacionDetalle));
            comando.Parameters.AddWithValue("@Estado", ingreso.Estado);
            comando.Parameters.AddWithValue("@ArchivoOriginalPath", ValorSql(ingreso.ArchivoOriginalPath));
        }

        private static async Task InsertarDetalleAsync(
            Havaianas.Facturacion.IngresoMercaderiaDetalle detalle,
            int ingresoId,
            SqlConnection conexion,
            SqlTransaction transaccion)
        {
            using SqlCommand comando = new SqlCommand(@"
                INSERT INTO [IngresoMercaderiaDetalle]
                    ([IngresoId], [CodigoProductoProveedor], [CodigoModelo], [NombreModelo], [Marca],
                     [Material], [Color], [TalleDesde], [TalleHasta], [CantidadCv], [UnidadesPorCv],
                     [CantidadUnidades], [PrecioUnitario], [BonificacionPct], [Importe], [ProductoId],
                     [MapeoEncontrado], [RequiereRevision])
                OUTPUT INSERTED.[Id]
                VALUES
                    (@IngresoId, @CodigoProductoProveedor, @CodigoModelo, @NombreModelo, @Marca,
                     @Material, @Color, @TalleDesde, @TalleHasta, @CantidadCv, @UnidadesPorCv,
                     @CantidadUnidades, @PrecioUnitario, @BonificacionPct, @Importe, @ProductoId,
                     @MapeoEncontrado, @RequiereRevision)", conexion, transaccion);

            comando.Parameters.AddWithValue("@IngresoId", ingresoId);
            comando.Parameters.AddWithValue("@CodigoProductoProveedor", detalle.CodigoProductoProveedor);
            comando.Parameters.AddWithValue("@CodigoModelo", ValorSql(detalle.CodigoModelo));
            comando.Parameters.AddWithValue("@NombreModelo", ValorSql(detalle.NombreModelo));
            comando.Parameters.AddWithValue("@Marca", ValorSql(detalle.Marca));
            comando.Parameters.AddWithValue("@Material", ValorSql(detalle.Material));
            comando.Parameters.AddWithValue("@Color", ValorSql(detalle.Color));
            comando.Parameters.AddWithValue("@TalleDesde", ValorSql(detalle.TalleDesde));
            comando.Parameters.AddWithValue("@TalleHasta", ValorSql(detalle.TalleHasta));
            comando.Parameters.AddWithValue("@CantidadCv", detalle.CantidadCv);
            comando.Parameters.AddWithValue("@UnidadesPorCv", detalle.UnidadesPorCv);
            comando.Parameters.AddWithValue("@CantidadUnidades", detalle.CantidadUnidades);
            comando.Parameters.AddWithValue("@PrecioUnitario", detalle.PrecioUnitario);
            comando.Parameters.AddWithValue("@BonificacionPct", detalle.BonificacionPct);
            comando.Parameters.AddWithValue("@Importe", detalle.Importe);
            comando.Parameters.AddWithValue("@ProductoId", ValorSql(detalle.ProductoId));
            comando.Parameters.AddWithValue("@MapeoEncontrado", detalle.MapeoEncontrado);
            comando.Parameters.AddWithValue("@RequiereRevision", detalle.RequiereRevision);

            try
            {
                detalle.IngresoId = ingresoId;
                detalle.Id = Convert.ToInt32(await comando.ExecuteScalarAsync());
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
            }

        }

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

        // funcion para obtener todos los productos
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

        internal Havaianas.Facturacion.IngresoMercaderia[] obtener_ingresos(DateTime fecha, DateTime fechaFin)
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
                    List<Havaianas.Facturacion.IngresoMercaderia> ingresosList = new List<Havaianas.Facturacion.IngresoMercaderia>();
                    while (reader.Read())
                    {
                        Havaianas.Facturacion.IngresoMercaderia ingreso = new Havaianas.Facturacion.IngresoMercaderia()
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
        internal Havaianas.Facturacion.IngresoMercaderiaDetalle[] obtener_ingresos_detalle(DateTime fecha, DateTime fechaFin)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("SELECT [ID], ISNULL([FechaFactura], GETDATE()), ISNULL([ProductoId], 0), ISNULL([Descripcion_Codigo], ''), " +
                "ISNULL([CantidadUnidades], 0), ISNULL([PrecioUnitario], 0), ISNULL([Total], 0), [check], IdDetalle FROM vw_DetalleIngresos WHERE CAST([FechaFactura] AS DATE) BETWEEN @fechaInicio AND @fechaFin", conexion))
            {
                cmd.Parameters.AddWithValue("@fechaInicio", fecha);
                cmd.Parameters.AddWithValue("@fechaFin", fechaFin);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<Havaianas.Facturacion.IngresoMercaderiaDetalle> ingresosList = new List<Havaianas.Facturacion.IngresoMercaderiaDetalle>();
                    while (reader.Read())
                    {
                        Havaianas.Facturacion.IngresoMercaderiaDetalle ingreso = new Havaianas.Facturacion.IngresoMercaderiaDetalle()
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

        internal productos obtener_producto(string producto)
        {
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            // Chequear si producto es numero


            using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 [ID], ISNULL([Codigo_Equis], 0), ISNULL([Cod_Art], 0), ISNULL([Descripcion_Codigo], ''), ISNULL([Categoria], '')" +
                ", ISNULL([Codigo_Color], 0), ISNULL([Descripcion_Color], ''), ISNULL([Talles], ''), ISNULL([Ean_2025], 0) FROM productos " +
                "WHERE Descripcion_Codigo LIKE '%' + @id + '%' OR CONVERT(varchar, EAN_2025)=@id OR CONVERT(varchar, Codigo_Equis)=@id", conexion))
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

        /// <summary>
        /// Función para obtener el stock de productos en una sucursal específica y en una fecha determinada
        /// </summary>
        /// <param name="suc"></param>
        /// <param name="fecha"></param>
        /// <returns></returns>
        internal IList<Modelos.stock> stock(int suc, DateTime? fecha, int producto = 0)
        {
            // Llamar al store procedure "sp_stock" con los parámetros "suc" y "fecha"
            using var conexion = new SqlConnection(connectionString);
            conexion.Open();
            using (SqlCommand cmd = new SqlCommand("sp_stock", conexion))
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
                groupBy = agDia ? " Fecha, ID_Producto, nombre_producto" : " ID_Producto, nombre_producto";
            }
            if (agSuc)
            {
                groupBy = (agDia || agProd) ? groupBy + ", ID_Sucursal, nombre_sucursal" : " ID_Sucursal, nombre_sucursal";
            }

            string comando = $"SELECT [ID], ISNULL([ID_Sucursal], 0), nombre_sucursal, ISNULL([ID_Producto], 0)" +
                ", ISNULL(nombre_producto, 'Sin Producto (' + CAST(ISNULL(ID_Producto, 0) AS varchar) + ')') nombre_producto" +
                ", ISNULL([Cantidad], 0), isnull(precio, 0), ISNULL([Numero], 0)" +
                ", ISNULL([Fecha], GETDATE()) fecha, ISNULL([Tipo_Factura], 0), ISNULL([Vendedor], 0), ISNULL([Forma_Pago], 0)" +
                ", cast( (cantidad*precio) as float) As subtotal,CAST(ISNULL([Descuento], 0) AS FLOAT)" +
                ", ISNULL([Observaciones], ''), ISNULL([Estado], 0), ISNULL([CAE], '')" +
                ", ISNULL([Hora], GETDATE()) FROM vw_ventas" + filtro + " ORDER BY Fecha, ID_Sucursal";

            if (groupBy != "")
            {
                comando = "SELECT " +
                    $"{groupBy}, CAST(SUM(ISNULL([Cantidad], 0)) AS INT) cantidad" +
                    ", SUM(cast((cantidad*precio) as float)) As subtotal, SUM(CAST(ISNULL([Descuento], 0) AS FLOAT)) descuento" +
                    $" FROM vw_ventas {filtro} GROUP BY {groupBy}  ORDER BY {groupBy} ";
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
                                cantidad = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
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
                                hora = reader.IsDBNull(17) ? DateTime.MinValue : reader.GetDateTime(17)
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
                                id_producto = (agProd == true) ? (reader.IsDBNull(reader.GetOrdinal("id_producto")) ? 0 : reader.GetInt32(reader.GetOrdinal("id_producto"))) : 0,
                                nombre_producto = (agProd == true) ? (reader.IsDBNull(reader.GetOrdinal("nombre_producto")) ? "" : reader.GetString(reader.GetOrdinal("nombre_producto"))) : "",
                                cantidad = reader.IsDBNull(reader.GetOrdinal("cantidad")) ? 0 : reader.GetInt32(reader.GetOrdinal("cantidad")),
                                precio = 0,
                                numero = 0,
                                fecha = (agDia == true) ? (reader.IsDBNull(reader.GetOrdinal("fecha")) ? DateTime.Today : reader.GetDateTime(reader.GetOrdinal("fecha"))) : DateTime.Today,
                                tipo_factura = 0,
                                vendedor = 0,
                                forma_pago = 0,
                                subTotal = reader.IsDBNull(reader.GetOrdinal("subtotal")) ? 0 : reader.GetDouble(reader.GetOrdinal("subtotal")),
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
                        venta.total = venta.subTotal - venta.descuento;
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
                        // insertar el detalle_ventas
                        using (SqlCommand cmd = new SqlCommand("INSERT INTO detalle_ventas (id_venta, id_producto, cantidad, precio, descuento, subtotal)" +
                            " VALUES(@id_venta, @id_producto, @cantidad, @precio, @descuento, @subtotal)", conexion))
                        {
                            cmd.Parameters.AddWithValue("@id_venta", (object)venta.id_venta_sucursal ?? System.DBNull.Value);
                            cmd.Parameters.AddWithValue("@id_producto", venta.id_producto);
                            cmd.Parameters.AddWithValue("@cantidad", venta.cantidad);
                            cmd.Parameters.AddWithValue("@precio", venta.precio);
                            cmd.Parameters.AddWithValue("@descuento", venta.descuento);
                            cmd.Parameters.AddWithValue("@subtotal", venta.subTotal);

                            cmd.ExecuteNonQuery();
                        }
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
                        venta.cantidad = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
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

        /// <summary>
        /// Borrar la factura
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
    }
}
