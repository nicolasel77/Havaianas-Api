# Unificación de productos y formas de pago

## Por qué había productos repetidos y ventas/stock que no cerraban

1. **Cada sucursal manda sus ventas con el ID de producto de su propia base.** `POST /Ventas` guardaba ese ID tal
   cual en `detalle_ventas`, sin traducirlo al producto del servidor. Además, cada sucursal tiene sus propios
   productos repetidos.
2. **Ventas duplicadas.** Si una sucursal reenviaba una venta, la API actualizaba la cabecera pero volvía a
   insertar todas las líneas de `detalle_ventas`.
3. **Altas sin control.** `POST /Productos` no revisaba si el producto ya existía, y armaba el ID nuevo con
   `MAX(Orden) + 1` en vez de `MAX(ID) + 1`.

## La solución

**La lista maestra (`productos.xlsx`) es la verdad.** El servidor y todas las sucursales quedan con esa lista, con los
mismos IDs, y las ventas, traslados e ingresos pasan a esos productos.

- **Productos del servidor.** Cada fila de la lista queda como un solo producto. Se conserva el ID del mejor producto
  existente que le corresponde; si no hay ninguno, se crea con un ID nuevo. El orden de preferencia es:
  1. descripción en MAYÚSCULAS y datos completos (talle, color y EAN)
  2. datos completos
  3. en MAYÚSCULAS
  4. con EAN o Cod_Art; si empatan, el de menor ID
- **Duplicados.** Los demás productos que corresponden a la misma fila se unifican en ese: sus ventas, ingresos,
  traslados, ajustes y mapeos de proveedor pasan a ese producto, y el duplicado queda inactivo (no se borra).
- **Fuera de la lista.** Los productos que no están en la lista se dan de baja (`Activo = 0`).
- **Sucursales.** Cada producto de la sucursal, con sus repetidos, se vincula con el de la lista. Primero se traducen
  sus ventas en el servidor. Después se convierte la base de la sucursal: sus movimientos pasan a los IDs del
  servidor y su tabla de productos se reemplaza por la del servidor.

### Cómo se reconoce un producto en la lista (`fn_buscar_maestro`)

Gana el primer criterio que encuentra coincidencia:

1. EAN
2. Cod_Art + código de color + descripción de color + talle
3. Cod_Art + código de color + talle, solo si en la lista hay una sola fila así
4. descripción + descripción de color + talle

En los criterios 2, 3 y 4 no se vincula si los dos tienen EAN y es distinto. Los vínculos por descripción, y los de
EAN cuando no coincide el talle o el color, quedan marcados para revisar.

Los talles escritos distinto (por ejemplo `39/40` contra `390`) se traducen con la tabla `TallesEquivalencia`.

### Lo que ya se ve en la lista

La lista tiene 2.909 filas y no repite ningún EAN, pero:

- **Filas repetidas.** Hay 5 iguales: KIDS SLIM GLITTER II PERSIGO en talles 256, 278, 290 y 312, y TOP MARVEL
  CLASSIC NAVY BLUE en 456. Se crea una sola de cada una: la que tiene EAN.
- **Códigos de color repetidos con distinto color.** El 4146976 / 9321 es BEIGE / STRAW y también ROSA GLOW. El
  4147955 / 4896 es GREEN OLIVE y también TROPICAL CORAL. Se tratan como productos distintos por la descripción
  del color, pero conviene corregir el código en la lista.
- **Hey Dude y accesorios.** Vienen sin Cod_Art (0), así que se reconocen por EAN o por descripción.

## Cambios en la API (para que no vuelva a pasar)

- **El servidor es el dueño de productos y formas de pago.** Las sucursales los descargan con `GET /Productos` y
  `GET /FormasPago`.
- **Altas y ediciones.** `POST` y `PUT /Productos` rechazan duplicados con `409` y devuelven el producto que ya
  existe. `DELETE` hace una baja lógica.
- **Ventas.** `POST /Ventas` traduce el producto y la forma de pago de las sucursales que todavía no se convirtieron,
  y reemplaza el detalle de una venta reenviada en vez de duplicarlo.

## Pasos (en este orden)

| # | Dónde | Qué |
|---|-------|-----|
| 1 | Servidor | Backup completo. |
| 2 | Servidor | `01_estructura.sql`: agrega tablas, columnas y procedimientos. No cambia datos. |
| 3 | Servidor | Publicar la nueva API y ejecutar `sp_stock.sql` (repo WebApp). Tiene que ser después del paso 2. |
| 4 | Servidor | `02_diagnostico.sql`: solo lectura. |
| 5 | Servidor | `03_catalogos_sucursales.sql` para **todas** las sucursales: copia la tabla de productos de cada una. |
| 6 | Servidor | `04_productos_maestro.sql`: carga la lista maestra. Se genera con `herramientas/lista_maestra_a_sql.py`. |
| 7 | Servidor | `05_aplicar_maestro.sql`: backup, `sp_maestro_preparar`, revisión, `sp_maestro_aplicar`. |
| 8 | Servidor | `06_sucursales.sql` por cada sucursal: vincular, cargar formas de pago y traducir sus ventas. |
| 9 | Sucursal | `08_convertir_sucursal.sql` en la base de cada sucursal, sin vender y con todo enviado. Al terminar, en el servidor: `EXEC sp_sucursal_convertida @suc = N`. |
| 10 | Servidor | `07_restricciones.sql`: índices únicos para que no se puedan volver a cargar duplicados. |
| 11 | Programa de sucursal | Descargar productos y formas de pago del servidor y no crearlos localmente. |

**Primero probar todo en una copia** (por ejemplo, el backup restaurado con otro nombre: cambiar el `USE` de cada
script).

## Supuestos a verificar en la prueba

- **Nombres de tablas y columnas** (`01_estructura.sql` verifica la mayoría):
  - `Productos`
  - `detalle_ventas` (`id`, `id_venta`, `id_producto`, `id_sucursal`)
  - `ventas` (`forma_pago`, `id_sucursal`)
  - `formas_pago` (`ID`, `Descripcion`)
- **Base de las sucursales:** se asume que tiene la misma estructura que la del servidor. `08_convertir_sucursal.sql`
  lista las columnas de producto que no conoce.
- **Tablas con clave por producto** (por ejemplo, una tabla de stock por sucursal y producto): si dos productos
  repetidos de la sucursal van al mismo producto, la conversión puede fallar por clave duplicada. En ese caso se
  deshace todo y hay que sumar esas filas antes.
- **Versión de SQL Server:** 2016 SP1 o superior (usa `CREATE OR ALTER`).
