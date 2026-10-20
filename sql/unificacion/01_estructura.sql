/*
    01_estructura.sql
    -----------------
    Crea las columnas, tablas y procedimientos que usan la unificación de productos con la
    LISTA MAESTRA (productos.xlsx) y la API para traducir los productos / formas de pago de cada
    sucursal a los del servidor.

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
    (N'dbo.Productos', N'ID'), (N'dbo.Productos', N'Orden'), (N'dbo.Productos', N'Cod_Art'), (N'dbo.Productos', N'Codigo_Color'),
    (N'dbo.Productos', N'Talles'), (N'dbo.Productos', N'Ean_2025'), (N'dbo.Productos', N'Codigo_Equis'),
    (N'dbo.Productos', N'Descripcion_Codigo'), (N'dbo.Productos', N'Descripcion_Color'),
    (N'dbo.Productos', N'Categoria'), (N'dbo.Productos', N'Genero'), (N'dbo.Productos', N'curva'),
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
-- Productos.Activo: los productos que no están en la lista maestra o que se unificaron quedan
-- inactivos (no se borran)
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
-- Lista maestra de productos (productos.xlsx). Se carga con 04_productos_maestro.sql.
-- id_producto = ID del producto del servidor que le corresponde (lo completa sp_maestro_preparar /
-- sp_maestro_aplicar). duplicado_de = fila repetida dentro de la misma lista.
IF OBJECT_ID('dbo.ProductosMaestro', 'U') IS NULL
CREATE TABLE dbo.ProductosMaestro (
    fila                int           NOT NULL CONSTRAINT PK_ProductosMaestro PRIMARY KEY,
    Cod_Art             int           NOT NULL,
    Descripcion_Codigo  varchar(100)  NOT NULL,
    Precio              decimal(18,2) NULL,
    Categoria           varchar(50)   NULL,
    Genero              varchar(50)   NULL,
    Codigo_Color        int           NOT NULL,
    Descripcion_Color   varchar(100)  NULL,
    Talles              varchar(50)   NULL,
    Ean_2025            bigint        NULL,
    duplicado_de        int           NULL,
    id_producto         int           NULL,
    -- columnas normalizadas para buscar (las completa sp_maestro_normalizar)
    n_descripcion       varchar(200)  NULL,
    n_color             varchar(200)  NULL,
    n_talle             varchar(50)   NULL,
    unico_codart_codcolor_talle bit   NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductosMaestro_Ean' AND object_id = OBJECT_ID('dbo.ProductosMaestro'))
    CREATE INDEX IX_ProductosMaestro_Ean ON dbo.ProductosMaestro (Ean_2025) INCLUDE (duplicado_de, n_color, n_talle, Codigo_Color);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductosMaestro_CodArt' AND object_id = OBJECT_ID('dbo.ProductosMaestro'))
    CREATE INDEX IX_ProductosMaestro_CodArt ON dbo.ProductosMaestro (Cod_Art, Codigo_Color, n_talle) INCLUDE (duplicado_de, n_color, Ean_2025, unico_codart_codcolor_talle);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductosMaestro_Descripcion' AND object_id = OBJECT_ID('dbo.ProductosMaestro'))
    CREATE INDEX IX_ProductosMaestro_Descripcion ON dbo.ProductosMaestro (n_descripcion, n_color, n_talle) INCLUDE (duplicado_de, Ean_2025);
GO

-- Talles escritos de otra forma en productos viejos o sucursales -> talle como está en la lista maestra.
-- Cargar en mayúsculas y sin espacios. Ej: ('39/40', '390'), ('39-40', '390').
IF OBJECT_ID('dbo.TallesEquivalencia', 'U') IS NULL
CREATE TABLE dbo.TallesEquivalencia (
    talle          varchar(50) NOT NULL CONSTRAINT PK_TallesEquivalencia PRIMARY KEY,
    talle_maestro  varchar(50) NOT NULL
);
GO

-- Vínculo de cada producto del servidor con su fila de la lista maestra (lo arma sp_maestro_preparar)
IF OBJECT_ID('dbo.ProductosMaestroVinculo', 'U') IS NULL
CREATE TABLE dbo.ProductosMaestroVinculo (
    id_producto  int          NOT NULL CONSTRAINT PK_ProductosMaestroVinculo PRIMARY KEY,
    fila         int          NOT NULL,
    criterio     varchar(30)  NOT NULL,   -- EAN / CODART_COLOR_TALLE / CODART_CODCOLOR_TALLE / DESCRIPCION
    revisar      bit          NOT NULL
);
GO

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
    criterio             varchar(30)  NOT NULL,   -- EAN / CODART_COLOR_TALLE / CODART_CODCOLOR_TALLE / DESCRIPCION / MANUAL
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

-- Unificación de productos del servidor: id_viejo pasa a ser id_nuevo
IF OBJECT_ID('dbo.ProductosUnificacion', 'U') IS NULL
CREATE TABLE dbo.ProductosUnificacion (
    id_viejo        int           NOT NULL CONSTRAINT PK_ProductosUnificacion PRIMARY KEY,
    id_nuevo        int           NOT NULL,
    motivo          varchar(50)   NOT NULL,   -- MAESTRO_<criterio> / MANUAL
    aplicado        bit           NOT NULL CONSTRAINT DF_ProductosUnificacion_aplicado DEFAULT (0),
    fecha           datetime      NOT NULL CONSTRAINT DF_ProductosUnificacion_fecha DEFAULT (GETDATE()),
    fecha_aplicado  datetime      NULL
);
GO

------------------------------------------------------------------------------------------
-- fn_talle: talle normalizado (mayúsculas, sin espacios) y traducido con TallesEquivalencia
------------------------------------------------------------------------------------------
CREATE OR ALTER FUNCTION dbo.fn_talle (@talle varchar(50))
RETURNS varchar(50)
AS
BEGIN
    DECLARE @t varchar(50) = UPPER(REPLACE(LTRIM(RTRIM(ISNULL(@talle, ''))), ' ', ''));
    SELECT @t = talle_maestro FROM dbo.TallesEquivalencia WHERE talle = @t;
    RETURN @t;
END
GO

------------------------------------------------------------------------------------------
-- fn_texto: texto normalizado para comparar descripciones (mayúsculas, sin espacios de más)
------------------------------------------------------------------------------------------
CREATE OR ALTER FUNCTION dbo.fn_texto (@texto varchar(200))
RETURNS varchar(200)
AS
BEGIN
    DECLARE @t varchar(200) = UPPER(LTRIM(RTRIM(ISNULL(@texto, ''))));
    WHILE CHARINDEX('  ', @t) > 0 SET @t = REPLACE(@t, '  ', ' ');
    SET @t = REPLACE(REPLACE(@t, ' /', '/'), '/ ', '/');
    RETURN @t;
END
GO

------------------------------------------------------------------------------------------
-- sp_maestro_normalizar: completa las columnas normalizadas de ProductosMaestro.
-- Volver a ejecutar si se cambia TallesEquivalencia (lo llaman sp_maestro_preparar y sp_equivalencias_sucursal).
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_maestro_normalizar
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.ProductosMaestro
    SET n_descripcion = dbo.fn_texto(Descripcion_Codigo),
        n_color       = dbo.fn_texto(Descripcion_Color),
        n_talle       = dbo.fn_talle(Talles);

    ;WITH C AS (
        SELECT unico_codart_codcolor_talle,
               COUNT(*) OVER (PARTITION BY Cod_Art, Codigo_Color, n_talle) AS cantidad
        FROM dbo.ProductosMaestro
        WHERE duplicado_de IS NULL
    )
    UPDATE C SET unico_codart_codcolor_talle = CASE WHEN cantidad = 1 THEN 1 ELSE 0 END;
END
GO

------------------------------------------------------------------------------------------
-- fn_buscar_maestro: busca la fila de la lista maestra que corresponde a un producto.
-- Recibe descripción, color y talle ya normalizados (dbo.fn_texto / dbo.fn_talle).
-- Orden de búsqueda (gana el primero que encuentra):
--   1) EAN
--   2) Cod_Art + Codigo_Color + Descripción de color + Talle
--   3) Cod_Art + Codigo_Color + Talle, solo si en la lista hay UNA sola fila así
--   4) Descripción + Descripción de color + Talle
-- En 2, 3 y 4 no se vincula si los dos tienen EAN y es distinto.
-- revisar = 1 cuando el vínculo es por EAN pero no coincide el talle o el color, o es por descripción.
------------------------------------------------------------------------------------------
CREATE OR ALTER FUNCTION dbo.fn_buscar_maestro
(
    @ean bigint, @cod_art int, @codigo_color int, @n_descripcion varchar(200), @n_color varchar(200), @n_talle varchar(50)
)
RETURNS TABLE
AS
RETURN
(
    SELECT TOP 1 fila, criterio, revisar
    FROM (
        SELECT m.fila, 'EAN' AS criterio, 1 AS prioridad,
               CAST(CASE WHEN m.n_talle = @n_talle AND (m.Codigo_Color = ISNULL(@codigo_color, 0) OR m.n_color = @n_color) THEN 0 ELSE 1 END AS bit) AS revisar
        FROM dbo.ProductosMaestro m
        WHERE ISNULL(@ean, 0) > 0 AND m.Ean_2025 = @ean AND m.duplicado_de IS NULL
        UNION ALL
        SELECT m.fila, 'CODART_COLOR_TALLE', 2, CAST(0 AS bit)
        FROM dbo.ProductosMaestro m
        WHERE ISNULL(@cod_art, 0) > 0 AND m.Cod_Art = @cod_art AND m.Codigo_Color = ISNULL(@codigo_color, 0)
          AND m.n_talle = @n_talle AND m.n_color = @n_color AND m.duplicado_de IS NULL
          AND (ISNULL(@ean, 0) = 0 OR ISNULL(m.Ean_2025, 0) = 0 OR m.Ean_2025 = @ean)
        UNION ALL
        SELECT m.fila, 'CODART_CODCOLOR_TALLE', 3, CAST(0 AS bit)
        FROM dbo.ProductosMaestro m
        WHERE ISNULL(@cod_art, 0) > 0 AND m.Cod_Art = @cod_art AND m.Codigo_Color = ISNULL(@codigo_color, 0)
          AND m.n_talle = @n_talle AND m.unico_codart_codcolor_talle = 1 AND m.duplicado_de IS NULL
          AND (ISNULL(@ean, 0) = 0 OR ISNULL(m.Ean_2025, 0) = 0 OR m.Ean_2025 = @ean)
        UNION ALL
        SELECT m.fila, 'DESCRIPCION', 4, CAST(1 AS bit)
        FROM dbo.ProductosMaestro m
        WHERE @n_descripcion <> '' AND m.n_descripcion = @n_descripcion AND m.n_color = @n_color AND m.n_talle = @n_talle
          AND m.duplicado_de IS NULL
          AND (ISNULL(@ean, 0) = 0 OR ISNULL(m.Ean_2025, 0) = 0 OR m.Ean_2025 = @ean)
    ) c
    ORDER BY prioridad, fila
);
GO

------------------------------------------------------------------------------------------
-- sp_maestro_preparar
-- Arma la propuesta para dejar el servidor igual a la lista maestra. NO cambia productos ni movimientos.
--   1) Marca filas repetidas dentro de la lista (queda la que tiene EAN, si no la primera).
--   2) Vincula cada producto activo del servidor con su fila de la lista (ProductosMaestroVinculo).
--   3) Por cada fila elige el producto del servidor que queda (conserva su ID):
--        a) descripción en MAYÚSCULAS y datos completos (talle, color y EAN)
--        b) datos completos   c) en MAYÚSCULAS   d) con EAN, con Cod_Art, menor ID
--      Los demás productos vinculados a la misma fila se unifican en él (ProductosUnificacion).
--      Las filas sin ningún producto se crearán con ID nuevo en sp_maestro_aplicar.
--   4) Devuelve resúmenes para revisar.
-- Las filas MANUAL de ProductosUnificacion se respetan.
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_maestro_preparar
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) Filas repetidas en la lista (mismo Cod_Art, descripción, código y descripción de color y talle, sin EAN distintos)
    UPDATE dbo.ProductosMaestro SET duplicado_de = NULL, id_producto = NULL;
    EXEC dbo.sp_maestro_normalizar;

    ;WITH Normalizada AS (
        SELECT fila, ISNULL(Ean_2025, 0) AS Ean,
               CONCAT(Cod_Art, '|', n_descripcion, '|', Codigo_Color, '|', n_color, '|', n_talle) AS clave
        FROM dbo.ProductosMaestro
    ),
    Grupos AS (
        SELECT clave FROM Normalizada GROUP BY clave
        HAVING COUNT(*) > 1 AND COUNT(DISTINCT NULLIF(Ean, 0)) <= 1
    ),
    Primera AS (
        SELECT n.fila,
               FIRST_VALUE(n.fila) OVER (PARTITION BY n.clave ORDER BY CASE WHEN n.Ean > 0 THEN 0 ELSE 1 END, n.fila) AS queda
        FROM Normalizada n INNER JOIN Grupos g ON g.clave = n.clave
    )
    UPDATE m SET duplicado_de = p.queda
    FROM dbo.ProductosMaestro m INNER JOIN Primera p ON p.fila = m.fila
    WHERE p.fila <> p.queda;

    EXEC dbo.sp_maestro_normalizar;   -- recalcula unico_codart_codcolor_talle sin las filas repetidas

    -- 2) Vincular productos del servidor (los que están en una unificación MANUAL pendiente no se vinculan)
    DELETE FROM dbo.ProductosMaestroVinculo;

    INSERT INTO dbo.ProductosMaestroVinculo (id_producto, fila, criterio, revisar)
    SELECT p.ID, b.fila, b.criterio, b.revisar
    FROM dbo.Productos p
    CROSS APPLY (SELECT dbo.fn_texto(p.Descripcion_Codigo) AS d, dbo.fn_texto(p.Descripcion_Color) AS c, dbo.fn_talle(p.Talles) AS t) n
    CROSS APPLY dbo.fn_buscar_maestro(p.Ean_2025, p.Cod_Art, p.Codigo_Color, n.d, n.c, n.t) b
    WHERE p.Activo = 1
      AND NOT EXISTS (SELECT 1 FROM dbo.ProductosUnificacion u WHERE u.aplicado = 0 AND u.motivo = 'MANUAL' AND u.id_viejo = p.ID);

    -- 3) Producto que queda por fila
    ;WITH Prioridad AS (
        SELECT v.fila, v.id_producto,
               ROW_NUMBER() OVER (PARTITION BY v.fila ORDER BY
                   CASE WHEN m.mayusculas = 1 AND m.completo = 1 THEN 0
                        WHEN m.completo = 1 THEN 1
                        WHEN m.mayusculas = 1 THEN 2
                        ELSE 3 END,
                   CASE WHEN ISNULL(p.Ean_2025, 0) > 0 THEN 0 ELSE 1 END,
                   CASE WHEN ISNULL(p.Cod_Art, 0) > 0 THEN 0 ELSE 1 END,
                   p.ID) AS rn
        FROM dbo.ProductosMaestroVinculo v
        INNER JOIN dbo.Productos p ON p.ID = v.id_producto
        CROSS APPLY (SELECT
            CASE WHEN LTRIM(RTRIM(ISNULL(p.Descripcion_Codigo, ''))) <> ''
                  AND ISNULL(p.Descripcion_Codigo, '') COLLATE Latin1_General_CS_AS = UPPER(ISNULL(p.Descripcion_Codigo, '')) COLLATE Latin1_General_CS_AS
                 THEN 1 ELSE 0 END AS mayusculas,
            CASE WHEN LTRIM(RTRIM(ISNULL(p.Talles, ''))) <> ''
                  AND (ISNULL(p.Codigo_Color, 0) > 0 OR LTRIM(RTRIM(ISNULL(p.Descripcion_Color, ''))) <> '')
                  AND ISNULL(p.Ean_2025, 0) > 0
                 THEN 1 ELSE 0 END AS completo
        ) m
    )
    UPDATE mm SET id_producto = pr.id_producto
    FROM dbo.ProductosMaestro mm
    INNER JOIN Prioridad pr ON pr.fila = mm.fila AND pr.rn = 1;

    DELETE FROM dbo.ProductosUnificacion WHERE aplicado = 0 AND motivo <> 'MANUAL';

    INSERT INTO dbo.ProductosUnificacion (id_viejo, id_nuevo, motivo)
    SELECT v.id_producto, mm.id_producto, 'MAESTRO_' + v.criterio
    FROM dbo.ProductosMaestroVinculo v
    INNER JOIN dbo.ProductosMaestro mm ON mm.fila = v.fila
    WHERE v.id_producto <> mm.id_producto
      AND NOT EXISTS (SELECT 1 FROM dbo.ProductosUnificacion u WHERE u.id_viejo = v.id_producto);

    -- 4) Resultados
    SELECT '1) Resumen' AS resultado,
           (SELECT COUNT(*) FROM dbo.ProductosMaestro) AS filas_lista,
           (SELECT COUNT(*) FROM dbo.ProductosMaestro WHERE duplicado_de IS NOT NULL) AS filas_repetidas_en_lista,
           (SELECT COUNT(*) FROM dbo.ProductosMaestro WHERE duplicado_de IS NULL AND id_producto IS NOT NULL) AS filas_con_producto_existente,
           (SELECT COUNT(*) FROM dbo.ProductosMaestro WHERE duplicado_de IS NULL AND id_producto IS NULL) AS filas_a_crear,
           (SELECT COUNT(*) FROM dbo.Productos WHERE Activo = 1) AS productos_activos_servidor,
           (SELECT COUNT(*) FROM dbo.ProductosUnificacion WHERE aplicado = 0) AS productos_a_unificar,
           (SELECT COUNT(*) FROM dbo.Productos p WHERE p.Activo = 1
              AND NOT EXISTS (SELECT 1 FROM dbo.ProductosMaestroVinculo v WHERE v.id_producto = p.ID)
              AND NOT EXISTS (SELECT 1 FROM dbo.ProductosUnificacion u WHERE u.aplicado = 0 AND u.id_viejo = p.ID)) AS productos_fuera_de_lista;

    SELECT '2) REVISAR vínculos dudosos' AS resultado, v.criterio,
           p.ID, p.Descripcion_Codigo, p.Descripcion_Color, p.Talles, p.Ean_2025, p.Cod_Art, p.Codigo_Color,
           mm.fila, mm.Descripcion_Codigo AS desc_lista, mm.Descripcion_Color AS color_lista, mm.Talles AS talle_lista,
           mm.Ean_2025 AS ean_lista, mm.Cod_Art AS codart_lista, mm.Codigo_Color AS codcolor_lista
    FROM dbo.ProductosMaestroVinculo v
    INNER JOIN dbo.Productos p ON p.ID = v.id_producto
    INNER JOIN dbo.ProductosMaestro mm ON mm.fila = v.fila
    WHERE v.revisar = 1
    ORDER BY mm.fila, p.ID;

    -- Productos fuera de la lista: con movimientos conviene unificarlos a mano con su producto de la lista
    -- (INSERT INTO ProductosUnificacion ... 'MANUAL'); los que no, se dan de baja al aplicar.
    SELECT '3) Productos del servidor que NO están en la lista' AS resultado,
           p.ID, p.Descripcion_Codigo, p.Descripcion_Color, p.Talles, p.Ean_2025, p.Cod_Art, p.Codigo_Color,
           (SELECT COUNT(*) FROM dbo.detalle_ventas dv WHERE dv.id_producto = p.ID) AS lineas_venta,
           (SELECT COUNT(*) FROM dbo.traslados t WHERE t.ID_Producto = p.ID) AS traslados,
           (SELECT COUNT(*) FROM dbo.IngresoMercaderiaDetalle i WHERE i.ProductoId = p.ID) AS ingresos,
           (SELECT COUNT(*) FROM dbo.compras c WHERE c.ID_Producto = p.ID) AS compras
    FROM dbo.Productos p
    WHERE p.Activo = 1
      AND NOT EXISTS (SELECT 1 FROM dbo.ProductosMaestroVinculo v WHERE v.id_producto = p.ID)
      AND NOT EXISTS (SELECT 1 FROM dbo.ProductosUnificacion u WHERE u.aplicado = 0 AND u.id_viejo = p.ID)
    ORDER BY lineas_venta DESC, p.Descripcion_Codigo, p.Descripcion_Color, p.Talles;

    SELECT '4) Unificaciones propuestas' AS resultado, u.motivo,
           u.id_nuevo, pn.Descripcion_Codigo AS desc_queda, pn.Descripcion_Color AS color_queda, pn.Talles AS talle_queda, pn.Ean_2025 AS ean_queda,
           u.id_viejo, pv.Descripcion_Codigo AS desc_viejo, pv.Descripcion_Color AS color_viejo, pv.Talles AS talle_viejo, pv.Ean_2025 AS ean_viejo
    FROM dbo.ProductosUnificacion u
    INNER JOIN dbo.Productos pn ON pn.ID = u.id_nuevo
    INNER JOIN dbo.Productos pv ON pv.ID = u.id_viejo
    WHERE u.aplicado = 0
    ORDER BY u.id_nuevo, u.id_viejo;

    SELECT '5) Filas de la lista que se van a crear' AS resultado, mm.*
    FROM dbo.ProductosMaestro mm
    WHERE mm.duplicado_de IS NULL AND mm.id_producto IS NULL
    ORDER BY mm.fila;

    SELECT '6) Filas repetidas dentro de la lista (no se crean)' AS resultado, mm.*
    FROM dbo.ProductosMaestro mm
    WHERE mm.duplicado_de IS NOT NULL
    ORDER BY mm.duplicado_de, mm.fila;

    SELECT '7) Datos sospechosos en la lista: mismo Cod_Art + código de color con distinta descripción de color' AS resultado,
           mm.Cod_Art, mm.Codigo_Color, mm.Descripcion_Color, COUNT(*) AS filas
    FROM dbo.ProductosMaestro mm
    WHERE mm.Cod_Art > 0 AND mm.duplicado_de IS NULL
      AND EXISTS (SELECT 1 FROM dbo.ProductosMaestro o WHERE o.Cod_Art = mm.Cod_Art AND o.Codigo_Color = mm.Codigo_Color
                  AND o.n_color <> mm.n_color AND o.duplicado_de IS NULL)
    GROUP BY mm.Cod_Art, mm.Codigo_Color, mm.Descripcion_Color
    ORDER BY mm.Cod_Art, mm.Codigo_Color;
END
GO

------------------------------------------------------------------------------------------
-- sp_aplicar_unificacion
-- Aplica ProductosUnificacion (filas aplicado = 0):
--   * @completar_datos = 1: completa en el producto que queda los datos que le falten (EAN, Cod_Art, Codigo_Equis)
--   * cambia el ID viejo por el nuevo en todas las tablas que referencian productos
--   * deja el producto viejo inactivo (Activo = 0, ID_Unificado = nuevo). No se borra nada.
-- En detalle_ventas solo se tocan líneas que ya tienen ID del servidor: las traducidas
-- (id_producto_sucursal NOT NULL) y las de sucursales que no tienen catálogo cargado en
-- ProductosSucursalImport (se asume que usan los IDs del servidor).
-- Devuelve 0 si aplicó, 1 si no aplicó nada por datos inválidos.
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_aplicar_unificacion
    @completar_datos bit = 1
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
        WHERE a.aplicado = 0 AND b.aplicado = 0 AND a.id_nuevo <> b.id_nuevo AND a.id_viejo <> b.id_nuevo;
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
        RETURN 1;
    END

    BEGIN TRAN;

    -- 1) Completar datos faltantes del producto final con los de sus duplicados
    IF @completar_datos = 1
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
        (N'detalle_ventas',           N'id_producto', N' AND (t.id_producto_sucursal IS NOT NULL OR NOT EXISTS (SELECT 1 FROM dbo.ProductosSucursalImport i WHERE i.id_sucursal = t.id_sucursal))'),
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
    RETURN 0;
END
GO

------------------------------------------------------------------------------------------
-- sp_maestro_aplicar
-- Deja el servidor igual a la lista maestra, en una sola transacción (usa lo que armó sp_maestro_preparar):
--   1) Crea los productos de las filas sin producto (ID = MAX(ID) + 1 ...)
--   2) Pisa los datos de cada producto que queda con los de su fila de la lista
--   3) Unifica los demás (sp_aplicar_unificacion): ventas, ingresos, traslados, etc. pasan al que queda
--   4) @desactivar_fuera_de_lista = 1: da de baja (Activo = 0) los productos que no están en la lista
-- Antes de aplicar hay que tener cargado en ProductosSucursalImport el catálogo de TODAS las sucursales
-- que mandaron ventas con sus propios IDs (03_catalogos_sucursales.sql). Si hay ventas de sucursales sin
-- catálogo cargado no aplica, salvo @sucursales_sin_catalogo_usan_ids_servidor = 1.
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_maestro_aplicar
    @desactivar_fuera_de_lista bit = 1,
    @sucursales_sin_catalogo_usan_ids_servidor bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.ProductosMaestroVinculo)
    BEGIN
        RAISERROR(N'Primero ejecutar sp_maestro_preparar.', 16, 1);
        RETURN 1;
    END

    IF @sucursales_sin_catalogo_usan_ids_servidor = 0
       AND EXISTS (SELECT 1 FROM dbo.detalle_ventas dv
                   WHERE dv.id_producto_sucursal IS NULL
                     AND NOT EXISTS (SELECT 1 FROM dbo.ProductosSucursalImport i WHERE i.id_sucursal = dv.id_sucursal))
    BEGIN
        SELECT 'Sucursales con ventas y sin catálogo cargado' AS resultado, dv.id_sucursal, COUNT(*) AS lineas
        FROM dbo.detalle_ventas dv
        WHERE dv.id_producto_sucursal IS NULL
          AND NOT EXISTS (SELECT 1 FROM dbo.ProductosSucursalImport i WHERE i.id_sucursal = dv.id_sucursal)
        GROUP BY dv.id_sucursal;
        RAISERROR(N'Hay ventas de sucursales sin catálogo en ProductosSucursalImport (ver resultado). Cargarlo (03_catalogos_sucursales.sql) o, si esas sucursales usan los IDs del servidor, ejecutar con @sucursales_sin_catalogo_usan_ids_servidor = 1.', 16, 1);
        RETURN 1;
    END

    BEGIN TRAN;

    -- 1) Crear los productos que faltan
    DECLARE @max_id int, @max_orden int;
    SELECT @max_id = ISNULL(MAX(ID), 0), @max_orden = ISNULL(MAX(Orden), 0) FROM dbo.Productos WITH (UPDLOCK, HOLDLOCK);

    ;WITH Nuevas AS (
        SELECT mm.fila, ROW_NUMBER() OVER (ORDER BY mm.fila) AS rn
        FROM dbo.ProductosMaestro mm
        WHERE mm.duplicado_de IS NULL AND mm.id_producto IS NULL
    )
    UPDATE mm SET id_producto = @max_id + n.rn
    FROM dbo.ProductosMaestro mm INNER JOIN Nuevas n ON n.fila = mm.fila;

    INSERT INTO dbo.Productos (ID, Orden, Codigo_Equis, Cod_Art, Descripcion_Codigo, Genero, curva, Categoria,
                               Codigo_Color, Descripcion_Color, Talles, Ean_2025, Activo)
    SELECT mm.id_producto, @max_orden + (mm.id_producto - @max_id), 0, mm.Cod_Art, mm.Descripcion_Codigo, ISNULL(mm.Genero, ''), '',
           ISNULL(mm.Categoria, ''), mm.Codigo_Color, ISNULL(mm.Descripcion_Color, ''), ISNULL(mm.Talles, ''), ISNULL(mm.Ean_2025, 0), 1
    FROM dbo.ProductosMaestro mm
    WHERE mm.duplicado_de IS NULL AND mm.id_producto > @max_id;

    PRINT 'Productos creados: ' + CAST(@@ROWCOUNT AS varchar(20));

    -- 2) Datos de la lista en los productos que quedan
    UPDATE p SET
        Cod_Art            = mm.Cod_Art,
        Descripcion_Codigo = mm.Descripcion_Codigo,
        Categoria          = ISNULL(mm.Categoria, p.Categoria),
        Genero             = ISNULL(mm.Genero, p.Genero),
        Codigo_Color       = mm.Codigo_Color,
        Descripcion_Color  = ISNULL(mm.Descripcion_Color, ''),
        Talles             = ISNULL(mm.Talles, ''),
        Ean_2025           = ISNULL(mm.Ean_2025, 0),
        Activo             = 1
    FROM dbo.Productos p
    INNER JOIN dbo.ProductosMaestro mm ON mm.id_producto = p.ID
    WHERE mm.duplicado_de IS NULL AND mm.id_producto <= @max_id;

    PRINT 'Productos actualizados con la lista: ' + CAST(@@ROWCOUNT AS varchar(20));

    -- 3) Unificar (la lista manda: no se completan datos con los de los duplicados)
    DECLARE @rc int;
    EXEC @rc = dbo.sp_aplicar_unificacion @completar_datos = 0;
    IF @rc <> 0
    BEGIN
        IF @@TRANCOUNT > 0 ROLLBACK;
        RETURN 1;
    END

    -- 4) Baja de los productos que no están en la lista
    IF @desactivar_fuera_de_lista = 1
    BEGIN
        UPDATE p SET Activo = 0
        FROM dbo.Productos p
        WHERE p.Activo = 1
          AND NOT EXISTS (SELECT 1 FROM dbo.ProductosMaestro mm WHERE mm.id_producto = p.ID AND mm.duplicado_de IS NULL);
        PRINT 'Productos fuera de la lista dados de baja: ' + CAST(@@ROWCOUNT AS varchar(20));
    END

    COMMIT;

    SELECT 'Resultado' AS resultado,
           (SELECT COUNT(*) FROM dbo.Productos WHERE Activo = 1) AS productos_activos,
           (SELECT COUNT(*) FROM dbo.ProductosMaestro WHERE duplicado_de IS NULL) AS filas_lista;
    RETURN 0;
END
GO

------------------------------------------------------------------------------------------
-- sp_equivalencias_sucursal
-- Vincula cada producto de ProductosSucursalImport (de la sucursal @suc) con un producto activo
-- del servidor, usando la lista maestra (fn_buscar_maestro). Ejecutar DESPUÉS de sp_maestro_aplicar
-- (los productos del servidor ya son los de la lista).
-- Varios productos repetidos de la sucursal pueden quedar vinculados al mismo producto del servidor.
-- Las equivalencias cargadas a mano (criterio = 'MANUAL') no se pisan.
-- Devuelve los productos de la sucursal que quedaron sin vincular.
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_equivalencias_sucursal
    @suc int
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.ProductosMaestro WHERE id_producto IS NOT NULL)
    BEGIN
        RAISERROR(N'La lista maestra no está aplicada (ejecutar sp_maestro_aplicar antes).', 16, 1);
        RETURN 1;
    END

    EXEC dbo.sp_maestro_normalizar;

    MERGE dbo.ProductosSucursal AS t
    USING (
        SELECT i.ID AS id_producto_sucursal, mm.id_producto, b.criterio, b.revisar
        FROM dbo.ProductosSucursalImport i
        CROSS APPLY (SELECT dbo.fn_texto(i.Descripcion_Codigo) AS d, dbo.fn_texto(i.Descripcion_Color) AS c, dbo.fn_talle(i.Talles) AS t) n
        CROSS APPLY dbo.fn_buscar_maestro(i.Ean_2025, i.Cod_Art, i.Codigo_Color, n.d, n.c, n.t) b
        INNER JOIN dbo.ProductosMaestro mm ON mm.fila = b.fila
        WHERE i.id_sucursal = @suc AND mm.id_producto IS NOT NULL
    ) AS m
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
    RETURN 0;
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
-- sp_sucursal_convertida
-- Ejecutar en el servidor DESPUÉS de convertir la base de la sucursal (08_convertir_sucursal.sql):
-- desde ese momento la sucursal manda los IDs del servidor, así que se borran sus equivalencias
-- (si no, la API volvería a traducir IDs que ya son del servidor).
-- No hace nada si quedan ventas de la sucursal sin traducir.
------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_sucursal_convertida
    @suc int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.sp_traducir_ventas_sucursal @suc = @suc;

    IF EXISTS (SELECT 1 FROM dbo.detalle_ventas WHERE id_sucursal = @suc AND id_producto_sucursal IS NULL)
       AND EXISTS (SELECT 1 FROM dbo.ProductosSucursalImport WHERE id_sucursal = @suc)
    BEGIN
        SELECT 'Líneas sin traducir' AS resultado, dv.id_producto, COUNT(*) AS lineas
        FROM dbo.detalle_ventas dv
        WHERE dv.id_sucursal = @suc AND dv.id_producto_sucursal IS NULL
        GROUP BY dv.id_producto;
        RAISERROR(N'Quedan ventas de la sucursal sin traducir (ver resultado). Vincular esos productos (03/06) antes.', 16, 1);
        RETURN 1;
    END

    BEGIN TRAN;
    DELETE FROM dbo.ProductosSucursal WHERE id_sucursal = @suc;
    DELETE FROM dbo.ProductosSucursalImport WHERE id_sucursal = @suc;
    DELETE FROM dbo.FormasPagoSucursal WHERE id_sucursal = @suc;
    COMMIT;

    PRINT 'Sucursal ' + CAST(@suc AS varchar(10)) + ': ahora usa los IDs del servidor.';
    RETURN 0;
END
GO

-- sp_proponer_unificacion quedó reemplazado por sp_maestro_preparar
IF OBJECT_ID('dbo.sp_proponer_unificacion', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_proponer_unificacion;
GO

SET NOEXEC OFF;
GO
PRINT 'Estructura de unificación creada / actualizada.';
GO
