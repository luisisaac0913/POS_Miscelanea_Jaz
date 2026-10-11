using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Punto_de_Venta
{
    public class MovimientoCaja
    {
        public int Id { get; set; }
        public DateTime FechaHora { get; set; } = DateTime.Now;
        // Tipos validos: "Fondo", "Entrada", "Salida"
        public string Tipo { get; set; } = "Salida";
        public decimal Monto { get; set; }
        public string Concepto { get; set; } = string.Empty;
    }

    public static class MovimientoCajaFileStore
    {
        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiscelaneaJaz",
            "movimientos_caja.txt");

        public static List<MovimientoCaja> Load()
        {
            if (!File.Exists(FilePath))
            {
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                using (File.Create(FilePath)) { }
                return [];
            }

            var list = new List<MovimientoCaja>();
            var lines = File.ReadAllLines(FilePath);

            for (var index = 0; index < lines.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(lines[index])) continue;

                var fields = lines[index].Split(new[] { "//" }, StringSplitOptions.None);
                if (fields.Length < 5) continue;

                if (!int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                    !DateTime.TryParse(fields[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ||
                    !decimal.TryParse(fields[3].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var monto))
                {
                    continue;
                }

                list.Add(new MovimientoCaja
                {
                    Id = id,
                    FechaHora = date.ToLocalTime(),
                    Tipo = fields[2].Trim(),
                    Monto = monto,
                    Concepto = fields[4].Trim()
                });
            }

            return list;
        }

        public static void Save(IEnumerable<MovimientoCaja> movimientos)
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"movimientos.{Guid.NewGuid():N}.tmp");

            try
            {
                var lines = movimientos.Select(m => string.Join("//",
                    m.Id.ToString(CultureInfo.InvariantCulture),
                    m.FechaHora.ToString("O", CultureInfo.InvariantCulture),
                    m.Tipo,
                    m.Monto.ToString("0.00", CultureInfo.InvariantCulture),
                    m.Concepto.Replace("//", "/").Replace("\r", "").Replace("\n", "")));

                File.WriteAllLines(temporaryPath, lines, new UTF8Encoding(false));
                File.Move(temporaryPath, FilePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}