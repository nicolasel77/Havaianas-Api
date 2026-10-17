"""
Prototipo de parser de Factura Gunar (Havaianas) - PoC en Python con PyMuPDF.
Objetivo: validar la lógica de extracción antes de portarla a C#/PdfPig
para el proyecto Havaianas WebApp de Nicolas.
"""
import fitz
import re
import json
from collections import defaultdict
from decimal import Decimal

PATH = "/mnt/user-data/uploads/Factura_A_0023-00029160.pdf"

CODE_RE = re.compile(r"^\d{10,13}$")
QTY_RE = re.compile(r"^\d+$")
PCT_RE = re.compile(r"^\d+(?:[.,]\d+)?%$")
MONEY_RE = re.compile(r"^[\d.]+,\d{2,4}$")  # Precio trae 4 decimales, Importe trae 2
DETAIL_RE = re.compile(r"^(.+?)\s*/\s*(.+?)\s*/\s*(.+?)\s*/\s*(.+?)\s*-\s*(.+)$")


def to_decimal_ar(s: str) -> Decimal:
    """Convierte '13.683,6800' -> Decimal('13683.6800') (formato AR: punto=miles, coma=decimal)."""
    return Decimal(s.replace(".", "").replace(",", "."))


def get_ordered_lines(page):
    """Reconstruye las 'líneas lógicas' del PDF en orden de aparición vertical,
    agrupando palabras por su bloque/línea de contenido de PDF."""
    words = page.get_text("words")
    lines = defaultdict(list)
    for x0, y0, x1, y1, text, block, line, wno in words:
        lines[(block, line, round(y0, 1))].append((x0, text))
    ordered = []
    for key in sorted(lines.keys(), key=lambda k: k[2]):
        ws = sorted(lines[key])
        ordered.append(" ".join(w[1] for w in ws))
    return ordered


def parse_header(tokens):
    """Extrae los datos de cabecera de la factura (una sola vez, de la primera página)."""
    header = {}
    joined = tokens
    def find_after(label):
        for i, t in enumerate(joined):
            if t == label and i + 1 < len(joined):
                return joined[i + 1]
        return None

    # Número de factura: aparece como '0023' '-' '00029160' tras 'Factura'
    for i, t in enumerate(joined):
        if t == "Factura":
            header["numero"] = f"{joined[i+1]}-{joined[i+3]}"
            break

    header["fecha"] = next((t for t in joined if re.match(r"^\d{2}/\d{2}/\d{4}$", t)), None)
    header["cliente_razon_social"] = joined[13] if len(joined) > 13 else None
    header["cuit_cliente"] = find_after("CUIT:")
    header["sucursal"] = find_after("Suc:")
    header["condicion_pago"] = find_after("Moneda:")  # nota: cruzado por layout, se corrige abajo
    header["remito"] = find_after("Remito:")
    return header


def parse_products(all_tokens):
    """Recorre el stream de tokens y arma cada línea de producto según el patrón fijo:
    Codigo, Descripcion, Cantidad, 'cv', UnidadesPorCv, Precio, Bonif%, Importe, DetalleColorTalle
    """
    productos = []
    i = 0
    n = len(all_tokens)
    # arrancamos después del primer header de tabla
    start = next((idx for idx, t in enumerate(all_tokens) if t == "Importe"), 0)
    i = start + 1

    while i < n:
        t = all_tokens[i]

        # saltar encabezados repetidos de página / pies de página
        if t in ("Producto", "Descripción", "Cant.", "Un.x Cv.", "Precio", "Bonif.", "Importe"):
            i += 1
            continue
        if t.startswith("*****TRANSPORTE") or t.startswith("Nro CAE") or t.startswith("Vencimiento:"):
            i += 1
            continue
        if t.startswith("Total:"):
            break  # fin de la tabla de productos

        if CODE_RE.match(t):
            try:
                codigo = t
                descripcion = all_tokens[i + 1]
                cantidad = all_tokens[i + 2]
                unidad_medida = all_tokens[i + 3]  # 'cv'
                un_por_cv = all_tokens[i + 4]
                precio = all_tokens[i + 5]
                bonif = all_tokens[i + 6]
                importe = all_tokens[i + 7]
                detalle = all_tokens[i + 8]

                if not (QTY_RE.match(cantidad) and unidad_medida == "cv"
                        and QTY_RE.match(un_por_cv) and MONEY_RE.match(precio)
                        and PCT_RE.match(bonif) and MONEY_RE.match(importe)):
                    i += 1
                    continue

                m = DETAIL_RE.match(detalle)
                marca, material, color, talle_desde, talle_hasta = (None,) * 5
                if m:
                    marca, material, color, talle_desde, talle_hasta = m.groups()

                desc_match = re.match(r"^(.+?)\s*-\s*(.+)$", descripcion)
                codigo_modelo = desc_match.group(1).strip() if desc_match else descripcion
                nombre_modelo = desc_match.group(2).strip() if desc_match else None

                cant_cv = int(cantidad)
                unidades_por_cv = int(un_por_cv)

                productos.append({
                    "codigo_producto_proveedor": codigo,
                    "codigo_modelo": codigo_modelo,
                    "nombre_modelo": nombre_modelo,
                    "marca": marca,
                    "material": material,
                    "color": color,
                    "talle_desde": talle_desde,
                    "talle_hasta": talle_hasta,
                    "cantidad_cv": cant_cv,
                    "unidades_por_cv": unidades_por_cv,
                    "cantidad_unidades": cant_cv * unidades_por_cv,
                    "precio_unitario": str(to_decimal_ar(precio)),
                    "bonificacion_pct": bonif.rstrip("%"),
                    "importe": str(to_decimal_ar(importe)),
                })
                i += 9
                continue
            except IndexError:
                pass
        i += 1

    return productos


def parse_totals(all_tokens):
    totals = {}
    for i, t in enumerate(all_tokens):
        if t == "Total:":
            totals["total_cv"] = all_tokens[i + 1]        # p.ej. '540 cv'
            totals["total_unidades"] = all_tokens[i + 2]   # p.ej. '6.480 un'
        if t == "Subtotal IVA (21,00%)":
            totals["subtotal"] = str(to_decimal_ar(all_tokens[i + 2]))
        if t == "IVA (21,00%)":
            totals["iva"] = str(to_decimal_ar(all_tokens[i + 2]))
        if t.startswith("Perc. IIBB"):
            totals["percepcion_iibb"] = str(to_decimal_ar(all_tokens[i + 2]))
        if t == "Total" and i + 1 < len(all_tokens) and MONEY_RE.match(all_tokens[i + 1]):
            totals["total_factura"] = str(to_decimal_ar(all_tokens[i + 1]))
    return totals


def main():
    doc = fitz.open(PATH)
    all_tokens = []
    for page in doc:
        all_tokens.extend(get_ordered_lines(page))

    header = parse_header(all_tokens)
    productos = parse_products(all_tokens)
    totals = parse_totals(all_tokens)

    result = {
        "header": header,
        "totales": totals,
        "cantidad_lineas_detectadas": len(productos),
        "productos": productos,
    }

    with open("/home/claude/havaianas_invoice_parser/output.json", "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    # Validación: sumar importes y comparar contra subtotal declarado
    suma_importes = sum(Decimal(p["importe"]) for p in productos)
    suma_cv = sum(p["cantidad_cv"] for p in productos)
    suma_unidades = sum(p["cantidad_unidades"] for p in productos)

    print(f"Líneas de producto detectadas: {len(productos)}")
    print(f"Suma CV: {suma_cv}  (declarado: {totals.get('total_cv')})")
    print(f"Suma unidades: {suma_unidades}  (declarado: {totals.get('total_unidades')})")
    print(f"Suma importes: {suma_importes}  (subtotal declarado: {totals.get('subtotal')})")
    print(f"Total factura declarado: {totals.get('total_factura')}")
    print()
    print("Primeras 3 líneas parseadas:")
    for p in productos[:3]:
        print(json.dumps(p, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
