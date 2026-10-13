using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace Punto_de_Venta
{
    public static class ProductoFileStore
    {
        public static List<Producto> Load()
        {
            var list = new List<Producto>();

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar 
                FROM Productos 
                ORDER BY Id ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Producto
                {
                    Id = reader.GetInt32(0),
                    Codigo = reader.GetString(1),
                    Nombre = reader.GetString(2),
                    Categoria = reader.GetString(3),
                    Costo = Convert.ToDecimal(reader.GetDouble(4)),
                    Ganancia = Convert.ToDecimal(reader.GetDouble(5)),
                    Precio = Convert.ToDecimal(reader.GetDouble(6)),
                    Stock = reader.GetInt32(7),
                    PedirCantidadAlCobrar = reader.GetInt32(8) == 1
                });
            }

            return list;
        }

        public static void Save(IEnumerable<Producto> products)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var transaction = conn.BeginTransaction();

            try
            {
                // Limpiar y reinsertar dentro de una sola transacción protegida
                using (var deleteCmd = conn.CreateCommand())
                {
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM Productos;";
                    deleteCmd.ExecuteNonQuery();
                }

                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT INTO Productos (Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar)
                    VALUES (@id, @codigo, @nombre, @categoria, @costo, @ganancia, @precio, @stock, @pedirCantidad);";

                var pId = insertCmd.Parameters.Add("@id", SqliteType.Integer);
                var pCodigo = insertCmd.Parameters.Add("@codigo", SqliteType.Text);
                var pNombre = insertCmd.Parameters.Add("@nombre", SqliteType.Text);
                var pCategoria = insertCmd.Parameters.Add("@categoria", SqliteType.Text);
                var pCosto = insertCmd.Parameters.Add("@costo", SqliteType.Real);
                var pGanancia = insertCmd.Parameters.Add("@ganancia", SqliteType.Real);
                var pPrecio = insertCmd.Parameters.Add("@precio", SqliteType.Real);
                var pStock = insertCmd.Parameters.Add("@stock", SqliteType.Integer);
                var pPedir = insertCmd.Parameters.Add("@pedirCantidad", SqliteType.Integer);

                foreach (var prod in products)
                {
                    pId.Value = prod.Id;
                    pCodigo.Value = prod.Codigo ?? string.Empty;
                    pNombre.Value = prod.Nombre ?? string.Empty;
                    pCategoria.Value = prod.Categoria ?? "N/A";
                    pCosto.Value = Convert.ToDouble(prod.Costo);
                    pGanancia.Value = Convert.ToDouble(prod.Ganancia);
                    pPrecio.Value = Convert.ToDouble(prod.Precio);
                    pStock.Value = prod.Stock;
                    pPedir.Value = prod.PedirCantidadAlCobrar ? 1 : 0;

                    insertCmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Búsqueda ultra rápida directa en SQLite aprovechando los índices (opcional para Cobro).
        /// </summary>
        public static Producto? BuscarPorCodigoOId(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar 
                FROM Productos 
                WHERE Codigo = @query OR Id = @queryInt
                LIMIT 1;";

            cmd.Parameters.AddWithValue("@query", query.Trim());
            cmd.Parameters.AddWithValue("@queryInt", int.TryParse(query.Trim(), out var parsedId) ? parsedId : -1);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new Producto
                {
                    Id = reader.GetInt32(0),
                    Codigo = reader.GetString(1),
                    Nombre = reader.GetString(2),
                    Categoria = reader.GetString(3),
                    Costo = Convert.ToDecimal(reader.GetDouble(4)),
                    Ganancia = Convert.ToDecimal(reader.GetDouble(5)),
                    Precio = Convert.ToDecimal(reader.GetDouble(6)),
                    Stock = reader.GetInt32(7),
                    PedirCantidadAlCobrar = reader.GetInt32(8) == 1
                };
            }

            return null;
        }
    }
}