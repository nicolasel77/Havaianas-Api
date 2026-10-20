"""
Genera sql/unificacion/04_productos_maestro.sql a partir de la lista maestra de productos (Excel).

Uso:
    pip install openpyxl
    python herramientas/lista_maestra_a_sql.py productos.xlsx

Columnas esperadas (primera hoja, fila 1 = títulos):
    Cod Art | Descripción Código | Precio | Categoría | Genero | Cód Color | Descripción Color | Talle | Eans
"""
import os
import sys

import openpyxl

COLUMNAS = ['Cod Art', 'Descripción Código', 'Precio', 'Categoría', 'Genero', 'Cód Color', 'Descripción Color', 'Talle', 'Eans']
SALIDA = os.path.join(os.path.dirname(__file__), '..', 'sql', 'unificacion', '04_productos_maestro.sql')


def texto(valor):
    if valor is None:
        return 'NULL'
    if isinstance(valor, float) and valor.is_integer():
        valor = int(valor)
    t = str(valor).strip()
    return 'NULL' if t == '' else "N'" + t.replace("'", "''") + "'"


def numero(valor):
    if valor is None or str(valor).strip() == '':
        return 'NULL'
    v = float(valor)
    return str(int(v)) if v.is_integer() else str(v)


def main(archivo):
    hoja = openpyxl.load_workbook(archivo, data_only=True, read_only=True).worksheets[0]
    filas = hoja.iter_rows(values_only=True)
    titulos = [str(t).strip() if t is not None else '' for t in next(filas)]
    if titulos[:len(COLUMNAS)] != COLUMNAS:
        sys.exit(f'Columnas inesperadas: {titulos}\nSe esperaba: {COLUMNAS}')

    valores = []
    for nro, f in enumerate(filas, start=2):
        if all(v is None for v in f[:len(COLUMNAS)]):
            continue
        cod_art, desc, precio, cat, gen, cod_color, color, talle, ean = f[:len(COLUMNAS)]
        valores.append(f"({nro}, {numero(cod_art) if cod_art is not None else '0'}, {texto(desc)}, {numero(precio)}, "
                       f"{texto(cat)}, {texto(gen)}, {numero(cod_color) if cod_color is not None else '0'}, "
                       f"{texto(color)}, {texto(talle)}, {numero(ean)})")

    with open(SALIDA, 'w', encoding='utf-8') as s:
        s.write('/*\n    04_productos_maestro.sql\n    ------------------------\n'
                f'    GENERADO con herramientas/lista_maestra_a_sql.py desde {os.path.basename(archivo)} ({len(valores)} productos).\n'
                '    Carga la lista maestra en dbo.ProductosMaestro (fila = número de fila en el Excel).\n'
                '    Se puede volver a ejecutar: reemplaza la lista. Después ejecutar sp_maestro_preparar.\n*/\n')
        s.write('USE [dbHavaianas_servidor]\nGO\nSET NOCOUNT ON;\nSET XACT_ABORT ON;\nBEGIN TRAN;\n\n')
        s.write('DELETE FROM dbo.ProductosMaestro;\n\n')
        for i in range(0, len(valores), 500):
            s.write('INSERT INTO dbo.ProductosMaestro (fila, Cod_Art, Descripcion_Codigo, Precio, Categoria, Genero, '
                    'Codigo_Color, Descripcion_Color, Talles, Ean_2025) VALUES\n')
            s.write(',\n'.join(valores[i:i + 500]))
            s.write(';\n\n')
        s.write("COMMIT;\nSELECT COUNT(*) AS productos_cargados FROM dbo.ProductosMaestro;\nGO\n")
    print(f'{len(valores)} productos -> {os.path.normpath(SALIDA)}')


if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
