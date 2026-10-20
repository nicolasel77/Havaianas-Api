/*
    05_aplicar_maestro.sql
    ----------------------
    Deja los productos del servidor iguales a la lista maestra (04_productos_maestro.sql):
      * cada fila de la lista queda como UN producto activo (se conserva el ID del mejor producto
        existente: MAYÚSCULAS + datos completos; si no hay ninguno se crea con ID nuevo)
      * los demás productos que son el mismo se unifican en él: ventas, ingresos, traslados, ajustes,
        mapeos de proveedor y equivalencias de sucursales pasan al producto que queda
      * los productos que no están en la lista se dan de baja (Activo = 0, no se borran)

    Requisitos: 01_estructura.sql, 04_productos_maestro.sql y 03_catalogos_sucursales.sql para todas las sucursales.
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
-- Paso 2. (Opcional) Talles escritos distinto que en la lista. Ver el resultado 3 del paso 3
-- (productos fuera de la lista): si muchos no se vinculan por el talle, cargar acá la equivalencia
-- (en mayúsculas y sin espacios) y volver a ejecutar el paso 3.
------------------------------------------------------------------------------------------
/*
MERGE dbo.TallesEquivalencia AS t
USING (VALUES ('39/40', '390'), ('41/42', '412')) AS m(talle, talle_maestro)
    ON t.talle = m.talle
WHEN MATCHED THEN UPDATE SET talle_maestro = m.talle_maestro
WHEN NOT MATCHED THEN INSERT (talle, talle_maestro) VALUES (m.talle, m.talle_maestro);
*/

------------------------------------------------------------------------------------------
-- Paso 3. Preparar (NO cambia productos ni movimientos). Devuelve:
--   1) resumen   2) vínculos dudosos   3) productos fuera de la lista (con sus movimientos)
--   4) unificaciones propuestas   5) filas que se van a crear   6) filas repetidas en la lista
--   7) datos sospechosos en la lista
------------------------------------------------------------------------------------------
EXEC dbo.sp_maestro_preparar;
GO

------------------------------------------------------------------------------------------
-- Paso 4. Correcciones a mano (volver a ejecutar el paso 3 después):
--   * Un producto fuera de la lista (resultado 3) que es uno de la lista: unificarlo a mano
--       INSERT INTO dbo.ProductosUnificacion (id_viejo, id_nuevo, motivo) VALUES (123, 456, 'MANUAL');
--     (456 = id_producto de la fila de la lista, ver dbo.ProductosMaestro)
--   * Un vínculo dudoso (resultado 2) que está mal: corregir el dato del producto o de la lista.
--   * Si la fila de la lista todavía no tiene producto (se crea al aplicar), hacer la unificación manual
--     DESPUÉS del paso 5:
--       INSERT INTO dbo.ProductosUnificacion (id_viejo, id_nuevo, motivo) VALUES (123, 456, 'MANUAL');
--       EXEC dbo.sp_aplicar_unificacion @completar_datos = 0;
------------------------------------------------------------------------------------------

------------------------------------------------------------------------------------------
-- Paso 5. Aplicar. Todo en una transacción: si algo falla no se cambia nada.
--   @desactivar_fuera_de_lista = 0 deja activos los productos que no están en la lista.
------------------------------------------------------------------------------------------
/*
EXEC dbo.sp_maestro_aplicar @desactivar_fuera_de_lista = 1;
*/

------------------------------------------------------------------------------------------
-- Paso 6 (OPCIONAL). Borrar líneas de venta repetidas por reenvíos de las sucursales
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
