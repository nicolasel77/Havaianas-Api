# Unificación de productos y formas de pago

## Por qué había productos repetidos y ventas/stock que no cerraban

1. **Cada sucursal manda sus ventas con el ID de producto de su propia base.** `POST /Ventas` guardaba ese ID tal
   cual en `detalle_ventas`, sin traducirlo al producto del servidor. Si en la sucursal el producto tenía otro ID,
   la venta quedaba asignada a otro producto o a uno inexistente.
2. **Ventas duplicadas.** Si una sucursal reenviaba una venta, la API actualizaba la cabecera pero volvía a
   insertar todas las líneas de `detalle_ventas`.
3. **Altas sin control.** `POST /Productos` no revisaba si ya existía el EAN o la combinación Cod_Art + color + talle,
   y armaba el ID nuevo con `MAX(Orden) + 1` en vez de `MAX(ID) + 1`.

## Qué cambia

- **El servidor es el dueño de productos y formas de pago.** Se crean y editan solo en el servidor (WebApp / API).
  Las sucursales los descargan con `GET /Productos` y `GET /FormasPago`.
- La API **rechaza duplicados** con `409 Conflict` y devuelve el producto que ya existe. Los criterios son:
  - mismo EAN
  - mismo Cod_Art + color + talle
  - misma descripción + color + talle
- **Equivalencias por sucursal.** `ProductosSucursal` y `FormasPagoSucursal` guardan qué ID de la sucursal
  corresponde a cada ID del servidor, y `POST /Ventas` traduce con ellas. El ID original queda guardado en
  `detalle_ventas.id_producto_sucursal` y `ventas.forma_pago_sucursal`.
- **Reenvíos.** Si una venta llega de nuevo, su detalle se reemplaza en vez de duplicarse. Para eso cada venta
  (sucursal + `id_venta_sucursal`) tiene que llegar completa en el mismo envío.
- **Bajas lógicas.** Los productos duplicados no se borran: quedan con `Activo = 0` e `ID_Unificado` = el producto
  que queda. `DELETE /Productos/{id}` también hace una baja lógica.
- **Endpoints nuevos:**
  - `PUT /Productos/{id}`
  - `DELETE /Productos/{id}`
  - `POST /Productos/Equivalencias/{suc}`
  - `GET`, `POST` y `PUT /FormasPago`
  - `POST /FormasPago/Equivalencias/{suc}`

## Cómo se elige qué producto queda

Se toman como el mismo producto los que comparten alguno de estos datos (siempre con el mismo talle):

- EAN y color
- Cod_Art y color, sin EANs distintos entre ellos
- descripción y color, sin EANs ni Cod_Art distintos entre ellos

De cada grupo queda uno, en este orden de prioridad:

1. descripción en **MAYÚSCULAS** y con todos los datos (talle, color y EAN)
2. con todos los datos
3. en MAYÚSCULAS
4. con EAN, con Cod_Art, con Código Equis o, si empatan, el de menor ID

Los grupos con datos contradictorios (dos EAN, dos Cod_Art o dos colores distintos) no se unifican solos: quedan
listados para revisar a mano.

En las sucursales, varios productos repetidos se vinculan con el mismo producto del servidor (`ProductosSucursal`),
así que sus ventas quedan juntas en el servidor.

## Pasos (en este orden)

| # | Qué | Cambia datos |
|---|-----|--------------|
| 1 | Backup completo de `dbHavaianas_servidor`. | No |
| 2 | Ejecutar `01_estructura.sql`. Si falta alguna tabla o columna esperada, el script lo avisa y no sigue. | Solo agrega columnas y tablas |
| 3 | Publicar la nueva API y ejecutar `sp_stock.sql` (repo WebApp). **Tiene que ser después del paso 2**, porque la API nueva usa las columnas nuevas. | No |
| 4 | Ejecutar `02_diagnostico.sql` y revisar los resultados. | No |
| 5 | `03_sucursales.sql` para **cada** sucursal: cargar su catálogo, vincular, revisar, cargar las formas de pago y traducir sus ventas. | Ventas de esa sucursal (pasan a IDs del servidor) |
| 6 | `04_unificar.sql`: backup, propuesta, revisión y aplicación. Opcional: borrar las líneas de venta repetidas. | Sí, en una transacción |
| 7 | `05_restricciones.sql`: índices únicos que impiden volver a cargar duplicados. | Solo recorta espacios en talles |
| 8 | En el programa de las sucursales: descargar productos y formas de pago del servidor, no crearlos localmente, y mandar el catálogo propio a `POST /Productos/Equivalencias/{suc}` mientras tenga IDs propios. | — |

El paso 5 tiene que hacerse **antes** del 6. Las ventas de una sucursal tienen IDs de la sucursal, y unificar sin
traducirlas primero mezclaría productos.

## Supuestos a verificar

- Nombres de tablas y columnas: `Productos`, `detalle_ventas` (`id`, `id_venta`, `id_producto`, `id_sucursal`),
  `ventas` (`forma_pago`, `id_sucursal`), `formas_pago` (`ID`, `Descripcion`). `01_estructura.sql` verifica la mayoría.
- Tablas que referencian productos: el resultado 11 de `02_diagnostico.sql` lista las columnas que podrían faltar en
  `sp_aplicar_unificacion`.
- SQL Server 2016 SP1 o superior (usa `CREATE OR ALTER`).
