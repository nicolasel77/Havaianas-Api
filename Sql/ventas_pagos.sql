USE [dbHavaianas_servidor]
GO

-- Pago combinado: partes de una venta cobrada con varias formas de pago.
-- Solo tiene filas para las ventas combinadas; el resto se pagó entero con ventas.forma_pago.
-- La Api la crea sola si tiene permiso; este script es por si no lo tiene.
IF OBJECT_ID('dbo.ventas_pagos') IS NULL
CREATE TABLE dbo.ventas_pagos (
    id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    id_sucursal int NOT NULL,
    id_venta_sucursal int NOT NULL,
    forma_pago int NOT NULL,            -- ID del servidor
    forma_pago_sucursal int NULL,       -- ID que mandó la sucursal (NULL si no se pudo traducir)
    importe money NOT NULL
)
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ventas_pagos_venta' AND object_id = OBJECT_ID('dbo.ventas_pagos'))
CREATE INDEX IX_ventas_pagos_venta ON dbo.ventas_pagos (id_sucursal, id_venta_sucursal)
GO
