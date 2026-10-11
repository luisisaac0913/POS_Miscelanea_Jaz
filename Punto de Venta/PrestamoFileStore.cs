using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Punto_de_Venta
{
    public static class PrestamoFileStore
    {
        private static readonly CultureInfo InvCulture = CultureInfo.InvariantCulture;

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiscelaneaJaz",
            "prestamos.txt");

        public static string AbonosFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiscelaneaJaz",
            "abonos_prestamos.txt");

        public static List<Deudor> Load()
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var debtors = new List<Deudor>();
            var ids = new HashSet<int>();
            var lines = File.ReadAllLines(FilePath);

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(new[] { "//" }, StringSplitOptions.None);
                if (parts.Length < 6 ||
                    !int.TryParse(parts[0].Trim(), NumberStyles.None, InvCulture, out var id) || id <= 0 ||
                    string.IsNullOrWhiteSpace(parts[1]))
                {
                    throw new InvalidDataException($"Formato invalido en la linea {index + 1} de prestamos.txt.");
                }

                var original = ParseAmount(parts[3], index + 1, "monto original");
                var paid = ParseAmount(parts[4], index + 1, "total abonado");
                var remaining = ParseAmount(parts[5], index + 1, "saldo restante");
                var telefono = parts.Length >= 7 ? parts[6].Trim() : string.Empty;

                if (original < 0 || paid < 0 || paid > original || remaining < 0)
                {
                    throw new InvalidDataException($"Los saldos no coinciden en la linea {index + 1} de prestamos.txt.");
                }

                if (!ids.Add(id))
                {
                    throw new InvalidDataException($"El ID {id} esta duplicado en la linea {index + 1} de prestamos.txt.");
                }

                debtors.Add(new Deudor
                {
                    Id = id,
                    Nombre = parts[1].Trim(),
                    Concepto = parts[2].Trim(),
                    MontoOriginal = original,
                    TotalAbonado = paid,
                    Telefono = telefono
                });
            }

            var abonosMap = LoadAbonos();
            foreach (var debtor in debtors)
            {
                if (abonosMap.TryGetValue(debtor.Id, out var listaAbonos))
                {
                    debtor.Abonos = listaAbonos.OrderByDescending(a => a.Fecha).ToList();
                }
            }

            return debtors;
        }

        public static void Save(IEnumerable<Deudor> debtors)
        {
            var debtorList = debtors.ToList();
            var ids = new HashSet<int>();

            foreach (var debtor in debtorList)
            {
                if (debtor.Id <= 0 || !ids.Add(debtor.Id))
                {
                    throw new InvalidDataException("Los prestamos deben tener IDs positivos y unicos.");
                }

                if (string.IsNullOrWhiteSpace(debtor.Nombre) ||
                    ContainsDelimiterOrNewLine(debtor.Nombre) || ContainsDelimiterOrNewLine(debtor.Concepto) ||
                    ContainsDelimiterOrNewLine(debtor.Telefono) ||
                    debtor.MontoOriginal < 0 || debtor.TotalAbonado < 0 || debtor.TotalAbonado > debtor.MontoOriginal)
                {
                    throw new InvalidDataException("El prestamo contiene datos no validos.");
                }
            }

            var lines = debtorList.Select(debtor => string.Join("//",
                debtor.Id.ToString(InvCulture),
                debtor.Nombre.Trim(),
                debtor.Concepto.Trim(),
                debtor.MontoOriginal.ToString(InvCulture),
                debtor.TotalAbonado.ToString(InvCulture),
                debtor.SaldoRestante.ToString(InvCulture),
                debtor.Telefono.Trim()));

            SafeFileStore.WriteAllLinesAtomic(FilePath, lines);
            SaveAbonos(debtorList.SelectMany(d => d.Abonos));
        }

        private static Dictionary<int, List<Abono>> LoadAbonos()
        {
            var map = new Dictionary<int, List<Abono>>();
            if (!File.Exists(AbonosFilePath)) return map;

            var lines = File.ReadAllLines(AbonosFilePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { "//" }, StringSplitOptions.None);
                if (parts.Length < 4) continue;

                if (int.TryParse(parts[0].Trim(), NumberStyles.None, InvCulture, out var id) &&
                    int.TryParse(parts[1].Trim(), NumberStyles.None, InvCulture, out var deudorId) &&
                    DateTime.TryParse(parts[2].Trim(), InvCulture, DateTimeStyles.RoundtripKind, out var fecha) &&
                    decimal.TryParse(parts[3].Trim(), NumberStyles.Any, InvCulture, out var monto))
                {
                    if (!map.TryGetValue(deudorId, out var list))
                    {
                        list = [];
                        map[deudorId] = list;
                    }
                    list.Add(new Abono
                    {
                        Id = id,
                        DeudorId = deudorId,
                        Fecha = fecha,
                        Monto = monto
                    });
                }
            }

            return map;
        }

        private static void SaveAbonos(IEnumerable<Abono> abonos)
        {
            var lines = abonos.Select(a => string.Join("//",
                a.Id.ToString(InvCulture),
                a.DeudorId.ToString(InvCulture),
                a.Fecha.ToString("o", InvCulture),
                a.Monto.ToString(InvCulture)));

            SafeFileStore.WriteAllLinesAtomic(AbonosFilePath, lines);
        }

        private static decimal ParseAmount(string text, int lineNumber, string fieldName)
        {
            var value = text.Trim();
            const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            if (decimal.TryParse(value, styles, InvCulture, out var amount) ||
                decimal.TryParse(value, styles, CultureInfo.GetCultureInfo("es-MX"), out amount) ||
                decimal.TryParse(value, styles, CultureInfo.GetCultureInfo("es-ES"), out amount))
            {
                return amount;
            }

            throw new InvalidDataException($"El campo {fieldName} es invalido en la linea {lineNumber} de prestamos.txt.");
        }

        private static bool ContainsDelimiterOrNewLine(string value) =>
            value.Contains("//", StringComparison.Ordinal) || value.Contains('\r') || value.Contains('\n');
    }
}