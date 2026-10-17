/*
    05_restricciones.sql
    --------------------
    Después de unificar: impide que se vuelvan a cargar productos duplicados en el servidor.
      * No puede haber dos productos activos con el mismo EAN.
      * No puede haber dos productos activos con el mismo Cod_Art + Codigo_Color + Talles.
    Si quedan duplicados (los "para revisar"), el índice no se crea y se listan los conflictos.
*/
USE [dbHavaianas_servidor]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Normalizar espacios en talles (los índices comparan el texto tal cual)
UPDATE dbo.Productos SET Talles = LTRIM(RTRIM(Talles)) WHERE Talles <> LTRIM(RTRIM(Talles));
GO

IF EXISTS (SELECT Ean_2025 FROM dbo.Productos WHERE Activo = 1 AND Ean_2025 > 0 GROUP BY Ean_2025 HAVING COUNT(*) > 1)
BEGIN
    PRINT 'No se crea UX_Productos_Ean: hay EAN repetidos entre productos activos.';
    SELECT 'EAN repetidos' AS conflicto, p.*
    FROM dbo.Productos p
    WHERE p.Activo = 1 AND p.Ean_2025 IN (SELECT Ean_2025 FROM dbo.Productos WHERE Activo = 1 AND Ean_2025 > 0 GROUP BY Ean_2025 HAVING COUNT(*) > 1)
    ORDER BY p.Ean_2025, p.ID;
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Productos_Ean' AND object_id = OBJECT_ID('dbo.Productos'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Productos_Ean ON dbo.Productos (Ean_2025) WHERE Activo = 1 AND Ean_2025 > 0;
GO

IF EXISTS (SELECT 1 FROM dbo.Productos WHERE Activo = 1 AND Cod_Art > 0 GROUP BY Cod_Art, Codigo_Color, Talles HAVING COUNT(*) > 1)
BEGIN
    PRINT 'No se crea UX_Productos_CodArt_Color_Talle: hay Cod_Art + color + talle repetidos entre productos activos.';
    SELECT 'Cod_Art + color + talle repetidos' AS conflicto, p.*
    FROM dbo.Productos p
    INNER JOIN (SELECT Cod_Art, Codigo_Color, Talles FROM dbo.Productos WHERE Activo = 1 AND Cod_Art > 0
                GROUP BY Cod_Art, Codigo_Color, Talles HAVING COUNT(*) > 1) d
        ON d.Cod_Art = p.Cod_Art AND d.Codigo_Color = p.Codigo_Color AND d.Talles = p.Talles
    WHERE p.Activo = 1
    ORDER BY p.Cod_Art, p.Codigo_Color, p.Talles, p.ID;
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Productos_CodArt_Color_Talle' AND object_id = OBJECT_ID('dbo.Productos'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Productos_CodArt_Color_Talle ON dbo.Productos (Cod_Art, Codigo_Color, Talles) WHERE Activo = 1 AND Cod_Art > 0;
GO
