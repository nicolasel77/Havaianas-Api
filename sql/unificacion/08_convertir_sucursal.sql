/*
    08_convertir_sucursal.sql
    -------------------------
    Se ejecuta en la BASE DE LA SUCURSAL (no en el servidor), después de 06_sucursales.sql.
    Deja la sucursal con la misma lista de productos (y formas de pago) que el servidor, con los mismos IDs:
      * pasa ventas, traslados, ingresos, ajustes, etc. de la sucursal al ID del servidor
      * reemplaza la tabla Productos de la sucursal por la del servidor (la anterior queda en Productos_antes_unificar)
      * lo mismo con las formas de pago, si se cargó su equivalencia

    Hacerlo con la sucursal SIN VENDER y con todas sus ventas ya enviadas al servidor. Al terminar, en el
    servidor: EXEC dbo.sp_sucursal_convertida @suc = N (desde ahí la sucursal manda IDs del servidor).

    Ejecutar por partes, en orden.
*/
USE [dbHavaianas_sucursal1]   -- <<< BASE DE LA SUCURSAL
GO
SET NOCOUNT ON;

------------------------------------------------------------------------------------------
-- Paso 1. Traer del servidor: equivalencias de esta sucursal y productos / formas de pago del servidor.
-- Si el servidor está en otra máquina usar [SERVIDOR].[dbHavaianas_servidor] (linked server) o
-- copiar estas tablas con Tareas -> Importar datos.
------------------------------------------------------------------------------------------
DECLARE @suc int = 1;   -- <<< NÚMERO DE SUCURSAL

IF OBJECT_ID('dbo.EquivalenciaServidor', 'U') IS NOT NULL DROP TABLE dbo.EquivalenciaServidor;
SELECT id_producto_sucursal, id_producto
INTO dbo.EquivalenciaServidor
FROM [dbHavaianas_servidor].dbo.ProductosSucursal
WHERE id_sucursal = @suc;

IF OBJECT_ID('dbo.ProductosServidor', 'U') IS NOT NULL DROP TABLE dbo.ProductosServidor;
SELECT *
INTO dbo.ProductosServidor
FROM [dbHavaianas_servidor].dbo.Productos
WHERE Activo = 1;

IF OBJECT_ID('dbo.FormasPagoEquivalencia', 'U') IS NOT NULL DROP TABLE dbo.FormasPagoEquivalencia;
SELECT id_forma_pago_sucursal, id_forma_pago
INTO dbo.FormasPagoEquivalencia
FROM [dbHavaianas_servidor].dbo.FormasPagoSucursal
WHERE id_sucursal = @suc;

IF OBJECT_ID('dbo.FormasPagoServidor', 'U') IS NOT NULL DROP TABLE dbo.FormasPagoServidor;
SELECT *
INTO dbo.FormasPagoServidor
FROM [dbHavaianas_servidor].dbo.formas_pago;

SELECT (SELECT COUNT(*) FROM dbo.EquivalenciaServidor) AS equivalencias,
       (SELECT COUNT(*) FROM dbo.ProductosServidor) AS productos_servidor,
       (SELECT COUNT(*) FROM dbo.FormasPagoEquivalencia) AS equivalencias_formas_pago;
GO

------------------------------------------------------------------------------------------
-- Paso 2. Convertir. Verifica antes que todos los productos usados en movimientos de la
-- sucursal tengan equivalencia; si falta alguno lo lista y no cambia nada.
------------------------------------------------------------------------------------------
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @refs TABLE (tabla sysname, columna sysname);
INSERT INTO @refs VALUES
    (N'detalle_ventas',           N'id_producto'),
    (N'compras',                  N'ID_Producto'),
    (N'IngresoMercaderiaDetalle', N'ProductoId'),
    (N'MapeoProductoProveedor',   N'ProductoId'),
    (N'traslados',                N'ID_Producto'),
    (N'ajustes',                  N'producto'),
    (N'ajustes',                  N'id_producto'),
    (N'ajustes_stock',            N'producto'),
    (N'precios',                  N'idProducto'),
    (N'precios',                  N'id_producto');

DELETE FROM @refs WHERE COL_LENGTH(N'dbo.' + tabla, columna) IS NULL;

-- Columnas que parecen referenciar productos y no están en la lista (revisar; si hace falta agregarlas arriba)
SELECT 'Columnas de producto NO incluidas (revisar)' AS resultado, t.name AS tabla, c.name AS columna
FROM sys.columns c
INNER JOIN sys.tables t ON t.object_id = c.object_id
WHERE (c.name LIKE '%producto%' OR c.name LIKE '%prod%')
  AND t.name NOT IN ('Productos', 'ProductosServidor', 'EquivalenciaServidor', 'Productos_antes_unificar')
  AND NOT EXISTS (SELECT 1 FROM @refs r WHERE r.tabla = t.name AND r.columna = c.name);

-- Productos usados sin equivalencia
IF OBJECT_ID('tempdb..#sin_equivalencia') IS NOT NULL DROP TABLE #sin_equivalencia;
CREATE TABLE #sin_equivalencia (tabla sysname, id_producto int, filas int);

DECLARE @tabla sysname, @columna sysname, @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT tabla, columna FROM @refs;
OPEN c;
FETCH NEXT FROM c INTO @tabla, @columna;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'INSERT INTO #sin_equivalencia SELECT ''' + @tabla + N''', t.' + QUOTENAME(@columna) + N', COUNT(*) FROM dbo.' + QUOTENAME(@tabla) + N' t ' +
               N'WHERE t.' + QUOTENAME(@columna) + N' IS NOT NULL AND t.' + QUOTENAME(@columna) + N' <> 0 ' +
               N'AND NOT EXISTS (SELECT 1 FROM dbo.EquivalenciaServidor e WHERE e.id_producto_sucursal = t.' + QUOTENAME(@columna) + N') ' +
               N'GROUP BY t.' + QUOTENAME(@columna) + N';';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM c INTO @tabla, @columna;
END
CLOSE c;
DEALLOCATE c;

IF EXISTS (SELECT 1 FROM #sin_equivalencia)
BEGIN
    SELECT 'Productos usados sin equivalencia: vincularlos en el servidor (06_sucursales.sql) y repetir el paso 1' AS resultado,
           s.tabla, s.id_producto, s.filas, p.*
    FROM #sin_equivalencia s
    LEFT JOIN dbo.Productos p ON p.ID = s.id_producto
    ORDER BY s.filas DESC;
    RAISERROR(N'Hay productos usados sin equivalencia. No se cambió nada.', 16, 1);
    RETURN;
END

BEGIN TRAN;

-- Desactivar temporalmente las claves foráneas que apuntan a Productos / formas_pago (se vuelven a validar al final)
DECLARE @fks TABLE (tabla nvarchar(300), fk sysname);
INSERT INTO @fks
SELECT QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)), fk.name
FROM sys.foreign_keys fk
WHERE fk.referenced_object_id IN (OBJECT_ID('dbo.Productos'), OBJECT_ID('dbo.formas_pago'));

DECLARE @fk_tabla nvarchar(300), @fk sysname;
DECLARE f CURSOR LOCAL FAST_FORWARD FOR SELECT tabla, fk FROM @fks;
OPEN f;
FETCH NEXT FROM f INTO @fk_tabla, @fk;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'ALTER TABLE ' + @fk_tabla + N' NOCHECK CONSTRAINT ' + QUOTENAME(@fk) + N';';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM f INTO @fk_tabla, @fk;
END
CLOSE f;

-- 1) Movimientos: ID de la sucursal -> ID del servidor (un solo UPDATE por columna, así no se mezclan IDs)
DECLARE c2 CURSOR LOCAL FAST_FORWARD FOR SELECT tabla, columna FROM @refs;
OPEN c2;
FETCH NEXT FROM c2 INTO @tabla, @columna;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'UPDATE t SET ' + QUOTENAME(@columna) + N' = e.id_producto FROM dbo.' + QUOTENAME(@tabla) + N' t ' +
               N'INNER JOIN dbo.EquivalenciaServidor e ON e.id_producto_sucursal = t.' + QUOTENAME(@columna) + N';' +
               N'PRINT ''' + @tabla + N'.' + @columna + N': '' + CAST(@@ROWCOUNT AS varchar(20)) + '' filas'';';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM c2 INTO @tabla, @columna;
END
CLOSE c2;
DEALLOCATE c2;

-- 2) Productos: reemplazar por los del servidor (columnas en común; respeta IDENTITY)
IF OBJECT_ID('dbo.Productos_antes_unificar', 'U') IS NULL
    SELECT * INTO dbo.Productos_antes_unificar FROM dbo.Productos;

DECLARE @columnas nvarchar(max);
SELECT @columnas = STUFF((
    SELECT N', ' + QUOTENAME(c.name)
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('dbo.Productos') AND c.is_computed = 0
      AND COL_LENGTH('dbo.ProductosServidor', c.name) IS NOT NULL
    ORDER BY c.column_id
    FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'');

SET @sql = N'DELETE FROM dbo.Productos;' +
           CASE WHEN OBJECTPROPERTY(OBJECT_ID('dbo.Productos'), 'TableHasIdentity') = 1 THEN N'SET IDENTITY_INSERT dbo.Productos ON;' ELSE N'' END +
           N'INSERT INTO dbo.Productos (' + @columnas + N') SELECT ' + @columnas + N' FROM dbo.ProductosServidor;' +
           CASE WHEN OBJECTPROPERTY(OBJECT_ID('dbo.Productos'), 'TableHasIdentity') = 1 THEN N'SET IDENTITY_INSERT dbo.Productos OFF;' ELSE N'' END +
           N'PRINT ''Productos: '' + CAST(@@ROWCOUNT AS varchar(20));';
EXEC sp_executesql @sql;

-- 3) Formas de pago (solo si se cargó la equivalencia)
IF EXISTS (SELECT 1 FROM dbo.FormasPagoEquivalencia) AND OBJECT_ID('dbo.formas_pago', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ventas', 'forma_pago') IS NOT NULL
    BEGIN
        UPDATE v SET forma_pago = e.id_forma_pago
        FROM dbo.ventas v INNER JOIN dbo.FormasPagoEquivalencia e ON e.id_forma_pago_sucursal = v.forma_pago;
        PRINT 'ventas.forma_pago: ' + CAST(@@ROWCOUNT AS varchar(20));
    END

    IF OBJECT_ID('dbo.formas_pago_antes_unificar', 'U') IS NULL
        SELECT * INTO dbo.formas_pago_antes_unificar FROM dbo.formas_pago;

    SET @columnas = NULL;
    SELECT @columnas = STUFF((
        SELECT N', ' + QUOTENAME(c.name)
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID('dbo.formas_pago') AND c.is_computed = 0
          AND COL_LENGTH('dbo.FormasPagoServidor', c.name) IS NOT NULL
        ORDER BY c.column_id
        FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'');

    SET @sql = N'DELETE FROM dbo.formas_pago;' +
               CASE WHEN OBJECTPROPERTY(OBJECT_ID('dbo.formas_pago'), 'TableHasIdentity') = 1 THEN N'SET IDENTITY_INSERT dbo.formas_pago ON;' ELSE N'' END +
               N'INSERT INTO dbo.formas_pago (' + @columnas + N') SELECT ' + @columnas + N' FROM dbo.FormasPagoServidor;' +
               CASE WHEN OBJECTPROPERTY(OBJECT_ID('dbo.formas_pago'), 'TableHasIdentity') = 1 THEN N'SET IDENTITY_INSERT dbo.formas_pago OFF;' ELSE N'' END;
    EXEC sp_executesql @sql;
END

-- Reactivar y validar las claves foráneas (si algún dato no cierra, falla y se deshace todo)
OPEN f;
FETCH NEXT FROM f INTO @fk_tabla, @fk;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'ALTER TABLE ' + @fk_tabla + N' WITH CHECK CHECK CONSTRAINT ' + QUOTENAME(@fk) + N';';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM f INTO @fk_tabla, @fk;
END
CLOSE f;
DEALLOCATE f;

COMMIT;
PRINT 'Sucursal convertida. Ahora en el servidor: EXEC dbo.sp_sucursal_convertida @suc = <número>;';
GO
