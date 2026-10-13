using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Punto_de_Venta
{
    public class MovimientoCaja
    {
        public int Id { get; set; }
        public DateTime FechaHora { get; set; } = DateTime.Now;
        // Tipos válidos: "Fondo", "Entrada", "Salida"
        public string Tipo { get; set; } = "Salida";
        public decimal Monto { get; set; }
        public string Concepto { get; set; } = string.Empty;
    }

    public static class MovimientoCajaFileStore
    {
        public static List<MovimientoCaja> Load()
        {
            var list = new List<MovimientoCaja>();

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, FechaHora, Tipo, Monto, Concepto FROM MovimientosCaja ORDER BY Id ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var fechaStr = reader.GetString(1);
                var tipo = reader.GetString(2);
                var monto = Convert.ToDecimal(reader.GetDouble(3));
                var concepto = reader.GetString(4);

                if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                {
                    DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
                }

                list.Add(new MovimientoCaja
                {
                    Id = id,
                    FechaHora = fecha,
                    Tipo = tipo,
                    Monto = monto,
                    Concepto = concepto
                });
            }

            return list;
        }

        public static void Save(IEnumerable<MovimientoCaja> movimientos)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();

            try
            {
                using (var delCmd = conn.CreateCommand())
                {
                    delCmd.Transaction = tr;
                    delCmd.CommandText = "DELETE FROM MovimientosCaja;";
                    delCmd.ExecuteNonQuery();
                }

                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tr;
                insertCmd.CommandText = @"
                    INSERT INTO MovimientosCaja (Id, FechaHora, Tipo, Monto, Concepto)
                    VALUES (@id, @fecha, @tipo, @monto, @concepto);";

                var pId = insertCmd.Parameters.Add("@id", SqliteType.Integer);
                var pFecha = insertCmd.Parameters.Add("@fecha", SqliteType.Text);
                var pTipo = insertCmd.Parameters.Add("@tipo", SqliteType.Text);
                var pMonto = insertCmd.Parameters.Add("@monto", SqliteType.Real);
                var pConcepto = insertCmd.Parameters.Add("@concepto", SqliteType.Text);

                foreach (var mov in movimientos)
                {
                    pId.Value = mov.Id;
                    pFecha.Value = mov.FechaHora.ToString("o", CultureInfo.InvariantCulture);
                    pTipo.Value = mov.Tipo ?? "Gasto";
                    pMonto.Value = Convert.ToDouble(mov.Monto);
                    pConcepto.Value = mov.Concepto ?? string.Empty;

                    insertCmd.ExecuteNonQuery();
                }

                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }
    }
}