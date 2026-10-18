using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Text;

namespace Punto_de_Venta.Servicios
{
    public static class TicketService
    {
        private static readonly string TicketsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiscelaneaJaz",
            "Tickets"
        );

        public static string GenerarTextoTicket(Venta venta, IEnumerable<ItemCarrito> items, decimal pagoCon, decimal cambio, AppConfig config)
        {
            var sb = new StringBuilder();
            var culture = CultureInfo.GetCultureInfo("es-MX");
            int ancho = config.AnchoTicketCaracteres > 0 ? config.AnchoTicketCaracteres : 32;

            // Encabezado personalizable
            if (!string.IsNullOrWhiteSpace(config.NombreNegocio))
                sb.AppendLine(CentrarTexto(config.NombreNegocio.Trim(), ancho));

            if (!string.IsNullOrWhiteSpace(config.GiroNegocio))
                sb.AppendLine(CentrarTexto(config.GiroNegocio.Trim(), ancho));

            if (!string.IsNullOrWhiteSpace(config.DireccionNegocio))
                sb.AppendLine(CentrarTexto(config.DireccionNegocio.Trim(), ancho));

            sb.AppendLine(new string('-', ancho));

            // Metadatos
            sb.AppendLine($"Folio: #{venta.Id}");
            sb.AppendLine($"Fecha: {venta.FechaHora:dd/MM/yyyy hh:mm tt}");
            sb.AppendLine($"Metodo: {venta.MetodoPago}");
            sb.AppendLine(new string('-', ancho));

            // Columnas
            sb.AppendLine("CANT PRODUCTO              TOTAL");
            sb.AppendLine(new string('-', ancho));

            // Detalle de productos
            foreach (var item in items)
            {
                string cantDesc = $"{item.Cantidad}x {item.Nombre}";
                string subtotalStr = item.Subtotal.ToString("0.00", culture);

                if (cantDesc.Length + subtotalStr.Length + 1 > ancho)
                {
                    if (cantDesc.Length > ancho - subtotalStr.Length - 1)
                    {
                        cantDesc = cantDesc.Substring(0, Math.Max(1, ancho - subtotalStr.Length - 2)) + ".";
                    }
                }

                int espacios = ancho - cantDesc.Length - subtotalStr.Length;
                sb.AppendLine($"{cantDesc}{new string(' ', Math.Max(1, espacios))}{subtotalStr}");
            }

            sb.AppendLine(new string('-', ancho));

            // Totales
            sb.AppendLine(AlinearDerecha($"TOTAL: {venta.Total.ToString("C2", culture)}", ancho));
            if (venta.MetodoPago != "Fiado")
            {
                sb.AppendLine(AlinearDerecha($"PAGO CON: {pagoCon.ToString("C2", culture)}", ancho));
                sb.AppendLine(AlinearDerecha($"CAMBIO: {cambio.ToString("C2", culture)}", ancho));
            }
            else
            {
                sb.AppendLine(AlinearDerecha("CUENTA POR COBRAR (FIADO)", ancho));
            }

            sb.AppendLine(new string('-', ancho));

            // Pie de página personalizable
            if (!string.IsNullOrWhiteSpace(config.MensajePie))
            {
                foreach (var linea in config.MensajePie.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
                {
                    sb.AppendLine(CentrarTexto(linea.Trim(), ancho));
                }
            }

            sb.AppendLine("\n\n");
            return sb.ToString();
        }

        public static void GuardarCopiaDigital(Venta venta, IEnumerable<ItemCarrito> items, decimal pagoCon, decimal cambio, AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(TicketsFolder);
                var rutaArchivo = Path.Combine(TicketsFolder, $"Ticket_{venta.Id}.txt");
                var texto = GenerarTextoTicket(venta, items, pagoCon, cambio, config);
                File.WriteAllText(rutaArchivo, texto, Encoding.UTF8);
            }
            catch { }
        }

        public static void ImprimirFisico(Venta venta, IEnumerable<ItemCarrito> items, decimal pagoCon, decimal cambio, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.ImpresoraSeleccionada) ||
                config.ImpresoraSeleccionada.Contains("Ninguna", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var textoTicket = GenerarTextoTicket(venta, items, pagoCon, cambio, config);
                var printDoc = new PrintDocument();
                printDoc.PrinterSettings.PrinterName = config.ImpresoraSeleccionada;

                printDoc.PrintPage += (sender, e) =>
                {
                    using var font = new Font("Consolas", 8, System.Drawing.FontStyle.Regular);
                    e.Graphics?.DrawString(textoTicket, font, Brushes.Black, new PointF(0, 0));
                };

                printDoc.Print();
            }
            catch { }
        }

        public static string GenerarTicketMuestra(AppConfig config)
        {
            var ventaPrueba = new Venta
            {
                Id = 101,
                FechaHora = DateTime.Now,
                Total = 45.00m,
                MetodoPago = "Efectivo"
            };

            var itemsPrueba = new List<ItemCarrito>
            {
                new() { Nombre = "Coca Cola 2.5L", Cantidad = 1, Precio = 35.00m },
                new() { Nombre = "Papas Sal 45g", Cantidad = 1, Precio = 10.00m }
            };

            return GenerarTextoTicket(ventaPrueba, itemsPrueba, 50.00m, 5.00m, config);
        }

        private static string CentrarTexto(string texto, int ancho)
        {
            if (texto.Length >= ancho) return texto.Substring(0, ancho);
            int espaciosIzq = (ancho - texto.Length) / 2;
            return new string(' ', espaciosIzq) + texto;
        }

        private static string AlinearDerecha(string texto, int ancho)
        {
            if (texto.Length >= ancho) return texto;
            return new string(' ', ancho - texto.Length) + texto;
        }
    }
}