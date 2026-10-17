/*
    01_estructura.sql
    -----------------
    Crea las columnas, tablas y procedimientos que usan la unificación de productos
    y la API para traducir los productos / formas de pago de cada sucursal a los del servidor.

    * NO modifica datos existentes: solo agrega columnas (con valor por defecto) y objetos nuevos.
    * Se puede ejecutar varias veces (es idempotente).
    * Hay que ejecutarlo ANTES de publicar la nueva versión de la API.
*/
USE [dbHavaianas_servidor]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

------------------------------------------------------------------------------------------
-- Verificación de columnas que se asumen existentes
------------------------------------------------------------------------------------------
DECLARE @faltantes nvarchar(max) = N'';
SELECT @faltantes = @faltantes + c.tabla + N'.' + c.columna + N'  '
FROM (VALUES
    (N'dbo.Productos', N'ID'), (N'dbo.Productos', N'Cod_Art'), (N'dbo.Productos', N'Codigo_Color'),
    (N'dbo.Productos', N'Talles'), (N'dbo.Productos', N'Ean_2025'), (N'dbo.Productos', N'Codigo_Equis'),
    (N'dbo.Productos', N'Descripcion_Codigo'), (N'dbo.Productos', N'Descripcion_Color'),
    (N'dbo.detalle_ventas', N'id_venta'), (N'dbo.detalle_ventas', N'id_producto'), (N'dbo.detalle_ventas', N'id_sucursal'),
    (N'dbo.ventas', N'forma_pago'), (N'dbo.ventas', N'id_sucursal'),
    (N'dbo.formas_pago', N'ID'), (N'dbo.formas_pago', N'Descripcion')
) c(tabla, columna)
WHERE COL_LENGTH(c.tabla, c.columna) IS NULL;

IF @faltantes <> N''
BEGIN
    RAISERROR(N'Faltan columnas esperadas: %s. Revisar los nombres antes de seguir.', 16, 1, @faltantes);
    SET NOEXEC ON;  -- no ejecuta el resto del script
END
GO

------------------------------------------------------------------------------------------
-- Columnas nuevas
------------------------------------------------------------------------------------------
-- Productos.Activo: los duplicados unificados quedan inactivos (no se borran)
IF COL_LENGTH('dbo.Productos', 'Activo') IS NULL
    ALTER TABLE dbo.Productos ADD Activo bit NOT NULL CONSTRAINT DF_Productos_Activo DEFAULT (1);
GO
-- Productos.ID_Unificado: a qué producto se unificó un duplicado
IF COL_LENGTH('dbo.Productos', 'ID_Unificado') IS NULL
    ALTER TABLE dbo.Productos ADD ID_Unificado int NULL;
GO
-- detalle_ventas.id_producto_sucursal: ID del producto tal cual lo mandó la sucursal.
-- NULL = todavía no se tradujo al producto del servidor.
IF COL_LENGTH('dbo.detalle_ventas', 'id_producto_sucursal') IS NULL
    ALTER TABLE dbo.detalle_ventas ADD id_producto_sucursal int NULL;
GO
-- ventas.forma_pago_sucursal: forma de pago tal cual la mandó la sucursal.
IF COL_LENGTH('dbo.ventas', 'forma_pago_sucursal') IS NULL
    ALTER TABLE dbo.ventas ADD forma_pago_sucursal int NULL;
GO

------------------------------------------------------------------------------------------
-- Tablas nuevas
------------------------------------------------------------------------------------------
-- Catálogo de productos de cada sucursal (se carga desde la base de la sucursal o desde la API)
IF OBJECT_ID('dbo.ProductosSucursalImport', 'U') IS NULL
CREATE TABLE dbo.ProductosSucursalImport (
    id_sucursal         int           NOT NULL,
    ID                  int           NOT NULL,
    Codigo_Equis        bigint        NULL,
    Cod_Art             int           NULL,
    Descripcion_Codigo  varchar(100)  NULL,
    Codigo_Color        int           NULL,
    Descripcion_Color   varchar(100)  NULL,
    Talles              varchar(50)   NULL,
    Ean_2025            bigint        NULL,
    CONSTRAINT PK_ProductosSucursalImport PRIMARY KEY (id_sucursal, ID)
);
GO

-- Equivalencia: producto de la sucursal -> producto del servidor
IF OBJECT_ID('dbo.ProductosSucursal', 'U') IS NULL
CREATE TABLE dbo.ProductosSucursal (
    id_sucursal          int          NOT NULL,
    id_producto_sucursal int          NOT NULL,
    id_producto          int          NOT NULL,
    criterio             varchar(30)  NOT NULL,   -- EAN / CODART_COLOR_TALLE / DESCRIPCION / MANUAL
    revisar              bit          NOT NULL CONSTRAINT DF_ProductosSucursal_revisar DEFAULT (0),
    fecha                datetime     NOT NULL CONSTRAINT DF_ProductosSucursal_fecha DEFAULT (GETDATE()),
    CONSTRAINT PK_ProductosSucursal PRIMARY KEY (id_sucursal, id_producto_sucursal)
);
GO

-- Equivalencia: forma de pago de la sucursal -> forma de pago del servidor
IF OBJECT_ID('dbo.FormasPagoSucursal', 'U') IS NULL
CREATE TABLE dbo.FormasPagoSucursal (
    id_sucursal             int          NOT NULL,
    id_forma_pago_sucursal  int          NOT NULL,
    descripcion_sucursal    varchar(100) NULL,
    id_forma_pago           int          NOT NULL,
    fecha                   datetime     NOT NULL CONSTRAINT DF_FormasPagoSucursal_fecha DEFAULT (GETDATE()),
    CONSTRAINT PK_FormasPagoSucursal PRIMARY KEY (id_sucursal, id_forma_pago_sucursal)
);
GO

-- Propuesta / registro de unificación de productos duplicados del servidor
IF OBJECT_ID('dbo.ProductosUnificacion', 'U') IS NULL
CREATE TABLE dbo.ProductosUnificacion (
    id_viejo        int           NOT NULL CONSTRAINT PK_ProductosUnificacion PRIMARY KEY,
    id_nuevo        int           NOT NULL,
    motivo          varchar(50)   NOT NULL,   -- EAN / CODART_COLOR_TALLE / MANUAL
    aplicado        bit           NOT NULL CONSTRAINT DF_ProductosUnificacion_aplicado DEFAULT (0),
    fecha           datetime      NOT NULL CONSTRAINT DF_ProductosUnificacion_fecha DEFAULT (GETDATE()),
    fecha_aplicado  datetime      NULL
);
GO

------------------------------------------------------------------------------------------
-- sp_equivalencias_sucursal
-- Vincula cada producto de ProductosSucursalImport (de la sucursal @suc) con un producto
-- activo del servidor. Orden de búsqueda:
--   1) mismo EAN
--   2) mismo Cod_Art + Codigo_Color + Talle
--   3) misma Descripción + Descripción de color + Talle
-- Las equivalencias cargadas a mano (criterio = 'MANUAL') no se pisan.
-- Devuelve los productos de la sucursal que quedaron sin vincular.
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_equivalencias_sucursal
    @suc int
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Servidor AS (
        SELECT ID, ISNULL(Ean_2025, 0) AS Ean, ISNULL(Cod_Art, 0) AS Cod_Art, ISNULL(Codigo_Color, 0) AS Codigo_Color,
               UPPER(REPLACE(LTRIM(RTRIM(ISNULL(Talles, ''))), ' ', '')) AS Talle,
               UPPER(LTRIM(RTRIM(ISNULL(Descripcion_Codigo, '')))) AS Descripcion,
               UPPER(LTRIM(RTRIM(ISNULL(Descripcion_Color, '')))) AS Color
        FROM dbo.Productos
        WHERE Activo = 1
    ),
    Sucursal AS (
        SELECT ID, ISNULL(Ean_2025, 0) AS Ean, ISNULL(Cod_Art, 0) AS Cod_Art, ISNULL(Codigo_Color, 0) AS Codigo_Color,
               UPPER(REPLACE(LTRIM(RTRIM(ISNULL(Talles, ''))), ' ', '')) AS Talle,
               UPPER(LTRIM(RTRIM(ISNULL(Descripcion_Codigo, '')))) AS Descripcion,
               UPPER(LTRIM(RTRIM(ISNULL(Descripcion_Color, '')))) AS Color
        FROM dbo.ProductosSucursalImport
        WHERE id_sucursal = @suc
    ),
    Candidatos AS (
        SELECT s.ID AS id_producto_sucursal, x.ID AS id_producto, x.criterio, x.prioridad, x.revisar
        FROM Sucursal s
        CROSS APPLY (
            -- 1) EAN. Si el talle o color no coinciden queda marcado para revisar.
            SELECT * FROM (
                SELECT TOP 1 v.ID, 'EAN' AS criterio, 1 AS prioridad,
                       CAST(CASE WHEN v.Talle = s.Talle AND v.Codigo_Color = s.Codigo_Color THEN 0 ELSE 1 END AS bit) AS revisar
                FROM Servidor v WHERE s.Ean > 0 AND v.Ean = s.Ean
                ORDER BY CASE WHEN v.Talle = s.Talle AND v.Codigo_Color = s.Codigo_Color THEN 0 ELSE 1 END, v.ID
            ) e
            UNION ALL
            -- 2) Cod_Art + Color + Talle (si los dos tienen EAN y es distinto, no es el mismo producto)
            SELECT * FROM (
                SELECT TOP 1 v.ID, 'CODART_COLOR_TALLE' AS criterio, 2 AS prioridad, CAST(0 AS bit) AS revisar
                FROM Servidor v
                WHERE s.Cod_Art > 0 AND v.Cod_Art = s.Cod_Art AND v.Codigo_Color = s.Codigo_Color AND v.Talle = s.Talle
                  AND (s.Ean = 0 OR v.Ean = 0 OR v.Ean = s.Ean)
                ORDER BY v.ID
            ) c
            UNION ALL
            -- 3) Descripción + Color + Talle
            SELECT * FROM (
                SELECT TOP 1 v.ID, 'DESCRIPCION' AS criterio, 3 AS prioridad, CAST(1 AS bit) AS revisar
                FROM Servidor v
                WHERE s.Descripcion <> '' AND v.Descripcion = s.Descripcion AND v.Color = s.Color AND v.Talle = s.Talle
                  AND (s.Ean = 0 OR v.Ean = 0 OR v.Ean = s.Ean)
                ORDER BY v.ID
            ) d
        ) x
    ),
    Mejor AS (
        SELECT *, ROW_NUMBER() OVER (PARTITION BY id_producto_sucursal ORDER BY prioridad) AS rn
        FROM Candidatos
    )
    MERGE dbo.ProductosSucursal AS t
    USING (SELECT id_producto_sucursal, id_producto, criterio, revisar FROM Mejor WHERE rn = 1) AS m
        ON t.id_sucursal = @suc AND t.id_producto_sucursal = m.id_producto_sucursal
    WHEN MATCHED AND t.criterio <> 'MANUAL' THEN
        UPDATE SET id_producto = m.id_producto, criterio = m.criterio, revisar = m.revisar, fecha = GETDATE()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (id_sucursal, id_producto_sucursal, id_producto, criterio, revisar)
        VALUES (@suc, m.id_producto_sucursal, m.id_producto, m.criterio, m.revisar);

    -- Productos de la sucursal sin vincular
    SELECT i.*
    FROM dbo.ProductosSucursalImport i
    WHERE i.id_sucursal = @suc
      AND NOT EXISTS (SELECT 1 FROM dbo.ProductosSucursal ps
                      WHERE ps.id_sucursal = i.id_sucursal AND ps.id_producto_sucursal = i.ID)
    ORDER BY i.Descripcion_Codigo, i.Descripcion_Color, i.Talles;
END
GO

------------------------------------------------------------------------------------------
-- sp_traducir_ventas_sucursal
-- Pasa las líneas de detalle_ventas de la sucursal del ID de producto de la sucursal al
-- ID del servidor, usando ProductosSucursal. Solo toca líneas no traducidas todavía
-- (id_producto_sucursal IS NULL), así que se puede ejecutar varias veces.
-- Hace lo mismo con la forma de pago de la cabecera (ventas.forma_pago).
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_traducir_ventas_sucursal
    @suc int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    UPDATE dv
    SET id_producto_sucursal = dv.id_producto,
        id_producto = ps.id_producto
    FROM dbo.detalle_ventas dv
    INNER JOIN dbo.ProductosSucursal ps
        ON ps.id_sucursal = dv.id_sucursal AND ps.id_producto_sucursal = dv.id_producto
    WHERE dv.id_sucursal = @suc
      AND dv.id_producto_sucursal IS NULL;

    DECLARE @lineas int = @@ROWCOUNT;

    UPDATE v
    SET forma_pago_sucursal = v.forma_pago,
        forma_pago = fp.id_forma_pago
    FROM dbo.ventas v
    INNER JOIN dbo.FormasPagoSucursal fp
        ON fp.id_sucursal = v.id_sucursal AND fp.id_forma_pago_sucursal = v.forma_pago
    WHERE v.id_sucursal = @suc
      AND v.forma_pago_sucursal IS NULL;

    DECLARE @ventas int = @@ROWCOUNT;

    COMMIT;

    SELECT @lineas AS lineas_traducidas, @ventas AS ventas_forma_pago_traducidas,
           (SELECT COUNT(*) FROM dbo.detalle_ventas WHERE id_sucursal = @suc AND id_producto_sucursal IS NULL) AS lineas_sin_traducir;
END
GO

------------------------------------------------------------------------------------------
-- sp_proponer_unificacion
-- Detecta productos duplicados en el servidor y carga la propuesta en ProductosUnificacion
-- (borra la propuesta anterior no aplicada, salvo las filas MANUAL).
-- Se consideran el MISMO producto (unificación automática):
--   * mismo EAN y mismo Codigo_Color y mismo Talle
--   * mismo Cod_Art y mismo Codigo_Color y mismo Talle, sin EANs distintos entre ellos
-- Se elige como producto final el que tiene EAN, luego Cod_Art, luego Codigo_Equis, luego el menor ID.
-- Lo dudoso NO se unifica: se lista para revisar a mano (ver 02_diagnostico.sql).
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_proponer_unificacion
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.ProductosUnificacion WHERE aplicado = 0 AND motivo <> 'MANUAL';

    IF OBJECT_ID('tempdb..#p') IS NOT NULL DROP TABLE #p;

    SELECT p.ID,
           ISNULL(p.Ean_2025, 0) AS Ean,
           ISNULL(p.Cod_Art, 0) AS Cod_Art,
           ISNULL(p.Codigo_Color, 0) AS Codigo_Color,
           UPPER(REPLACE(LTRIM(RTRIM(ISNULL(p.Talles, ''))), ' ', '')) AS Talle,
           -- prioridad para elegir el producto que queda (1 = mejor)
           ROW_NUMBER() OVER (ORDER BY CASE WHEN ISNULL(p.Ean_2025, 0) > 0 THEN 0 ELSE 1 END,
                                       CASE WHEN ISNULL(p.Cod_Art, 0) > 0 THEN 0 ELSE 1 END,
                                       CASE WHEN ISNULL(p.Codigo_Equis, 0) > 0 THEN 0 ELSE 1 END,
                                       p.ID) AS prioridad,
           p.ID AS grupo,
           CAST('' AS varchar(50)) AS motivo
    INTO #p
    FROM dbo.Productos p
    WHERE p.Activo = 1
      -- los productos que ya están en una unificación manual pendiente no se tocan
      AND NOT EXISTS (SELECT 1 FROM dbo.ProductosUnificacion u WHERE u.aplicado = 0 AND (u.id_viejo = p.ID));

    -- el grupo se identifica con la mejor prioridad; se propaga hasta que no haya cambios
    UPDATE #p SET grupo = prioridad;

    DECLARE @cambios int = 1;
    WHILE @cambios > 0
    BEGIN
        SET @cambios = 0;

        -- mismo EAN + color + talle
        UPDATE p SET grupo = g.grupo, motivo = 'EAN'
        FROM #p p
        INNER JOIN (SELECT Ean, Codigo_Color, Talle, MIN(grupo) AS grupo
                    FROM #p WHERE Ean > 0
                    GROUP BY Ean, Codigo_Color, Talle) g
            ON g.Ean = p.Ean AND g.Codigo_Color = p.Codigo_Color AND g.Talle = p.Talle
        WHERE p.grupo > g.grupo;
        SET @cambios = @cambios + @@ROWCOUNT;

        -- mismo Cod_Art + color + talle, solo si en ese grupo no hay dos EAN distintos
        UPDATE p SET grupo = g.grupo, motivo = CASE WHEN p.motivo = '' THEN 'CODART_COLOR_TALLE' ELSE p.motivo END
        FROM #p p
        INNER JOIN (SELECT Cod_Art, Codigo_Color, Talle, MIN(grupo) AS grupo
                    FROM #p WHERE Cod_Art > 0
                    GROUP BY Cod_Art, Codigo_Color, Talle
                    HAVING COUNT(DISTINCT NULLIF(Ean, 0)) <= 1) g
            ON g.Cod_Art = p.Cod_Art AND g.Codigo_Color = p.Codigo_Color AND g.Talle = p.Talle
        WHERE p.grupo > g.grupo;
        SET @cambios = @cambios + @@ROWCOUNT;
    END

    INSERT INTO dbo.ProductosUnificacion (id_viejo, id_nuevo, motivo)
    SELECT p.ID, f.ID, p.motivo
    FROM #p p
    INNER JOIN #p f ON f.prioridad = p.grupo
    WHERE p.ID <> f.ID
      AND NOT EXISTS (SELECT 1 FROM dbo.ProductosUnificacion u WHERE u.id_viejo = p.ID);

    -- Resultado: propuesta para revisar
    SELECT u.id_nuevo, u.id_viejo, u.motivo,
           pv.Descripcion_Codigo AS desc_viejo, pv.Descripcion_Color AS color_viejo, pv.Talles AS talle_viejo, pv.Ean_2025 AS ean_viejo, pv.Cod_Art AS codart_viejo,
           pn.Descripcion_Codigo AS desc_nuevo, pn.Descripcion_Color AS color_nuevo, pn.Talles AS talle_nuevo, pn.Ean_2025 AS ean_nuevo, pn.Cod_Art AS codart_nuevo
    FROM dbo.ProductosUnificacion u
    INNER JOIN dbo.Productos pv ON pv.ID = u.id_viejo
    INNER JOIN dbo.Productos pn ON pn.ID = u.id_nuevo
    WHERE u.aplicado = 0
    ORDER BY u.id_nuevo, u.id_viejo;
END
GO

------------------------------------------------------------------------------------------
-- sp_aplicar_unificacion
-- Aplica ProductosUnificacion (filas aplicado = 0) en una transacción:
--   * completa en el producto final los datos que le falten (EAN, Cod_Art, Codigo_Equis)
--   * cambia el ID viejo por el nuevo en todas las tablas que referencian productos
--   * deja el producto viejo inactivo (Activo = 0, ID_Unificado = nuevo). No se borra nada.
-- En detalle_ventas solo se tocan líneas que ya tienen ID del servidor: las traducidas
-- (id_producto_sucursal NOT NULL) y las de sucursales sin catálogo cargado en ProductosSucursal.
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_aplicar_unificacion
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Cadenas: si A -> B y B -> C, A debe ir directo a C
    DECLARE @n int = 1;
    WHILE @n > 0
    BEGIN
        UPDATE a SET id_nuevo = b.id_nuevo
        FROM dbo.ProductosUnificacion a
        INNER JOIN dbo.ProductosUnificacion b ON b.id_viejo = a.id_nuevo
        WHERE a.aplicado = 0 AND a.id_nuevo <> b.id_nuevo AND a.id_viejo <> b.id_nuevo;
        SET @n = @@ROWCOUNT;
    END

    IF EXISTS (SELECT 1 FROM dbo.ProductosUnificacion WHERE aplicado = 0 AND id_viejo = id_nuevo)
       -- ciclos (A -> B y B -> A)
       OR EXISTS (SELECT 1 FROM dbo.ProductosUnificacion a
                  INNER JOIN dbo.ProductosUnificacion b ON b.id_viejo = a.id_nuevo
                  WHERE a.aplicado = 0 AND b.aplicado = 0)
       OR EXISTS (SELECT 1 FROM dbo.ProductosUnificacion u
                  LEFT JOIN dbo.Productos p ON p.ID = u.id_nuevo
                  WHERE u.aplicado = 0 AND (p.ID IS NULL OR p.Activo = 0))
    BEGIN
        RAISERROR(N'ProductosUnificacion tiene filas inválidas (id_viejo = id_nuevo, ciclos, o id_nuevo inexistente/inactivo). No se aplicó nada.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    -- 1) Completar datos faltantes del producto final con los de sus duplicados
    UPDATE pn SET
        Ean_2025     = CASE WHEN ISNULL(pn.Ean_2025, 0) = 0 THEN d.Ean ELSE pn.Ean_2025 END,
        Cod_Art      = CASE WHEN ISNULL(pn.Cod_Art, 0) = 0 THEN d.Cod_Art ELSE pn.Cod_Art END,
        Codigo_Equis = CASE WHEN ISNULL(pn.Codigo_Equis, 0) = 0 THEN d.Equis ELSE pn.Codigo_Equis END
    FROM dbo.Productos pn
    INNER JOIN (
        SELECT u.id_nuevo,
               MAX(NULLIF(ISNULL(pv.Ean_2025, 0), 0)) AS Ean,
               MAX(NULLIF(ISNULL(pv.Cod_Art, 0), 0)) AS Cod_Art,
               MAX(NULLIF(ISNULL(pv.Codigo_Equis, 0), 0)) AS Equis
        FROM dbo.ProductosUnificacion u
        INNER JOIN dbo.Productos pv ON pv.ID = u.id_viejo
        WHERE u.aplicado = 0
        GROUP BY u.id_nuevo
    ) d ON d.id_nuevo = pn.ID;

    -- 2) Reemplazar referencias. Se ignoran las tablas/columnas que no existan.
    DECLARE @refs TABLE (tabla sysname, columna sysname, filtro nvarchar(400));
    INSERT INTO @refs VALUES
        (N'detalle_ventas',           N'id_producto', N' AND (t.id_producto_sucursal IS NOT NULL OR NOT EXISTS (SELECT 1 FROM dbo.ProductosSucursal ps WHERE ps.id_sucursal = t.id_sucursal))'),
        (N'compras',                  N'ID_Producto', N''),
        (N'IngresoMercaderiaDetalle', N'ProductoId',  N''),
        (N'MapeoProductoProveedor',   N'ProductoId',  N''),
        (N'traslados',                N'ID_Producto', N''),
        (N'ajustes',                  N'producto',    N''),
        (N'ajustes',                  N'id_producto', N''),
        (N'ajustes_stock',            N'producto',    N''),
        (N'precios',                  N'idProducto',  N''),
        (N'precios',                  N'id_producto', N''),
        (N'ProductosSucursal',        N'id_producto', N'');

    DECLARE @tabla sysname, @columna sysname, @filtro nvarchar(400), @sql nvarchar(max);
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT tabla, columna, filtro FROM @refs;
    OPEN c;
    FETCH NEXT FROM c INTO @tabla, @columna, @filtro;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF COL_LENGTH(N'dbo.' + @tabla, @columna) IS NOT NULL
        BEGIN
            SET @sql = N'UPDATE t SET ' + QUOTENAME(@columna) + N' = u.id_nuevo FROM dbo.' + QUOTENAME(@tabla) + N' t ' +
                       N'INNER JOIN dbo.ProductosUnificacion u ON u.id_viejo = t.' + QUOTENAME(@columna) +
                       N' WHERE u.aplicado = 0' + @filtro + N';' +
                       N'PRINT ''' + @tabla + N'.' + @columna + N': '' + CAST(@@ROWCOUNT AS varchar(20)) + '' filas'';';
            EXEC sp_executesql @sql;
        END
        FETCH NEXT FROM c INTO @tabla, @columna, @filtro;
    END
    CLOSE c;
    DEALLOCATE c;

    -- 3) Desactivar duplicados
    UPDATE p SET Activo = 0, ID_Unificado = u.id_nuevo
    FROM dbo.Productos p
    INNER JOIN dbo.ProductosUnificacion u ON u.id_viejo = p.ID
    WHERE u.aplicado = 0;

    -- Si algún producto ya unificado antes apuntaba a uno que ahora se unificó, actualizarlo
    UPDATE p SET ID_Unificado = u.id_nuevo
    FROM dbo.Productos p
    INNER JOIN dbo.ProductosUnificacion u ON u.id_viejo = p.ID_Unificado
    WHERE u.aplicado = 0;

    UPDATE dbo.ProductosUnificacion SET aplicado = 1, fecha_aplicado = GETDATE() WHERE aplicado = 0;

    COMMIT;
END
GO

SET NOEXEC OFF;
GO
PRINT 'Estructura de unificación creada / actualizada.';
GO
