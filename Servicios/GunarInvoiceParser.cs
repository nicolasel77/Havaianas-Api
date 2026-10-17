// GunarInvoiceParser.cs
//
// Parser de facturas PDF de Calzados Gunar S.A. (proveedor Havaianas) para el
// proyecto Havaianas WebApp de Nicolas.
//
// Lógica validada previamente contra la factura real 0023-00029160 usando un
// prototipo equivalente en Python/PyMuPDF: las 161 líneas de producto y los
// totales (540 cv, 6.480 un, subtotal $92.074.852,43) coincidieron exactamente
// con lo declarado en el PDF.
//
// Paquete NuGet requerido:
//   dotnet add package PdfPig
//
// Idea central: no parsear el texto plano de la página (rompe con ciertas
// tipografías por el espaciado entre glifos). En cambio, se extraen las
// PALABRAS con sus coordenadas X/Y y se reconstruyen "líneas lógicas"
// agrupando por posición vertical (Y) y ordenando por X. Sobre ese stream de
// líneas, cada producto sigue un patrón fijo de 9 tokens consecutivos:
//
//   Codigo | "Modelo - Nombre" | Cantidad | "cv" | UnidadesPorCv |
//   Precio | Bonif% | Importe | "Marca / Material / Color / TalleDesde - TalleHasta"
//
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Havaianas.Facturacion.Parsing
{
    public class ProductoFactura
    {
        public string CodigoProductoProveedor { get; set; } = "";
        public string CodigoModelo { get; set; } = "";
        public string? NombreModelo { get; set; }
        public string? Marca { get; set; }
        public string? Material { get; set; }
        public string? Color { get; set; }
        public string? TalleDesde { get; set; }
        public string? TalleHasta { get; set; }
        public int CantidadCv { get; set; }
        public int UnidadesPorCv { get; set; }
        public int CantidadUnidades { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal BonificacionPct { get; set; }
        public decimal Importe { get; set; }
    }

    public class HeaderFactura
    {
        public string? Numero { get; set; }
        public string? Fecha { get; set; }
        public string? ClienteRazonSocial { get; set; }
        public string? CuitCliente { get; set; }
        public string? Sucursal { get; set; }
        public string? CondicionPago { get; set; }
        public string? Remito { get; set; }
    }

    public class TotalesFactura
    {
        public int TotalCv { get; set; }
        public int TotalUnidades { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Iva { get; set; }
        public decimal PercepcionIibb { get; set; }
        public decimal TotalFactura { get; set; }
    }

    public class FacturaParseada
    {
        public HeaderFactura Header { get; set; } = new();
        public TotalesFactura Totales { get; set; } = new();
        public List<ProductoFactura> Productos { get; set; } = new();

        /// <summary>
        /// Chequeo de integridad: compara lo sumado línea a línea contra los
        /// totales declarados en el pie de la factura. Usalo antes de dar por
        /// buena la extracción y disparar el ingreso de stock.
        /// </summary>
        public (bool Ok, string Detalle) Validar()
        {
            var sumaCv = Productos.Sum(p => p.CantidadCv);
            var sumaUnidades = Productos.Sum(p => p.CantidadUnidades);
            var sumaImportes = Productos.Sum(p => p.Importe);

            var ok = sumaCv == Totales.TotalCv
                     && sumaUnidades == Totales.TotalUnidades
                     && Math.Abs(sumaImportes - Totales.Subtotal) < 0.05m;

            var detalle = $"CV: {sumaCv}/{Totales.TotalCv} | " +
                          $"Unidades: {sumaUnidades}/{Totales.TotalUnidades} | " +
                          $"Subtotal: {sumaImportes:N2}/{Totales.Subtotal:N2}";
            return (ok, detalle);
        }
    }

    public static class GunarInvoiceParser
    {
        private static readonly CultureInfo Ar = new("es-AR");

        private static readonly Regex CodeRe = new(@"^\d{10,13}$", RegexOptions.Compiled);
        private static readonly Regex QtyRe = new(@"^\d+$", RegexOptions.Compiled);
        private static readonly Regex PctRe = new(@"^\d+(?:[.,]\d+)?%$", RegexOptions.Compiled);
        // Precio viene con 4 decimales (13.683,6800), Importe con 2 (1.149.429,12)
        private static readonly Regex MoneyRe = new(@"^[\d.]+,\d{2,4}$", RegexOptions.Compiled);
        private static readonly Regex DetailRe =
            new(@"^(?<marca>.+?)\s*/\s*(?<material>.+?)\s*/\s*(?<color>.+?)\s*/\s*(?<desde>[^-]+?)\s*-\s*(?<hasta>\d{1,3}(?:\s*/\s*\d{1,3})?)$",
                RegexOptions.Compiled);
        private static readonly Regex ModeloRe = new(@"^(?<codigo>.+?)\s*-\s*(?<nombre>.+)$", RegexOptions.Compiled);

        private static readonly HashSet<string> HeaderTokens = new()
        {
            "Producto", "Descripción", "Cant.", "Un.x Cv.", "Precio", "Bonif.", "Importe"
        };

        public static FacturaParseada Parse(string pdfPath)
        {
            using var document = PdfDocument.Open(pdfPath);
            var allLines = new List<string>();

            foreach (var page in document.GetPages())
            {
                allLines.AddRange(GetOrderedLines(page));
            }

            var header = ParseHeader(allLines);
            var productos = ParseProductos(allLines);
            var totales = ParseTotales(allLines);

            return new FacturaParseada
            {
                Header = header,
                Totales = totales,
                Productos = productos
            };
        }

        /// <summary>
        /// Reconstruye "líneas lógicas" a partir de las palabras del PDF, agrupando
        /// por coordenada Y (misma línea visual) y ordenando por X (izquierda a derecha).
        /// Esto reemplaza el uso de page.Text plano, que puede venir mal espaciado
        /// según la fuente incrustada en el PDF.
        /// </summary>
        private static List<string> GetOrderedLines(Page page)
        {
            var words = page.GetWords().ToList();

            // Agrupamos por Y redondeado (tolerancia para no separar palabras
            // de una misma fila por pequeñas diferencias de baseline).
            var grupos = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 1))
                .OrderByDescending(g => g.Key); // PdfPig: Y crece hacia arriba

            var palabrasOrdenadas = new List<string>();
            foreach (var g in grupos)
            {
                var palabras = g.OrderBy(w => w.BoundingBox.Left).ToList();
                var palabra = "";
                var derecha = 0d;

                foreach (var word in palabras)
                {
                    var separacion = word.BoundingBox.Left - derecha;
                    if (palabra.Length > 0 && separacion > 0.2)
                    {
                        palabrasOrdenadas.Add(palabra);
                        palabra = "";
                    }

                    palabra += word.Text;
                    derecha = word.BoundingBox.Right;
                }

                if (palabra.Length > 0)
                    palabrasOrdenadas.Add(palabra);
            }
            return palabrasOrdenadas;
        }

        private static HeaderFactura ParseHeader(List<string> tokens)
        {
            var header = new HeaderFactura();

            var texto = string.Join(" ", tokens);

            var numeroMatch = Regex.Match(texto,
                @"Factura(?:\s+[A-Z])?\s*:?[\s]*(?<numero>\d{3,5}\s*[-/]\s*\d{4,8})",
                RegexOptions.IgnoreCase);
            if (numeroMatch.Success)
            {
                header.Numero = Regex.Replace(numeroMatch.Groups["numero"].Value, @"\s*[-/]\s*", "-");
            }
            else
            {
                var numeroEnTexto = Regex.Match(texto, @"(?<!\d)(?<numero>\d{3,5}\s*[-/]\s*\d{4,8})(?!\d)");
                if (numeroEnTexto.Success)
                    header.Numero = Regex.Replace(numeroEnTexto.Groups["numero"].Value, @"\s*[-/]\s*", "-");

                for (var i = 1; i < tokens.Count - 1; i++)
                {
                    if (header.Numero != null) break;
                    if (tokens[i] != "-" || !tokens[i + 1].All(char.IsDigit)) continue;

                    var prefijo = new List<string>();
                    for (var j = i - 1; j >= 0 && tokens[j].All(char.IsDigit); j--)
                        prefijo.Insert(0, tokens[j]);

                    var numero = string.Concat(prefijo) + "-" + tokens[i + 1];
                    if (Regex.IsMatch(numero, @"^\d{3,5}-\d{4,8}$"))
                    {
                        header.Numero = numero;
                        break;
                    }
                }
            }

            header.Fecha = tokens.FirstOrDefault(t => Regex.IsMatch(t, @"^\d{2}/\d{2}/\d{4}$"));
            header.CuitCliente = FindAfter(tokens, "CUIT:");
            header.Sucursal = FindAfter(tokens, "Suc:");
            header.CondicionPago = FindAfter(tokens, "Condición de pago:");
            header.Remito = FindAfter(tokens, "Remito:");
            header.ClienteRazonSocial = tokens
                .FirstOrDefault(t => t.Contains('(') && t.Contains(')') && Regex.IsMatch(t, @"\(\d+\)"));

            return header;
        }

        private static string? FindAfter(List<string> tokens, string label)
        {
            var idx = tokens.IndexOf(label);
            return idx >= 0 && idx + 1 < tokens.Count ? tokens[idx + 1] : null;
        }

        private static List<ProductoFactura> ParseProductos(List<string> tokens)
        {
            var productos = new List<ProductoFactura>();
            var start = tokens.IndexOf("Importe");
            var i = start >= 0 ? start + 1 : 0;

            while (i < tokens.Count)
            {
                var t = tokens[i];

                if (t.StartsWith("Total:"))
                {
                    break; // fin de la tabla de productos
                }

                if (CodeRe.IsMatch(t))
                {
                    var cantidadIndex = -1;
                    for (var j = i + 1; j < Math.Min(i + 12, tokens.Count - 1); j++)
                    {
                        if (QtyRe.IsMatch(tokens[j]) && tokens[j + 1].Equals("cv", StringComparison.OrdinalIgnoreCase))
                        {
                            cantidadIndex = j;
                            break;
                        }
                    }

                    if (cantidadIndex < 0 || cantidadIndex + 5 >= tokens.Count)
                    {
                        i++;
                        continue;
                    }

                    var descripcion = string.Join(" ", tokens.Skip(i + 1).Take(cantidadIndex - i - 1));
                    var cantidad = tokens[cantidadIndex];
                    var unidadMedida = tokens[cantidadIndex + 1];
                    var unPorCv = tokens[cantidadIndex + 2];
                    var precio = tokens[cantidadIndex + 3];
                    var bonif = tokens[cantidadIndex + 4];
                    var importe = tokens[cantidadIndex + 5];
                    var detalleFin = cantidadIndex + 6;
                    while (detalleFin < tokens.Count && !CodeRe.IsMatch(tokens[detalleFin]) &&
                           !tokens[detalleFin].StartsWith("Total:") &&
                           !tokens[detalleFin].StartsWith("*****TRANSPORTE") &&
                           !tokens[detalleFin].Equals("Factura", StringComparison.OrdinalIgnoreCase))
                        detalleFin++;
                    var detalle = string.Join(" ", tokens.Skip(cantidadIndex + 6).Take(detalleFin - cantidadIndex - 6));
                    var transporteIndex = detalle.IndexOf("*****TRANSPORTE", StringComparison.OrdinalIgnoreCase);
                    if (transporteIndex >= 0)
                        detalle = detalle[..transporteIndex].Trim();

                    bool esFilaValida = QtyRe.IsMatch(cantidad)
                                         && unidadMedida == "cv"
                                         && QtyRe.IsMatch(unPorCv)
                                         && MoneyRe.IsMatch(precio)
                                         && PctRe.IsMatch(bonif)
                                         && MoneyRe.IsMatch(importe);

                    if (!esFilaValida)
                    {
                        i++;
                        continue;
                    }

                    var producto = new ProductoFactura
                    {
                        CodigoProductoProveedor = t,
                        CantidadCv = int.Parse(cantidad),
                        UnidadesPorCv = int.Parse(unPorCv),
                        PrecioUnitario = ParseDecimalAr(precio),
                        BonificacionPct = ParseDecimalAr(bonif.TrimEnd('%')),
                        Importe = ParseDecimalAr(importe),
                    };
                    producto.CantidadUnidades = producto.CantidadCv * producto.UnidadesPorCv;

                    var modeloMatch = ModeloRe.Match(descripcion);
                    if (modeloMatch.Success)
                    {
                        producto.CodigoModelo = modeloMatch.Groups["codigo"].Value.Trim();
                        producto.NombreModelo = modeloMatch.Groups["nombre"].Value.Trim();
                    }
                    else
                    {
                        producto.CodigoModelo = descripcion;
                    }

                    var detalleMatch = DetailRe.Match(detalle);
                    if (detalleMatch.Success)
                    {
                        producto.Marca = detalleMatch.Groups["marca"].Value.Trim();
                        producto.Material = detalleMatch.Groups["material"].Value.Trim();
                        producto.Color = detalleMatch.Groups["color"].Value.Trim();
                        producto.TalleDesde = detalleMatch.Groups["desde"].Value.Trim();
                        producto.TalleHasta = detalleMatch.Groups["hasta"].Value.Trim();
                    }

                    productos.Add(producto);
                    i = detalleFin;
                    continue;
                }

                i++;
            }

            return productos;
        }

        private static TotalesFactura ParseTotales(List<string> tokens)
        {
            var totales = new TotalesFactura();
            var texto = string.Join(" ", tokens);

            var totalMatch = Regex.Match(texto, @"Total:\s*(?<cv>[\d.]+)\s*cv\s+(?<unidades>[\d.]+)\s*un", RegexOptions.IgnoreCase);
            if (totalMatch.Success)
            {
                totales.TotalCv = int.Parse(totalMatch.Groups["cv"].Value.Replace(".", ""), Ar);
                totales.TotalUnidades = int.Parse(totalMatch.Groups["unidades"].Value.Replace(".", ""), Ar);
            }

            totales.Subtotal = ParseMoneyAfter(texto, @"Subtotal\s+IVA\s*\(21,00%\)");
            totales.Iva = ParseMoneyAfter(texto, @"(?<!Subtotal\s)IVA\s*\(21,00%\)");
            totales.PercepcionIibb = ParseMoneyAfter(texto, @"Perc\.\s*IIBB");
            totales.TotalFactura = ParseMoneyAfter(texto, @"(?<!Subtotal\s)Total(?!:)");

            return totales;
        }

        private static decimal ParseMoneyAfter(string texto, string etiqueta)
        {
            var match = Regex.Match(texto, etiqueta + @"\s*:?\s*\$?\s*(?<importe>[\d.]+,[\d]{2,4})", RegexOptions.IgnoreCase);
            return match.Success ? ParseDecimalAr(match.Groups["importe"].Value) : 0m;
        }

        private static int ParseIntFromPrefixed(string s)
        {
            // "540 cv" -> 540 ; "6.480 un" -> 6480
            var numero = s.Split(' ')[0].Replace(".", "");
            return int.Parse(numero, Ar);
        }

        /// <summary>Convierte '13.683,6800' (formato AR: punto=miles, coma=decimal) a decimal.</summary>
        private static decimal ParseDecimalAr(string s)
        {
            var normalizado = s.Replace(".", "").Replace(",", ".");
            return decimal.Parse(normalizado, CultureInfo.InvariantCulture);
        }
    }
}

// ---------------------------------------------------------------------------
// Ejemplo de uso en un endpoint de tu Web API (ASP.NET Core):
//
// [HttpPost("facturas/importar")]
// public IActionResult ImportarFactura(IFormFile pdf)
// {
//     var tempPath = Path.GetTempFileName();
//     using (var stream = System.IO.File.Create(tempPath))
//         pdf.CopyTo(stream);
//
//     var factura = GunarInvoiceParser.Parse(tempPath);
//     var (ok, detalle) = factura.Validar();
//
//     if (!ok)
//     {
//         // Devolvé la factura igual, pero marcá "requiere revisión manual"
//         // -- nunca cargues el ingreso de stock directo si la validación falla.
//         return Ok(new { factura, validacion = new { ok, detalle } });
//     }
//
//     // A partir de acá: mapear factura.Productos contra tu tabla
//     // MapeoProductoProveedor (CodigoProveedor + Color + Talle -> SKU interno)
//     // y mostrar pantalla de previsualización antes de confirmar el ingreso.
//
//     return Ok(new { factura, validacion = new { ok, detalle } });
// }
// ---------------------------------------------------------------------------
