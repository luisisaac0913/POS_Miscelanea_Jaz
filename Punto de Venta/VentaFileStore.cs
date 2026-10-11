using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Punto_de_Venta
{
    public static class VentaFileStore
    {
        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiscelaneaJaz",
            "ventas.txt");

        public static List<Venta> Load()
        {
            if (!File.Exists(FilePath))
            {
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                using (File.Create(FilePath)) { }
                return [];
            }

            var sales = new List<Venta>();
            var lines = File.ReadAllLines(FilePath);
            var mxCulture = CultureInfo.GetCultureInfo("es-MX");

            for (var index = 0; index < lines.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(lines[index])) continue;

                var fields = lines[index].Split(new[] { "//" }, StringSplitOptions.None);
                if (fields.Length < 6) continue;

                if (!int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                {
                    continue;
                }

                // =========================================================================
                // RAMA 1: FORMATO MODERNO DE MISCELÁNEA JAZ (7 a 9 campos con estructura interna)
                // Estructura: 0:Id // 1:Productos(JSON/P1:) // 2:CantTotal // 3:PagoCon // 4:Cambio // 5:FechaISO // 6:Total // [7:Estado] // [8:Metodo]
                // =========================================================================
                if (fields.Length >= 7 && (fields[1].StartsWith("P1:", StringComparison.OrdinalIgnoreCase) || fields.Length >= 9 && IsIsoOrNumber(fields[2], fields[5])))
                {
                    var state = fields.Length >= 8 ? fields[7].Trim() : "Cerrada";
                    if (state is not ("Abierta" or "Cerrada" or "Cancelada" or "Devuelta" or "Fiado"))
                    {
                        state = "Cerrada";
                    }
                    var isFiado = string.Equals(state, "Fiado", StringComparison.OrdinalIgnoreCase);

                    var paid = ParseAmount(fields[3]);
                    var change = ParseAmount(fields[4]);
                    var total = ParseAmount(fields[6]);

                    var paymentMethod = fields.Length >= 9 ? fields[8].Trim() : (isFiado ? "N/A" : "Efectivo");
                    if (paymentMethod is not ("Efectivo" or "Tarjeta" or "Transferencia" or "N/A"))
                    {
                        paymentMethod = isFiado ? "N/A" : "Efectivo";
                    }

                    if (!TryParseFlexibleDate(fields[5].Trim(), out var date))
                    {
                        continue;
                    }

                    var products = DeserializeProducts(fields[1].Trim(), id);
                    var productSummary = products.Count == 0 ? fields[1].Trim() : string.Empty;

                    sales.Add(new Venta
                    {
                        Id = id,
                        FechaHora = date.ToLocalTime(),
                        Total = total,
                        PagoCon = paid,
                        Cambio = change,
                        Estado = state,
                        MetodoPago = paymentMethod,
                        Productos = products,
                        ProductosResumen = productSummary
                    });
                    continue;
                }

                // =========================================================================
                // RAMA 2: COMPATIBILIDAD CON VERSIONES HISTÓRICAS Y EXPORTACIONES PLANAS
                // =========================================================================

                // Caso 2.A: Formato clásico simple (6 columnas)
                // 0:Id // 1:Estado // 2:ProductosResumen // 3:CantidadTotal // 4:Fecha // 5:Total
                if (fields.Length == 6)
                {
                    var state = NormalizeState(fields[1]);
                    var isFiado = state == "Fiado";
                    var productsSummary = fields[2].Trim();
                    int.TryParse(fields[3].Trim(), out var qty);

                    if (!TryParseFlexibleDate(fields[4].Trim(), out var date))
                    {
                        continue;
                    }

                    var total = ParseAmount(fields[5]);

                    sales.Add(new Venta
                    {
                        Id = id,
                        Estado = state,
                        MetodoPago = isFiado ? "N/A" : "Efectivo",
                        ProductosResumen = productsSummary,
                        CantidadTotal = qty,
                        PagoCon = isFiado ? 0m : total,
                        Cambio = 0m,
                        FechaHora = date.ToLocalTime(),
                        Total = total,
                        Productos = []
                    });
                    continue;
                }

                // Caso 2.B: Formato intermedio (8 o 9 columnas orden plano legible)
                // 0:Id // 1:Estado // 2:Metodo // 3:ProductosResumen // 4:CantidadTotal // 5:PagoCon // 6:Cambio // 7:Fecha // 8:Total
                if (fields.Length >= 8)
                {
                    var state = NormalizeState(fields[1]);
                    var isFiado = state == "Fiado";
                    var method = string.IsNullOrWhiteSpace(fields[2]) ? (isFiado ? "N/A" : "Efectivo") : fields[2].Trim();
                    var productsSummary = fields[3].Trim();
                    int.TryParse(fields[4].Trim(), out var qty);
                    var paid = ParseAmount(fields[5]);
                    var change = ParseAmount(fields[6]);

                    if (!TryParseFlexibleDate(fields[7].Trim(), out var date))
                    {
                        continue;
                    }

                    var total = fields.Length >= 9 ? ParseAmount(fields[8]) : ParseAmount(fields[7]);

                    sales.Add(new Venta
                    {
                        Id = id,
                        Estado = state,
                        MetodoPago = method,
                        ProductosResumen = productsSummary,
                        CantidadTotal = qty,
                        PagoCon = paid,
                        Cambio = change,
                        FechaHora = date.ToLocalTime(),
                        Total = total,
                        Productos = []
                    });
                }
            }

            return sales;
        }

        private static bool IsIsoOrNumber(string qtyField, string dateField)
        {
            return int.TryParse(qtyField.Trim(), out _) &&
                   (dateField.Contains('T') || DateTime.TryParse(dateField.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        }

        private static string NormalizeState(string rawState)
        {
            var state = rawState.Trim();
            return state is ("Abierta" or "Cerrada" or "Cancelada" or "Devuelta" or "Fiado") ? state : "Cerrada";
        }

        private static bool TryParseFlexibleDate(string text, out DateTime date)
        {
            var clean = text.Trim();
            var mxCulture = CultureInfo.GetCultureInfo("es-MX");

            if (DateTime.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date))
            {
                return true;
            }

            if (DateTime.TryParse(clean, mxCulture, DateTimeStyles.None, out date))
            {
                return true;
            }

            if (DateTime.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return true;
            }

            return false;
        }

        private static List<DetalleVenta> DeserializeProducts(string raw, int saleId)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return [];
            }

            if (raw.StartsWith("P1:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var base64 = raw.Substring(3);
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                    var items = JsonSerializer.Deserialize<List<DetalleVenta>>(json);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            item.VentaId = saleId;
                        }
                        return items;
                    }
                }
                catch
                {
                    return [];
                }
            }

            return [];
        }

        private static decimal ParseAmount(string text)
        {
            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            if (decimal.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out var val) ||
                decimal.TryParse(text.Trim(), styles, CultureInfo.GetCultureInfo("es-MX"), out val))
            {
                return val;
            }
            return 0m;
        }

        public static void Save(IEnumerable<Venta> sales)
        {
            var saleList = sales.ToList();
            var ids = new HashSet<int>();

            foreach (var sale in saleList)
            {
                var isValidPayment = sale.Estado == "Fiado"
                    ? (sale.Total >= 0)
                    : (sale.PagoCon >= 0 && sale.Total >= 0 && sale.Cambio >= 0 && (sale.PagoCon >= sale.Total || sale.Total == 0));

                if (sale.Id <= 0 || !ids.Add(sale.Id) || sale.CantidadTotal < 0 || !isValidPayment ||
                    sale.Estado is not ("Abierta" or "Cerrada" or "Cancelada" or "Devuelta" or "Fiado") ||
                    (sale.Productos.Count > 0
                        ? sale.Productos.Any(product => product.Cantidad <= 0 || product.Precio < 0 || ContainsDelimiterOrNewLine(product.Nombre))
                        : ContainsDelimiterOrNewLine(sale.ProductosResumen)))
                {
                    throw new InvalidDataException("La venta contiene valores que no son validos para el formato establecido.");
                }
            }

            var lines = saleList.Select(sale => string.Join("//",
                sale.Id.ToString(CultureInfo.InvariantCulture),
                SerializeProducts(sale),
                sale.CantidadTotal.ToString(CultureInfo.InvariantCulture),
                sale.PagoCon.ToString(CultureInfo.InvariantCulture),
                sale.Cambio.ToString(CultureInfo.InvariantCulture),
                sale.FechaHora.ToString("O", CultureInfo.InvariantCulture),
                sale.Total.ToString(CultureInfo.InvariantCulture),
                sale.Estado,
                string.IsNullOrWhiteSpace(sale.MetodoPago) ? (sale.Estado == "Fiado" ? "N/A" : "Efectivo") : sale.MetodoPago));

            // Guardado atómico centralizado con vaciado a disco
            SafeFileStore.WriteAllLinesAtomic(FilePath, lines);
        }

        private static string SerializeProducts(Venta sale)
        {
            if (sale.Productos.Count == 0)
            {
                return sale.ProductosResumen;
            }

            var json = JsonSerializer.Serialize(sale.Productos.Select(product => new DetalleVenta
            {
                Id = product.Id,
                VentaId = product.VentaId,
                Nombre = product.Nombre,
                Cantidad = product.Cantidad,
                Costo = product.Costo,
                Ganancia = product.Ganancia,
                Precio = product.Precio,
                Categoria = string.IsNullOrWhiteSpace(product.Categoria) ? "N/A" : product.Categoria
            }));

            return $"P1:{Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}";
        }

        private static bool ContainsDelimiterOrNewLine(string value) =>
            value.Contains("//", StringComparison.Ordinal) || value.Contains('\r') || value.Contains('\n');
    }
}