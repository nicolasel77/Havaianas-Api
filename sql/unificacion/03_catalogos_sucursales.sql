/*
    03_catalogos_sucursales.sql
    ---------------------------
    Carga en el servidor el listado de productos de CADA sucursal (tal como está hoy en su base,
    con sus repetidos). Se usa para:
      * saber que las ventas de esa sucursal vienen con IDs propios (y no tocarlas al unificar el servidor)
      * después (06_sucursales.sql), vincular cada producto de la sucursal con el de la lista maestra.

    Hacerlo para TODAS las sucursales ANTES de 05_aplicar_maestro.sql.

    Opción 1 (la base de la sucursal está en este mismo SQL Server, o hay linked server):
        usar el INSERT de abajo, una vez por sucursal.
    Opción 2: en SSMS, botón derecho sobre la base -> Tareas -> Importar datos,
        origen = tabla Productos de la sucursal, destino = dbo.ProductosSucursalImport
        (agregar la columna id_sucursal con el número de sucursal).
    Opción 3: la API: POST /Productos/Equivalencias/{suc} con el listado de productos de la sucursal
        (mismo formato que GET /Productos). Solo DESPUÉS de 05_aplicar_maestro.sql, porque además vincula.
*/
USE [dbHavaianas_servidor]
GO
SET NOCOUNT ON;

DECLARE @suc int = 1;   -- <<< NÚMERO DE SUCURSAL

-- Cambiar [dbHavaianas_sucursal1] por la base de la sucursal ([SERVIDOR].[base] si es linked server)
DELETE FROM dbo.ProductosSucursalImport WHERE id_sucursal = @suc;

INSERT INTO dbo.ProductosSucursalImport
    (id_sucursal, ID, Codigo_Equis, Cod_Art, Descripcion_Codigo, Codigo_Color, Descripcion_Color, Talles, Ean_2025)
SELECT @suc, ID, Codigo_Equis, Cod_Art, Descripcion_Codigo, Codigo_Color, Descripcion_Color, Talles, Ean_2025
FROM [dbHavaianas_sucursal1].dbo.Productos;

SELECT id_sucursal, COUNT(*) AS productos FROM dbo.ProductosSucursalImport GROUP BY id_sucursal;

-- Productos de la sucursal que aparecen en ventas del servidor y no están en su catálogo
-- (si hay, el catálogo está incompleto: esas ventas no se van a poder traducir)
SELECT 'Ventas con productos que no están en el catálogo de la sucursal' AS resultado, dv.id_producto, COUNT(*) AS lineas
FROM dbo.detalle_ventas dv
WHERE dv.id_sucursal = @suc AND dv.id_producto_sucursal IS NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.ProductosSucursalImport i WHERE i.id_sucursal = dv.id_sucursal AND i.ID = dv.id_producto)
GROUP BY dv.id_producto
ORDER BY lineas DESC;
GO
