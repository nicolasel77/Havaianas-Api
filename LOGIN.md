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

## Puesta en marcha en el servidor

1. **Base**: correr `Sql/usuarios.sql` en `dbHavaianas_servidor` (se puede correr más de una vez).
2. **Claves**: generar una clave por cada cosa con
   `dotnet "Havaianas Api.dll" generar-clave`
   y cargarlas como variables de entorno del sitio/servicio de la Api (o en `appsettings.Production.json`,
   que está en `.gitignore`):
   - `Jwt__Clave` = una clave
   - `ClavesSucursales__<idSucursal>` = una clave distinta por sucursal (ej. `ClavesSucursales__3`)
3. **Usuarios**: `dotnet "Havaianas Api.dll" crear-usuario <usuario> "<nombre>" [idSucursal]` (pide la contraseña).
   Si alguien se olvida la contraseña: `dotnet "Havaianas Api.dll" resetear-password <usuario>`.
   Para dar de baja: `UPDATE Usuarios SET Activo = 0 WHERE Usuario = '...'`.
4. **Orden para no cortar la sincronización**: primero instalar en cada local la versión de Escritorio que
   manda la clave (clave `api_clave` en su App.config), después publicar esta Api. Si la Api se publica antes,
   los locales no pueden sincronizar hasta que se actualicen (no se pierde nada: al actualizar mandan lo pendiente).

Sin `Jwt:Clave` de al menos 32 caracteres la Api no arranca, a propósito.
