using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Punto_de_Venta
{
    public static class CorteCajaFileStore
    {
        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiscelaneaJaz",
            "cortes.txt");

        public static List<CorteCaja> Load()
        {
            if (!File.Exists(FilePath))
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                using (File.Create(FilePath)) { }
                return [];
            }

            var list = new List<CorteCaja>();
            var lines = File.ReadAllLines(FilePath);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { "//" }, StringSplitOptions.None);
                if (parts.Length < 8) continue;

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
                if (!DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)) continue;

                var fondo = ParseDecimal(parts[2]);
                var ventas = ParseDecimal(parts[3]);
                var entradas = ParseDecimal(parts[4]);
                var salidas = ParseDecimal(parts[5]);
                var esperado = ParseDecimal(parts[6]);
                var contado = ParseDecimal(parts[7]);
                var notas = parts.Length > 8 ? parts[8] : string.Empty;

                list.Add(new CorteCaja
                {
                    Id = id,
                    FechaHora = fecha,
                    FondoInicial = fondo,
                    VentasEfectivo = ventas,
                    EntradasEfectivo = entradas,
                    SalidasEfectivo = salidas,
                    TotalEsperado = esperado,
                    TotalContado = contado,
                    Observaciones = notas
                });
            }

            return list;
        }

        public static void Save(IEnumerable<CorteCaja> cortes)
        {
            var lines = cortes.Select(c => string.Join("//",
                c.Id.ToString(CultureInfo.InvariantCulture),
                c.FechaHora.ToString("o", CultureInfo.InvariantCulture),
                c.FondoInicial.ToString("0.00", CultureInfo.InvariantCulture),
                c.VentasEfectivo.ToString("0.00", CultureInfo.InvariantCulture),
                c.EntradasEfectivo.ToString("0.00", CultureInfo.InvariantCulture),
                c.SalidasEfectivo.ToString("0.00", CultureInfo.InvariantCulture),
                c.TotalEsperado.ToString("0.00", CultureInfo.InvariantCulture),
                c.TotalContado.ToString("0.00", CultureInfo.InvariantCulture),
                c.Observaciones.Replace("//", " ").Trim()));

            // Guardado atómico centralizado con vaciado físico
            SafeFileStore.WriteAllLinesAtomic(FilePath, lines);
        }

        private static decimal ParseDecimal(string text)
        {
            if (decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var val) ||
                decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out val))
            {
                return val;
            }
            return 0m;
        }
    }
}