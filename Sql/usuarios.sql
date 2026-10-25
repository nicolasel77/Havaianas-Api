-- Tabla de usuarios para el login de la Api (JWT).
-- La contraseña se guarda hasheada (PasswordHasher de ASP.NET Core Identity, PBKDF2).
-- Para crear un usuario no insertes a mano: usá el comando de la Api
--   dotnet "Havaianas Api.dll" crear-usuario <usuario> <nombre> <idSucursal>
-- que pide la contraseña y guarda el hash.

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
