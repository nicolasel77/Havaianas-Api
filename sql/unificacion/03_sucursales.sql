/*
    03_sucursales.sql
    -----------------
    Vincula los productos y formas de pago de UNA sucursal con los del servidor y
    traduce sus ventas ya cargadas. Repetir para cada sucursal (cambiar @suc).

    IMPORTANTE: hacerlo para TODAS las sucursales ANTES de 04_unificar.sql.
    Las ventas que llegan de una sucursal traen el ID de producto de la base de la sucursal;
    hay que pasarlas a IDs del servidor antes de unificar los duplicados del servidor.

    Paso A: cargar el catálogo de productos de la sucursal en dbo.ProductosSucursalImport.
            Opción 1 (la base de la sucursal está en este mismo SQL Server o hay linked server):
                usar el INSERT de abajo cambiando el nombre de la base.
            Opción 2: en SSMS, botón derecho sobre la base -> Tareas -> Importar datos,
                origen = tabla Productos de la sucursal, destino = dbo.ProductosSucursalImport
                (agregar la columna id_sucursal con el número de sucursal).
            Opción 3: la API: POST /Productos/Equivalencias/{suc} con el listado de productos
                de la sucursal (mismo formato que GET /Productos). Hace los pasos A y B juntos.
    Paso B: vincular (sp_equivalencias_sucursal) y revisar lo que quedó sin vincular / para revisar.
    Paso C: formas de pago.
    Paso D: traducir las ventas ya cargadas (sp_traducir_ventas_sucursal).
*/
USE [dbHavaianas_servidor]
GO
SET NOCOUNT ON;

DECLARE @suc int = 1;   -- <<< NÚMERO DE SUCURSAL

------------------------------------------------------------------------------------------
-- Paso A (opción 1). Cambiar [dbHavaianas_sucursal1] por la base de la sucursal
-- (o [SERVIDOR_SUCURSAL].[base] si es un linked server).
------------------------------------------------------------------------------------------
/*
DELETE FROM dbo.ProductosSucursalImport WHERE id_sucursal = @suc;

INSERT INTO dbo.ProductosSucursalImport
    (id_sucursal, ID, Codigo_Equis, Cod_Art, Descripcion_Codigo, Codigo_Color, Descripcion_Color, Talles, Ean_2025)
SELECT @suc, ID, Codigo_Equis, Cod_Art, Descripcion_Codigo, Codigo_Color, Descripcion_Color, Talles, Ean_2025
FROM [dbHavaianas_sucursal1].dbo.Productos;
*/

------------------------------------------------------------------------------------------
-- Paso B. Vincular. Devuelve los productos de la sucursal que NO se pudieron vincular.
------------------------------------------------------------------------------------------
EXEC dbo.sp_equivalencias_sucursal @suc = @suc;

-- Vinculados que conviene mirar (EAN igual pero talle/color distinto, o vinculados solo por descripción)
SELECT 'Revisar vínculo' AS resultado, ps.criterio,
       i.ID AS id_sucursal_prod, i.Descripcion_Codigo AS desc_sucursal, i.Descripcion_Color AS color_sucursal, i.Talles AS talle_sucursal, i.Ean_2025 AS ean_sucursal,
       p.ID AS id_servidor, p.Descripcion_Codigo AS desc_servidor, p.Descripcion_Color AS color_servidor, p.Talles AS talle_servidor, p.Ean_2025 AS ean_servidor
FROM dbo.ProductosSucursal ps
INNER JOIN dbo.ProductosSucursalImport i ON i.id_sucursal = ps.id_sucursal AND i.ID = ps.id_producto_sucursal
INNER JOIN dbo.Productos p ON p.ID = ps.id_producto
WHERE ps.id_sucursal = @suc AND ps.revisar = 1
ORDER BY i.Descripcion_Codigo;

-- Para corregir o completar un vínculo a mano:
--   MERGE con criterio 'MANUAL' (no lo pisa una nueva ejecución de sp_equivalencias_sucursal)
/*
MERGE dbo.ProductosSucursal AS t
USING (VALUES (@suc, 123 /* ID en la sucursal */, 456 /* ID en el servidor */)) AS m(id_sucursal, id_producto_sucursal, id_producto)
    ON t.id_sucursal = m.id_sucursal AND t.id_producto_sucursal = m.id_producto_sucursal
WHEN MATCHED THEN UPDATE SET id_producto = m.id_producto, criterio = 'MANUAL', revisar = 0, fecha = GETDATE()
WHEN NOT MATCHED THEN INSERT (id_sucursal, id_producto_sucursal, id_producto, criterio) VALUES (m.id_sucursal, m.id_producto_sucursal, m.id_producto, 'MANUAL');
*/

-- Si un producto de la sucursal no existe en el servidor: crearlo desde la WebApp
-- (Productos -> Nuevo producto) y volver a ejecutar este paso.

------------------------------------------------------------------------------------------
-- Paso C. Formas de pago: ID en la sucursal -> ID en el servidor.
-- Ver el resultado 9 de 02_diagnostico.sql y la tabla de formas de pago de la sucursal.
------------------------------------------------------------------------------------------
/*
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

------------------------------------------------------------------------------------------
-- Paso D. Traducir las ventas ya cargadas de la sucursal. Se puede repetir sin problema:
-- solo procesa lo que todavía no fue traducido.
------------------------------------------------------------------------------------------
/*
EXEC dbo.sp_traducir_ventas_sucursal @suc = @suc;
*/
GO
