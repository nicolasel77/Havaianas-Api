/*
    06_sucursales.sql
    -----------------
    En el SERVIDOR, después de 05_aplicar_maestro.sql. Repetir para cada sucursal (cambiar @suc).
    Vincula cada producto de la sucursal (cargado en 03_catalogos_sucursales.sql) con el producto
    del servidor de su fila de la lista maestra, y traduce las ventas de la sucursal que ya están en
    el servidor. Varios productos repetidos de la sucursal quedan vinculados al mismo producto.

    Cada paso declara su @suc: cambiarlo en todos (así cada bloque se puede ejecutar solo).
    Paso A: vincular y revisar lo que quedó sin vincular / para revisar.
    Paso B: formas de pago.
    Paso C: traducir las ventas ya cargadas.
    Después: convertir la base de la sucursal (08_convertir_sucursal.sql).
*/
USE [dbHavaianas_servidor]
GO
SET NOCOUNT ON;

DECLARE @suc int = 1;   -- <<< NÚMERO DE SUCURSAL

------------------------------------------------------------------------------------------
-- Paso A. Vincular. Devuelve los productos de la sucursal que NO se pudieron vincular.
------------------------------------------------------------------------------------------
EXEC dbo.sp_equivalencias_sucursal @suc = @suc;

-- Cuántas ventas tiene cada producto sin vincular (los que tienen ventas hay que vincularlos a mano)
SELECT 'Sin vincular, con ventas' AS resultado, i.ID, i.Descripcion_Codigo, i.Descripcion_Color, i.Talles, i.Ean_2025,
       COUNT(dv.id_producto) AS lineas_venta
FROM dbo.ProductosSucursalImport i
LEFT JOIN dbo.detalle_ventas dv ON dv.id_sucursal = i.id_sucursal AND dv.id_producto = i.ID AND dv.id_producto_sucursal IS NULL
WHERE i.id_sucursal = @suc
  AND NOT EXISTS (SELECT 1 FROM dbo.ProductosSucursal ps WHERE ps.id_sucursal = i.id_sucursal AND ps.id_producto_sucursal = i.ID)
GROUP BY i.ID, i.Descripcion_Codigo, i.Descripcion_Color, i.Talles, i.Ean_2025
HAVING COUNT(dv.id_producto) > 0
ORDER BY lineas_venta DESC;

-- Vinculados que conviene mirar (EAN igual pero talle/color distinto, o vinculados solo por descripción)
SELECT 'Revisar vínculo' AS resultado, ps.criterio,
       i.ID AS id_sucursal_prod, i.Descripcion_Codigo AS desc_sucursal, i.Descripcion_Color AS color_sucursal, i.Talles AS talle_sucursal, i.Ean_2025 AS ean_sucursal,
       p.ID AS id_servidor, p.Descripcion_Codigo AS desc_servidor, p.Descripcion_Color AS color_servidor, p.Talles AS talle_servidor, p.Ean_2025 AS ean_servidor
FROM dbo.ProductosSucursal ps
INNER JOIN dbo.ProductosSucursalImport i ON i.id_sucursal = ps.id_sucursal AND i.ID = ps.id_producto_sucursal
INNER JOIN dbo.Productos p ON p.ID = ps.id_producto
WHERE ps.id_sucursal = @suc AND ps.revisar = 1
ORDER BY i.Descripcion_Codigo;
GO

-- Para corregir o completar un vínculo a mano:
--   MERGE con criterio 'MANUAL' (no lo pisa una nueva ejecución de sp_equivalencias_sucursal)
/*
DECLARE @suc int = 1;   -- <<< NÚMERO DE SUCURSAL
MERGE dbo.ProductosSucursal AS t
USING (VALUES (@suc, 123 /* ID en la sucursal */, 456 /* ID en el servidor */)) AS m(id_sucursal, id_producto_sucursal, id_producto)
    ON t.id_sucursal = m.id_sucursal AND t.id_producto_sucursal = m.id_producto_sucursal
WHEN MATCHED THEN UPDATE SET id_producto = m.id_producto, criterio = 'MANUAL', revisar = 0, fecha = GETDATE()
WHEN NOT MATCHED THEN INSERT (id_sucursal, id_producto_sucursal, id_producto, criterio) VALUES (m.id_sucursal, m.id_producto_sucursal, m.id_producto, 'MANUAL');
*/
GO

------------------------------------------------------------------------------------------
-- Paso B. Formas de pago: ID en la sucursal -> ID en el servidor.
-- Ver el resultado 9 de 02_diagnostico.sql y la tabla de formas de pago de la sucursal.
------------------------------------------------------------------------------------------
/*
DECLARE @suc int = 1;   -- <<< NÚMERO DE SUCURSAL
MERGE dbo.FormasPagoSucursal AS t
USING (VALUES
    (@suc, 1 /* ID en la sucursal */, 'EFECTIVO', 1 /* ID en el servidor */),
    (@suc, 2, 'DEBITO', 2)
) AS m(id_sucursal, id_forma_pago_sucursal, descripcion_sucursal, id_forma_pago)
    ON t.id_sucursal = m.id_sucursal AND t.id_forma_pago_sucursal = m.id_forma_pago_sucursal
WHEN MATCHED THEN UPDATE SET id_forma_pago = m.id_forma_pago, descripcion_sucursal = m.descripcion_sucursal, fecha = GETDATE()
WHEN NOT MATCHED THEN INSERT (id_sucursal, id_forma_pago_sucursal, descripcion_sucursal, id_forma_pago)
    VALUES (m.id_sucursal, m.id_forma_pago_sucursal, m.descripcion_sucursal, m.id_forma_pago);
*/
GO

------------------------------------------------------------------------------------------
-- Paso C. Traducir las ventas ya cargadas de la sucursal. Se puede repetir sin problema:
-- solo procesa lo que todavía no fue traducido.
------------------------------------------------------------------------------------------
/*
DECLARE @suc int = 1;   -- <<< NÚMERO DE SUCURSAL
EXEC dbo.sp_traducir_ventas_sucursal @suc = @suc;
*/
GO
