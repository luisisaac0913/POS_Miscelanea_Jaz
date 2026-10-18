using Microsoft.Data.Sqlite;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using System;
using System.Globalization;

namespace Punto_de_Venta
{
    public static class TurnoCajaManager
    {
        /// <summary>
        /// Registra una entrada o salida manual de efectivo en el turno activo.
        /// </summary>
        /// <param name="tipo">Debe ser "Entrada" o "Salida"</param>
        /// <param name="monto">Cantidad en dinero</param>
        /// <param name="concepto">Razón del movimiento (ej. "Pago proveedor Coca-Cola")</param>
        public static void RegistrarMovimientoEfectivo(string tipo, decimal monto, string concepto)
        {
            var turnoActivo = ObtenerTurnoActivo();
            if (turnoActivo == null)
            {
                throw new InvalidOperationException("No se pueden registrar movimientos. La caja está cerrada.");
            }

            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();

            try
            {
                // 1. Insertar el movimiento
                using (var insertMovCmd = conn.CreateCommand())
                {
                    insertMovCmd.Transaction = tr;
                    insertMovCmd.CommandText = @"
                        INSERT INTO MovimientosCaja (FechaHora, Tipo, Monto, Concepto, CorteCajaId)
                        VALUES (@fecha, @tipo, @monto, @concepto, @corteId);";
                    insertMovCmd.Parameters.AddWithValue("@fecha", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                    insertMovCmd.Parameters.AddWithValue("@tipo", tipo);
                    insertMovCmd.Parameters.AddWithValue("@monto", Convert.ToDouble(monto));
                    insertMovCmd.Parameters.AddWithValue("@concepto", concepto);
                    insertMovCmd.Parameters.AddWithValue("@corteId", turnoActivo.Id);
                    insertMovCmd.ExecuteNonQuery();
                }

                // 2. Actualizar los acumulados del turno
                using (var updateCorteCmd = conn.CreateCommand())
                {
                    updateCorteCmd.Transaction = tr;
                    if (tipo.Equals("Entrada", StringComparison.OrdinalIgnoreCase))
                    {
                        updateCorteCmd.CommandText = @"
                            UPDATE CortesCaja 
                            SET EntradasEfectivo = EntradasEfectivo + @monto,
                                TotalEsperado = TotalEsperado + @monto
                            WHERE Id = @id;";
                    }
                    else if (tipo.Equals("Salida", StringComparison.OrdinalIgnoreCase))
                    {
                        updateCorteCmd.CommandText = @"
                            UPDATE CortesCaja 
                            SET SalidasEfectivo = SalidasEfectivo + @monto,
                                TotalEsperado = TotalEsperado - @monto
                            WHERE Id = @id;";
                    }

                    updateCorteCmd.Parameters.AddWithValue("@monto", Convert.ToDouble(monto));
                    updateCorteCmd.Parameters.AddWithValue("@id", turnoActivo.Id);
                    updateCorteCmd.ExecuteNonQuery();
                }

                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }
        /// <summary>
        /// Obtiene el turno actualmente abierto en la base de datos, o null si la caja está cerrada.
        /// </summary>
        public static CorteCaja ObtenerTurnoActivo()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FechaHora, FechaHoraCierre, Estado, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones 
                FROM CortesCaja 
                WHERE Estado = 'Abierto' 
                ORDER BY Id DESC 
                LIMIT 1;";

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var id = reader.GetInt32(0);
                var fechaStr = reader.GetString(1);
                var fechaCierreStr = reader.IsDBNull(2) ? null : reader.GetString(2);
                var estado = reader.GetString(3);
                var fondo = Convert.ToDecimal(reader.GetDouble(4));
                var ventas = Convert.ToDecimal(reader.GetDouble(5));
                var entradas = Convert.ToDecimal(reader.GetDouble(6));
                var salidas = Convert.ToDecimal(reader.GetDouble(7));
                var esperado = Convert.ToDecimal(reader.GetDouble(8));
                var contado = Convert.ToDecimal(reader.GetDouble(9));
                var notas = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);

                DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaApertura);

                DateTime? fechaCierre = null;
                if (!string.IsNullOrEmpty(fechaCierreStr) && DateTime.TryParse(fechaCierreStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fCierre))
                {
                    fechaCierre = fCierre;
                }

                return new CorteCaja
                {
                    Id = id,
                    FechaHora = fechaApertura,
                    FechaHoraCierre = fechaCierre,
                    Estado = estado,
                    FondoInicial = fondo,
                    VentasEfectivo = ventas,
                    EntradasEfectivo = entradas,
                    SalidasEfectivo = salidas,
                    TotalEsperado = esperado,
                    TotalContado = contado,
                    Observaciones = notas
                };
            }

            return null;
        }
        /// <summary>
        /// Cierra el turno activo, calculando el total esperado y comparándolo con el total físico contado por el cajero (Arqueo Ciego).
        /// </summary>
        public static void CerrarTurno(CorteCaja turnoActivo, decimal totalContadoFisico, string observacionesCierre)
        {
            using var conn = DatabaseHelper.GetConnection();

            // 1. Obtener la suma de Entradas y Salidas vinculadas a este turno exacto
            using var cmdMovimientos = conn.CreateCommand();
            cmdMovimientos.CommandText = @"
        SELECT 
            COALESCE(SUM(CASE WHEN Tipo = 'Entrada' THEN Monto ELSE 0 END), 0) AS TotalEntradas,
            COALESCE(SUM(CASE WHEN Tipo = 'Salida' THEN Monto ELSE 0 END), 0) AS TotalSalidas
        FROM MovimientosCaja 
        WHERE CorteCajaId = @idTurno;";
            cmdMovimientos.Parameters.AddWithValue("@idTurno", turnoActivo.Id);

            decimal totalEntradas = 0;
            decimal totalSalidas = 0;
            using (var reader = cmdMovimientos.ExecuteReader())
            {
                if (reader.Read())
                {
                    totalEntradas = Convert.ToDecimal(reader.GetDouble(0));
                    totalSalidas = Convert.ToDecimal(reader.GetDouble(1));
                }
            }

            // 2. Obtener la suma de Ventas en Efectivo realizadas desde que se abrió este turno
            using var cmdVentas = conn.CreateCommand();
            cmdVentas.CommandText = @"
        SELECT COALESCE(SUM(Total), 0) 
        FROM Ventas 
        WHERE MetodoPago = 'Efectivo' 
          AND Estado = 'Completada' 
          AND FechaHora >= @fechaApertura;";
            cmdVentas.Parameters.AddWithValue("@fechaApertura", turnoActivo.FechaHora.ToString("o", System.Globalization.CultureInfo.InvariantCulture));

            decimal ventasEfectivo = Convert.ToDecimal(cmdVentas.ExecuteScalar());

            // 3. Calcular el Total Esperado en Caja (Fórmula del Arqueo)
            decimal totalEsperado = turnoActivo.FondoInicial + ventasEfectivo + totalEntradas - totalSalidas;

            // 4. Actualizar el registro del turno en la base de datos para cerrarlo
            using var cmdCierre = conn.CreateCommand();
            cmdCierre.CommandText = @"
        UPDATE CortesCaja 
        SET FechaHoraCierre = @fechaCierre, 
            Estado = 'Cerrado', 
            VentasEfectivo = @ventasEfectivo,
            EntradasEfectivo = @entradas,
            SalidasEfectivo = @salidas,
            TotalEsperado = @esperado,
            TotalContado = @contado,
            Observaciones = @obs 
        WHERE Id = @idTurno;";

            cmdCierre.Parameters.AddWithValue("@fechaCierre", DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            cmdCierre.Parameters.AddWithValue("@ventasEfectivo", Convert.ToDouble(ventasEfectivo));
            cmdCierre.Parameters.AddWithValue("@entradas", Convert.ToDouble(totalEntradas));
            cmdCierre.Parameters.AddWithValue("@salidas", Convert.ToDouble(totalSalidas));
            cmdCierre.Parameters.AddWithValue("@esperado", Convert.ToDouble(totalEsperado));
            cmdCierre.Parameters.AddWithValue("@contado", Convert.ToDouble(totalContadoFisico));
            cmdCierre.Parameters.AddWithValue("@obs", observacionesCierre);
            cmdCierre.Parameters.AddWithValue("@idTurno", turnoActivo.Id);

            cmdCierre.ExecuteNonQuery();
        }

        /// <summary>
        /// Registra la apertura de un nuevo turno de caja con su fondo inicial en SQLite.
        /// </summary>
        public static CorteCaja AbrirTurno(decimal fondoInicial, string concepto = "Fondo de apertura")
        {
            var turnoActivo = ObtenerTurnoActivo();
            if (turnoActivo != null)
            {
                return turnoActivo; // Ya hay un turno abierto
            }

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CortesCaja (FechaHora, Estado, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones)
                VALUES (@fecha, 'Abierto', @fondo, 0, 0, 0, @fondo, 0, @obs);
                SELECT last_insert_rowid();";

            var ahoraStr = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            cmd.Parameters.AddWithValue("@fecha", ahoraStr);
            cmd.Parameters.AddWithValue("@fondo", Convert.ToDouble(fondoInicial));
            cmd.Parameters.AddWithValue("@obs", concepto);

            long idGenerado = (long)cmd.ExecuteScalar();

            return new CorteCaja
            {
                Id = (int)idGenerado,
                FechaHora = DateTime.Now,
                Estado = "Abierto",
                FondoInicial = fondoInicial,
                TotalEsperado = fondoInicial,
                Observaciones = concepto
            };
        }
    }
}