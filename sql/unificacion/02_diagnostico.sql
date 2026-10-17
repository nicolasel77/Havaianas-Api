/*
    02_diagnostico.sql
    ------------------
    SOLO LECTURA. Muestra el estado de los productos y formas de pago antes de unificar.
    Ejecutar con "Resultados en cuadrícula" y revisar cada resultado (están numerados).
*/
USE [dbHavaianas_servidor]
GO
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#prod') IS NOT NULL DROP TABLE #prod;
SELECT p.ID, p.Activo,
       ISNULL(p.Ean_2025, 0) AS Ean,
       ISNULL(p.Cod_Art, 0) AS Cod_Art,
       ISNULL(p.Codigo_Equis, 0) AS Codigo_Equis,
       ISNULL(p.Codigo_Color, 0) AS Codigo_Color,
       UPPER(REPLACE(LTRIM(RTRIM(ISNULL(p.Talles, ''))), ' ', '')) AS Talle,
       UPPER(LTRIM(RTRIM(ISNULL(p.Descripcion_Codigo, '')))) AS Descripcion,
       UPPER(LTRIM(RTRIM(ISNULL(p.Descripcion_Color, '')))) AS Color
INTO #prod
FROM dbo.Productos p
WHERE p.Activo = 1;

-- 1) Resumen
SELECT '1) Resumen' AS resultado,
       (SELECT COUNT(*) FROM dbo.Productos) AS productos_total,
       (SELECT COUNT(*) FROM #prod) AS productos_activos,
       (SELECT COUNT(*) FROM #prod WHERE Ean = 0) AS activos_sin_ean,
       (SELECT COUNT(*) FROM #prod WHERE Cod_Art = 0) AS activos_sin_codart,
       (SELECT COUNT(*) FROM (SELECT Ean FROM #prod WHERE Ean > 0 GROUP BY Ean HAVING COUNT(*) > 1) x) AS eans_repetidos,
       (SELECT COUNT(*) FROM (SELECT 1 a FROM #prod WHERE Cod_Art > 0 GROUP BY Cod_Art, Codigo_Color, Talle HAVING COUNT(*) > 1) x) AS codart_color_talle_repetidos;

-- 2) Duplicados por EAN (mismo EAN, color y talle) -> se unifican automáticamente
SELECT '2) Mismo EAN + color + talle' AS resultado, p.*
FROM #prod p
WHERE EXISTS (SELECT 1 FROM #prod o WHERE o.ID <> p.ID AND o.Ean = p.Ean AND p.Ean > 0
              AND o.Codigo_Color = p.Codigo_Color AND o.Talle = p.Talle)
ORDER BY p.Ean, p.ID;

-- 3) Duplicados por Cod_Art + color + talle -> se unifican si no tienen EANs distintos
SELECT '3) Mismo Cod_Art + color + talle' AS resultado, p.*,
       CASE WHEN g.eans > 1 THEN 'NO (EANs distintos, revisar)' ELSE 'SI' END AS se_unifica
FROM #prod p
INNER JOIN (SELECT Cod_Art, Codigo_Color, Talle, COUNT(*) AS cant, COUNT(DISTINCT NULLIF(Ean, 0)) AS eans
            FROM #prod WHERE Cod_Art > 0
            GROUP BY Cod_Art, Codigo_Color, Talle HAVING COUNT(*) > 1) g
    ON g.Cod_Art = p.Cod_Art AND g.Codigo_Color = p.Codigo_Color AND g.Talle = p.Talle
ORDER BY p.Cod_Art, p.Codigo_Color, p.Talle, p.ID;

-- 4) REVISAR A MANO: mismo EAN pero distinto color o talle (EAN mal cargado en alguno)
SELECT '4) REVISAR: mismo EAN, distinto color/talle' AS resultado, p.*
FROM #prod p
WHERE p.Ean > 0
  AND EXISTS (SELECT 1 FROM #prod o WHERE o.Ean = p.Ean AND (o.Codigo_Color <> p.Codigo_Color OR o.Talle <> p.Talle))
ORDER BY p.Ean, p.ID;

-- 5) REVISAR A MANO: misma descripción + color + talle pero distinto Cod_Art o EAN
SELECT '5) REVISAR: misma descripción/color/talle, distinto código' AS resultado, p.*
FROM #prod p
WHERE p.Descripcion <> ''
  AND EXISTS (SELECT 1 FROM #prod o WHERE o.ID <> p.ID AND o.Descripcion = p.Descripcion AND o.Color = p.Color AND o.Talle = p.Talle
              AND (o.Cod_Art <> p.Cod_Art OR (o.Ean <> p.Ean AND o.Ean > 0 AND p.Ean > 0)))
ORDER BY p.Descripcion, p.Color, p.Talle, p.ID;

-- 6) Movimientos por producto duplicado (para ver cuánto pesa cada uno)
SELECT '6) Ventas por ID de producto y sucursal (solo IDs duplicados)' AS resultado,
       dv.id_sucursal, dv.id_producto, COUNT(*) AS lineas, SUM(dv.cantidad) AS unidades
FROM dbo.detalle_ventas dv
WHERE dv.id_producto IN (
    SELECT p.ID FROM #prod p
    WHERE EXISTS (SELECT 1 FROM #prod o WHERE o.ID <> p.ID
                  AND ((o.Ean = p.Ean AND p.Ean > 0) OR (o.Cod_Art = p.Cod_Art AND p.Cod_Art > 0 AND o.Codigo_Color = p.Codigo_Color AND o.Talle = p.Talle))))
GROUP BY dv.id_sucursal, dv.id_producto
ORDER BY dv.id_producto, dv.id_sucursal;

-- 7) Líneas de venta cuyo producto NO existe en el servidor (ID de la sucursal sin equivalencia)
SELECT '7) Ventas con producto inexistente en el servidor' AS resultado,
       dv.id_sucursal, dv.id_producto, COUNT(*) AS lineas, SUM(dv.cantidad) AS unidades
FROM dbo.detalle_ventas dv
LEFT JOIN dbo.Productos p ON p.ID = dv.id_producto
WHERE p.ID IS NULL
GROUP BY dv.id_sucursal, dv.id_producto
ORDER BY dv.id_sucursal, dv.id_producto;

-- 8) Líneas de venta repetidas (la API anterior volvía a insertar el detalle cada vez que una
--    sucursal reenviaba una venta). Ver 04_unificar.sql, paso opcional de limpieza.
SELECT '8) Líneas de venta repetidas' AS resultado, dv.id_sucursal,
       COUNT(*) AS grupos_repetidos, SUM(dv.veces - 1) AS lineas_de_mas, SUM((dv.veces - 1) * dv.cantidad) AS unidades_de_mas
FROM (SELECT id_sucursal, id_venta, id_producto, cantidad, precio, COUNT(*) AS veces
      FROM dbo.detalle_ventas
      GROUP BY id_sucursal, id_venta, id_producto, cantidad, precio, descuento, subtotal
      HAVING COUNT(*) > 1) dv
GROUP BY dv.id_sucursal
ORDER BY dv.id_sucursal;

-- 9) Formas de pago usadas en ventas, por sucursal (para armar FormasPagoSucursal)
SELECT '9) Formas de pago en ventas' AS resultado, v.id_sucursal, v.forma_pago,
       fp.Descripcion AS descripcion_servidor, COUNT(*) AS ventas
FROM dbo.ventas v
LEFT JOIN dbo.formas_pago fp ON fp.ID = v.forma_pago
GROUP BY v.id_sucursal, v.forma_pago, fp.Descripcion
ORDER BY v.id_sucursal, v.forma_pago;

-- 10) Formas de pago del servidor con descripción repetida
SELECT '10) Formas de pago repetidas' AS resultado, fp.*
FROM dbo.formas_pago fp
WHERE EXISTS (SELECT 1 FROM dbo.formas_pago o WHERE o.ID <> fp.ID
              AND UPPER(LTRIM(RTRIM(o.Descripcion))) = UPPER(LTRIM(RTRIM(fp.Descripcion))))
ORDER BY fp.Descripcion, fp.ID;

-- 11) Columnas de la base que parecen referenciar productos.
--     Si aparece alguna con cubierta = 'NO', agregarla en sp_aplicar_unificacion (01_estructura.sql).
SELECT '11) Columnas que referencian productos' AS resultado, t.name AS tabla, c.name AS columna,
       CASE WHEN t.name + '.' + c.name IN ('detalle_ventas.id_producto', 'compras.ID_Producto', 'IngresoMercaderiaDetalle.ProductoId',
                                           'MapeoProductoProveedor.ProductoId', 'traslados.ID_Producto', 'ajustes.producto', 'ajustes.id_producto',
                                           'ajustes_stock.producto', 'precios.idProducto', 'precios.id_producto', 'ProductosSucursal.id_producto')
            THEN 'SI' ELSE 'NO' END AS cubierta
FROM sys.columns c
INNER JOIN sys.tables t ON t.object_id = c.object_id
WHERE (c.name LIKE '%producto%' OR c.name LIKE '%prod%')
  AND t.name NOT IN ('Productos', 'ProductosSucursalImport', 'ProductosUnificacion')
  AND c.name NOT IN ('id_producto_sucursal')
ORDER BY cubierta, t.name, c.name;
GO
