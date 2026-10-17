using Havaianas_Api.Modelos;
using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Havaianas_Api.Servicios
{
    public class FacturaPdfParser
    {
        private static readonly Regex FechaRegex = new Regex(
            @"(?:fecha|date)\s*[:\-]?\s*(?<fecha>\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LineaRegex = new Regex(
            @"^\s*(?<codigo>\d{3,10})\s+.+?\s+(?<cantidad>\d+(?:[.,]\d+)?)\s+(?<precio>\$?\s*[\d.]+(?:,\d{1,2})?)\s*$",
            RegexOptions.Compiled);

        public ResultadoLecturaFactura Extraer(Stream archivo)
        {
            ResultadoLecturaFactura resultado = new ResultadoLecturaFactura();
            List<string> paginas = new List<string>();

            using (PdfDocument documento = PdfDocument.Open(archivo))
            {
                foreach (var pagina in documento.GetPages())
                {
                    paginas.Add(pagina.Text);
                }
            }

            resultado.texto_extraido = string.Join(Environment.NewLine, paginas);
            if (string.IsNullOrWhiteSpace(resultado.texto_extraido))
            {
                resultado.errores.Add("El PDF no contiene texto seleccionable. El documento requiere OCR.");
                return resultado;
            }

            Match fecha = FechaRegex.Match(resultado.texto_extraido);
            if (!fecha.Success || !TryParseFecha(fecha.Groups["fecha"].Value, out DateTime fechaFactura))
            {
                resultado.errores.Add("No se encontró una fecha de factura reconocible.");
            }
            else
            {
                resultado.factura.fecha = fechaFactura;
            }

            foreach (string lineaTexto in resultado.texto_extraido.Split('\n'))
            {
                Match linea = LineaRegex.Match(lineaTexto.Replace('\r', ' ').Trim());
                if (!linea.Success)
                {
                    continue;
                }

                if (!TryParseNumero(linea.Groups["cantidad"].Value, out double cantidad) ||
                    !TryParseNumero(linea.Groups["precio"].Value, out double precio) ||
                    !int.TryParse(linea.Groups["codigo"].Value, out int codigo))
                {
                    continue;
                }

                resultado.factura.lineas.Add(new LineaFactura
                {
                    cod_art = codigo,
                    cantidad = cantidad,
                    precio = precio
                });
            }

            if (resultado.factura.lineas.Count == 0)
            {
                resultado.errores.Add("No se encontraron líneas con código, cantidad y precio reconocibles.");
            }

            return resultado;
        }

        private static bool TryParseFecha(string valor, out DateTime fecha)
        {
            string[] formatos = { "d/M/yyyy", "dd/MM/yyyy", "yyyy/M/d", "yyyy-MM-dd" };
            return DateTime.TryParseExact(valor.Replace('-', '/'), formatos,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);
        }

        private static bool TryParseNumero(string valor, out double numero)
        {
            string normalizado = valor.Replace("$", string.Empty).Replace(" ", string.Empty);
            if (normalizado.Contains(',') && normalizado.Contains('.'))
            {
                normalizado = normalizado.Replace(".", string.Empty).Replace(',', '.');
            }
            else if (normalizado.Contains(','))
            {
                normalizado = normalizado.Replace(',', '.');
            }

            return double.TryParse(normalizado, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out numero);
        }
    }

    public class ResultadoLecturaFactura
    {
        public ImportarFactura factura { get; set; } = new ImportarFactura();
        public string texto_extraido { get; set; } = string.Empty;
        public List<string> errores { get; set; } = new List<string>();
    }
}
