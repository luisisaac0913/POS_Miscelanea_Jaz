using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Punto_de_Venta
{
    public static class ProductoFileStore
    {
        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiscelaneaJaz",
            "productos.txt");

        public static List<Producto> Load()
        {
            if (!File.Exists(FilePath))
            {
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                using (File.Create(FilePath)) { }
                return [];
            }

            var products = new List<Producto>();
            var ids = new HashSet<int>();
            var lines = File.ReadAllLines(FilePath);

            for (var index = 0; index < lines.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(lines[index])) continue;

                var fields = lines[index].Split(new[] { "//" }, StringSplitOptions.None);

                // Formatos compatibles: 4, 5, 6, 7, 8 o 9 campos
                if (fields.Length < 4 ||
                    !int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                {
                    continue;
                }

                var code = fields[1].Trim();
                var name = fields[2].Trim();
                var category = "N/A";
                decimal cost = 0m;
                decimal margin = 0m;
                decimal price = 0m;
                int stock = 0;
                bool pedirCantidad = false;

                if (fields.Length == 4)
                {
                    price = ParseAmount(fields[3]);
                    margin = price;
                }
                else if (fields.Length == 5)
                {
                    price = ParseAmount(fields[3]);
                    category = string.IsNullOrWhiteSpace(fields[4]) ? "N/A" : fields[4].Trim();
                    margin = price;
                }
                else if (fields.Length == 6)
                {
                    cost = ParseAmount(fields[3]);
                    margin = ParseAmount(fields[4]);
                    price = ParseAmount(fields[5]);
                }
                else if (fields.Length == 7)
                {
                    category = string.IsNullOrWhiteSpace(fields[3]) ? "N/A" : fields[3].Trim();
                    cost = ParseAmount(fields[4]);
                    margin = ParseAmount(fields[5]);
                    price = ParseAmount(fields[6]);
                }
                else if (fields.Length >= 8)
                {
                    category = string.IsNullOrWhiteSpace(fields[3]) ? "N/A" : fields[3].Trim();
                    cost = ParseAmount(fields[4]);
                    margin = ParseAmount(fields[5]);
                    price = ParseAmount(fields[6]);
                    int.TryParse(fields[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stock);

                    // Si existe el noveno campo, leemos la bandera
                    if (fields.Length >= 9)
                    {
                        bool.TryParse(fields[8].Trim(), out pedirCantidad);
                    }
                }

                if (price == 0m && (cost > 0m || margin > 0m))
                {
                    price = cost + margin;
                }

                if (!ids.Add(id)) continue;

                products.Add(new Producto
                {
                    Id = id,
                    Codigo = code,
                    Nombre = name,
                    Categoria = category,
                    Costo = cost,
                    Ganancia = margin,
                    Precio = price,
                    Stock = stock,
                    PedirCantidadAlCobrar = pedirCantidad
                });
            }

            return products;
        }

        public static void Save(IEnumerable<Producto> products)
        {
            var productList = products.ToList();
            var ids = new HashSet<int>();
            foreach (var product in productList)
            {
                if (product.Id <= 0 || !ids.Add(product.Id) ||
                    string.IsNullOrWhiteSpace(product.Nombre) ||
                    ContainsDelimiterOrNewLine(product.Nombre) ||
                    ContainsDelimiterOrNewLine(product.Codigo) ||
                    ContainsDelimiterOrNewLine(product.Categoria))
                {
                    throw new InvalidDataException("El producto contiene caracteres no validos (// o saltos de linea).");
                }
            }
            // Guarda estándar: ID//Codigo//Nombre//Categoria//Costo//Ganancia//Precio//Stock//PedirCantidadAlCobrar
            var lines = productList.Select(product => string.Join("//",
                product.Id.ToString(CultureInfo.InvariantCulture),
                product.Codigo.Trim(),
                product.Nombre.Trim(),
                string.IsNullOrWhiteSpace(product.Categoria) ? "N/A" : product.Categoria.Trim(),
                product.Costo.ToString("0.00", CultureInfo.InvariantCulture),
                product.Ganancia.ToString("0.00", CultureInfo.InvariantCulture),
                product.Precio.ToString("0.00", CultureInfo.InvariantCulture),
                product.Stock.ToString(CultureInfo.InvariantCulture),
                product.PedirCantidadAlCobrar.ToString().ToLowerInvariant()));

            // Guardado atómico centralizado con vaciado a disco
            SafeFileStore.WriteAllLinesAtomic(FilePath, lines);
        }

        private static decimal ParseAmount(string text)
        {
            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            if (decimal.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out var value) ||
                decimal.TryParse(text.Trim(), styles, CultureInfo.GetCultureInfo("es-MX"), out value))
            {
                return value;
            }
            return 0m;
        }

        private static bool ContainsDelimiterOrNewLine(string value) =>
            value.Contains("//", StringComparison.Ordinal) || value.Contains('\r') || value.Contains('\n');
    }
}