-- Tabla de usuarios para el login de la Api (JWT). Correr en dbHavaianas_servidor.
-- La contraseña se guarda hasheada (PasswordHasher de ASP.NET Core Identity, PBKDF2).
-- Para crear un usuario no insertes a mano: usá el comando de la Api
--   dotnet "Havaianas Api.dll" crear-usuario <usuario> <nombre> [idSucursal]
-- que pide la contraseña y guarda el hash.
-- Se puede correr más de una vez: solo crea lo que falta.

IF OBJECT_ID('dbo.Usuarios', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Usuarios (
        [Id]            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Usuario]       NVARCHAR(50)  NOT NULL,
        [Nombre]        NVARCHAR(100) NOT NULL,
        [PasswordHash]  NVARCHAR(200) NOT NULL,
        [IdSucursal]    INT           NULL REFERENCES dbo.sucursales([ID]),
        [Activo]        BIT           NOT NULL DEFAULT 1,
        [FechaAlta]     DATETIME      NOT NULL DEFAULT GETDATE(),
        [UltimoLogin]   DATETIME      NULL,
        CONSTRAINT UQ_Usuarios_Usuario UNIQUE ([Usuario])
    );
END
GO

-- Sello de seguridad: cambia con cada cambio de contraseña y corta las sesiones abiertas.
-- Para cerrar a mano todas las sesiones de alguien:  UPDATE Usuarios SET SelloSeguridad = NEWID() WHERE Usuario = '...'
-- Para dar de baja a alguien:                         UPDATE Usuarios SET Activo = 0 WHERE Usuario = '...'
IF COL_LENGTH('dbo.Usuarios', 'SelloSeguridad') IS NULL
BEGIN
    ALTER TABLE dbo.Usuarios ADD [SelloSeguridad] UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_Usuarios_SelloSeguridad DEFAULT NEWID();
END
GO
