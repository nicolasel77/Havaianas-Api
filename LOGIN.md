# Login de la Api

Todos los endpoints piden autenticación, salvo `POST /Auth/login`. Hay dos formas de entrar:

| Quién | Cómo | Dónde se configura |
|---|---|---|
| Personas (WebApp) | Usuario y contraseña en `POST /Auth/login` → token JWT en `Authorization: Bearer ...` | Tabla `Usuarios` |
| Havaianas Escritorio (cada sucursal) | Cabecera `X-Clave-Sucursal` con la clave de la sucursal | `ClavesSucursales` en el servidor de la Api |

## Qué protege

- Contraseñas guardadas con hash PBKDF2 (PasswordHasher de ASP.NET Core Identity), nunca en texto.
- Bloqueo por intentos: 5 contraseñas mal seguidas bloquean a ese usuario 15 minutos; 20 fallos desde una
  misma IP pública bloquean esa IP. Las IPs privadas (la red del local) y las de `Login:IpsSinLimite` solo
  tienen el límite por usuario, para que el servidor de la WebApp (que hace el login de todos) no bloquee a todos.
- El token dura `Jwt:HorasValidez` horas (10 por defecto).
- Cada usuario tiene un "sello" (`SelloSeguridad`). Cambiar la contraseña, resetearla o desactivar al usuario
  invalida sus tokens en menos de 1 minuto, aunque no hayan vencido.
- Las claves de sucursal se comparan en tiempo constante y tienen que tener al menos 32 caracteres.
- CORS solo para los orígenes de `Cors:Origenes` (https://lacosteleta.ar).

## Secretos: `appsettings.Secretos.json`

Ninguna clave va en el repo (este repo es público). Todas van en un archivo `appsettings.Secretos.json` que se crea
a mano **junto al `Havaianas Api.dll`** en el servidor (y en `bin\Debug\net6.0` o en la carpeta del proyecto para
probar en tu PC). Está en `.gitignore` y Visual Studio no lo publica, así que publicar no lo pisa.
Usar `appsettings.Secretos.ejemplo.json` como modelo:

- `ConnectionStrings:Havaianas`: la cadena de conexión al SQL. **Sin esto la Api no arranca.**
  (La variable de entorno `HAVAIANAS_CONNECTION`, si existe, tiene prioridad; sirve para apuntar a una base de prueba.)
- `Jwt:Clave`: clave para firmar los tokens (mínimo 32 caracteres). **Sin esto la Api no arranca.**
- `ClavesSucursales:<idSucursal>`: una clave distinta por sucursal.
- Las claves push (`Push:ClavePublica`, `Push:ClavePrivada`) también se pueden poner acá.

La clave del SQL estuvo escrita en `Datos/sql_db.cs` y quedó en el historial de GitHub: **hay que cambiarla**
en el SQL Server (`ALTER LOGIN [usuario] WITH PASSWORD = '...'`) y poner la nueva solo en este archivo.

## Puesta en marcha en el servidor

1. **Base**: correr `Sql/usuarios.sql` en `dbHavaianas_servidor` (se puede correr más de una vez).
2. **Claves**: generar una clave por cada cosa con
   `dotnet "Havaianas Api.dll" generar-clave`
   y cargarlas en `appsettings.Secretos.json` (ver arriba): `Jwt:Clave` y una `ClavesSucursales:<idSucursal>`
   distinta por sucursal (ej. `"3": "..."`).
3. **Usuarios**: `dotnet "Havaianas Api.dll" crear-usuario <usuario> "<nombre>" [idSucursal]` (pide la contraseña).
   Si alguien se olvida la contraseña: `dotnet "Havaianas Api.dll" resetear-password <usuario>`.
   Para dar de baja: `UPDATE Usuarios SET Activo = 0 WHERE Usuario = '...'`.
4. **Orden para no cortar la sincronización**: primero instalar en cada local la versión de Escritorio que
   manda la clave (clave `api_clave` en su `Havaianas Escritorio.exe.config`), después publicar esta Api. Si la Api se publica antes,
   los locales no pueden sincronizar hasta que se actualicen (no se pierde nada: al actualizar mandan lo pendiente).

Sin `Jwt:Clave` de al menos 32 caracteres la Api no arranca, a propósito.
