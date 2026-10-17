/*
    04_unificar.sql
    ---------------
    Unifica los productos duplicados del servidor.
    Requisitos: 01_estructura.sql ejecutado y 03_sucursales.sql hecho para TODAS las sucursales.

    Ejecutar por partes (seleccionar el bloque y F5), en orden.
*/
USE [dbHavaianas_servidor]
GO

------------------------------------------------------------------------------------------
-- Paso 1. BACKUP completo. Cambiar la ruta si hace falta (es una carpeta del servidor SQL).
------------------------------------------------------------------------------------------
DECLARE @archivo nvarchar(400) = N'C:\Backups\dbHavaianas_servidor_antes_unificar_' + FORMAT(GETDATE(), 'yyyyMMdd_HHmm') + N'.bak';
BACKUP DATABASE [dbHavaianas_servidor] TO DISK = @archivo WITH COPY_ONLY, INIT, STATS = 10;
GO

------------------------------------------------------------------------------------------
-- Paso 2. Generar la propuesta (no cambia productos ni movimientos).
-- Devuelve id_nuevo (el que queda) / id_viejo (el que se unifica en él) con sus datos.
------------------------------------------------------------------------------------------
EXEC dbo.sp_proponer_unificacion;
GO

------------------------------------------------------------------------------------------
-- Paso 3. Revisar la propuesta y los resultados 4 y 5 de 02_diagnostico.sql.
--   * Para sacar una fila de la propuesta:
--       DELETE FROM dbo.ProductosUnificacion WHERE id_viejo = 123 AND aplicado = 0;
--   * Para unificar a mano dos productos que la propuesta no detectó:
--       INSERT INTO dbo.ProductosUnificacion (id_viejo, id_nuevo, motivo) VALUES (123, 456, 'MANUAL');
--   * Para cambiar cuál queda: actualizar id_nuevo en todas las filas del grupo.
------------------------------------------------------------------------------------------

------------------------------------------------------------------------------------------
-- Paso 4. Aplicar. Todo en una transacción: si algo falla no se cambia nada.
------------------------------------------------------------------------------------------
/*
EXEC dbo.sp_aplicar_unificacion;
*/

------------------------------------------------------------------------------------------
-- Paso 5 (OPCIONAL). Borrar líneas de venta repetidas por reenvíos de las sucursales
-- (resultado 8 de 02_diagnostico.sql). Deja una sola línea por cada grupo idéntico
-- de venta + producto + cantidad + precio + descuento + subtotal.
-- Ojo: si en un mismo ticket se cargó dos veces el mismo producto en líneas separadas,
-- también se deja una sola. Revisar el resultado 8 antes.
------------------------------------------------------------------------------------------
/*
SET XACT_ABORT ON;
BEGIN TRAN;

;WITH Repetidas AS (
    SELECT ROW_NUMBER() OVER (PARTITION BY id_sucursal, id_venta, id_producto, cantidad, precio, descuento, subtotal
                              ORDER BY id) AS rn
    FROM dbo.detalle_ventas
)
DELETE FROM Repetidas WHERE rn > 1;

SELECT @@ROWCOUNT AS lineas_borradas;

COMMIT;   -- o ROLLBACK si el número no cierra
*/
GO
