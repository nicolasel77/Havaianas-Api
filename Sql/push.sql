USE [dbHavaianas_servidor]
GO

-- Navegadores que activaron los avisos en el Inicio de la WebApp (PushSubscription del navegador)
IF OBJECT_ID('dbo.push_suscripciones') IS NULL
CREATE TABLE dbo.push_suscripciones (
    id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    endpoint varchar(800) NOT NULL,
    p256dh varchar(200) NOT NULL,
    auth varchar(100) NOT NULL,
    fecha_alta datetime NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_push_suscripciones_endpoint UNIQUE (endpoint)
)
GO

-- Agotados ya avisados: un aviso por producto y sucursal por día
IF OBJECT_ID('dbo.push_avisos_agotado') IS NULL
CREATE TABLE dbo.push_avisos_agotado (
    id_sucursal int NOT NULL,
    id_producto int NOT NULL,
    fecha date NOT NULL,
    fecha_aviso datetime NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_push_avisos_agotado PRIMARY KEY (id_sucursal, id_producto, fecha)
)
GO
