using Microsoft.Win32;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using Punto_de_Venta.Vistas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _clockTimer;
        private bool _isShuttingDown;

        private readonly Dictionary<string, UserControl> _vistasCache = new Dictionary<string, UserControl>();

        public MainWindow()
        {
            InitializeComponent();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (_, _) => UpdateClock();

            Closing += (_, _) =>
            {
                if (!_isShuttingDown)
                {
                    _isShuttingDown = true;

                    try
                    {
                        var config = AppConfig.Load();
                        BackupService.GenerarRespaldo("Cierre", config.RutaRespaldos);
                    }
                    catch
                    {
                        BackupService.GenerarRespaldo("Cierre");
                    }

                    Application.Current.Shutdown();
                }
            };

            UpdateClock();
            _clockTimer.Start();
        }

        private void AbrirVista<T>() where T : UserControl, new()
        {
            string nombreVista = typeof(T).Name;

            if (!_vistasCache.ContainsKey(nombreVista))
            {
                _vistasCache[nombreVista] = new T();
            }

            // REPARACIÓN DEL PANTALLAZO EN BLANCO: 
            // Forzar que el UserControl vuelva a ser visible, ya que el evento ESC lo oculta internamente.
            _vistasCache[nombreVista].Visibility = Visibility.Visible;

            GridDashboard.Visibility = Visibility.Collapsed;
            ContenedorVistas.Content = _vistasCache[nombreVista];
            ContenedorVistas.Visibility = Visibility.Visible;

            // Foco retardado para asegurar que la vista activa reciba los eventos del teclado
            Dispatcher.BeginInvoke(new Action(() => {
                _vistasCache[nombreVista].Focus();
            }), DispatcherPriority.Loaded);
        }

        public void RegresarAlMenu()
        {
            // 1. Ocultar y limpiar el contenedor de la vista modular
            ContenedorVistas.Visibility = Visibility.Collapsed;
            ContenedorVistas.Content = null;

            // 2. Volver a mostrar el panel de inicio (Dashboard Bento)
            if (GridDashboard != null)
            {
                GridDashboard.Visibility = Visibility.Visible;
            }

            // 3. Devolver el foco a la ventana principal
            this.Focus();
            Keyboard.Focus(this);
        }

        private void ModernTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateClock()
        {
            var now = DateTime.Now;
            var spanishCulture = CultureInfo.GetCultureInfo("es-MX");

            if (FindName("ClockText") is TextBlock clockBlock)
            {
                clockBlock.Text = now.ToString("hh:mm:ss tt", CultureInfo.GetCultureInfo("en-US"));
            }

            if (FindName("DateText") is TextBlock dateBlock)
            {
                dateBlock.Text = now.ToString("dddd, d 'de' MMMM 'de' yyyy", spanishCulture);
            }
        }

        private void Cobro_Click(object sender, RoutedEventArgs e) => AbrirVista<Cobro>();
        private void Prestamos_Click(object sender, RoutedEventArgs e) => AbrirVista<Prestamos>();
        private void CodigosDeBarras_Click(object sender, RoutedEventArgs e) => AbrirVista<CodigosBarrasWindow>();

        private void Inventario_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Inventario>();
        }

        private void Ventas_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Ventas>();
        }

        private void Graficas_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Graficas>();
        }

        private void Configuracion_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Configuracion>();
        }

        private void Promociones_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Promociones>();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // REPARACIÓN CLAVE DEL ESC: Si el modal de exportar está abierto, lo cierra.
            if (ExportarOverlay != null && ExportarOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    CerrarExportar();
                    e.Handled = true;
                }
                return;
            }

            // REPARACIÓN CLAVE DEL ESC: Si un módulo está abierto, dejamos que el UserControl 
            // y sus modales internos manejen el ESC por sí mismos.
            if (ContenedorVistas.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    // Dejamos pasar el evento para que llegue al UserControl activo
                    return;
                }
                // Retornamos para evitar que estando dentro de un módulo, presiones teclas del menú general.
                return;
            }

            // Atajos directos desde el menú de inicio
            switch (e.Key)
            {
                case Key.D1:
                    Cobro_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D2:
                    Ventas_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D3:
                    Inventario_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D4:
                    Prestamos_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D5:
                    Graficas_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D6:
                    Configuracion_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D7:
                    Exportar_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D8:
                    Promociones_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }

        private void BtnSubirLista_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar lista de productos para importar a SQLite"
            };

            if (openFileDialog.ShowDialog() != true) return;

            try
            {
                var lineasNuevas = File.ReadAllLines(openFileDialog.FileName, Encoding.UTF8);
                var productosExistentes = ProductoDB.Load();
                var catalogoPorId = productosExistentes.ToDictionary(p => p.Id);

                int maxId = catalogoPorId.Keys.DefaultIfEmpty(0).Max();
                int importadosCount = 0;

                foreach (var linea in lineasNuevas)
                {
                    if (string.IsNullOrWhiteSpace(linea)) continue;
                    var prod = ParsearLineaProducto(linea);
                    if (prod == null) continue;

                    if (prod.Id <= 0 || !catalogoPorId.ContainsKey(prod.Id))
                    {
                        var existentePorCodigo = catalogoPorId.Values.FirstOrDefault(p => !string.IsNullOrEmpty(p.Codigo) && p.Codigo == prod.Codigo);
                        if (existentePorCodigo != null)
                        {
                            prod.Id = existentePorCodigo.Id;
                            catalogoPorId[prod.Id] = prod;
                        }
                        else
                        {
                            maxId++;
                            prod.Id = maxId;
                            catalogoPorId[prod.Id] = prod;
                        }
                    }
                    else
                    {
                        catalogoPorId[prod.Id] = prod;
                    }
                    importadosCount++;
                }

                ProductoDB.Save(catalogoPorId.Values);
                ToastNotification.Show($"Se importaron y guardaron {importadosCount} productos en SQLite correctamente", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al importar archivo: {ex.Message}", ToastType.Error);
            }
        }

        private static Producto? ParsearLineaProducto(string linea)
        {
            var partes = linea.Split(new[] { "//" }, StringSplitOptions.None);
            if (partes.Length < 4) return null;

            int.TryParse(partes[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
            var codigo = partes[1].Trim();
            var nombre = partes[2].Trim();
            if (string.IsNullOrWhiteSpace(nombre)) return null;

            var categoria = "N/A";
            decimal costo = 0m, ganancia = 0m, precio = 0m;
            int stock = 0;
            bool pedirCantidad = false, seVendePorPeso = false;

            if (partes.Length >= 10)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3].Trim();
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
                int.TryParse(partes[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stock);
                bool.TryParse(partes[8].Trim(), out pedirCantidad);
                bool.TryParse(partes[9].Trim(), out seVendePorPeso);
            }
            else if (partes.Length == 9)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3].Trim();
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
                int.TryParse(partes[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stock);
                bool.TryParse(partes[8].Trim(), out pedirCantidad);
            }
            else if (partes.Length == 8)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3].Trim();
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
                int.TryParse(partes[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stock);
            }
            else if (partes.Length == 7)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3].Trim();
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
            }
            else if (partes.Length == 6)
            {
                costo = ParseDecimalFlexible(partes[3]);
                ganancia = ParseDecimalFlexible(partes[4]);
                precio = ParseDecimalFlexible(partes[5]);
            }
            else if (partes.Length == 5)
            {
                precio = ParseDecimalFlexible(partes[3]);
                categoria = string.IsNullOrWhiteSpace(partes[4]) ? "N/A" : partes[4].Trim();
                ganancia = precio;
            }
            else if (partes.Length == 4)
            {
                precio = ParseDecimalFlexible(partes[3]);
                ganancia = precio;
            }

            if (precio == 0m && (costo > 0m || ganancia > 0m))
            {
                precio = costo + ganancia;
            }

            return new Producto
            {
                Id = id,
                Codigo = codigo,
                Nombre = nombre,
                Categoria = categoria,
                Costo = costo,
                Ganancia = ganancia,
                Precio = precio,
                Stock = stock,
                PedirCantidadAlCobrar = pedirCantidad,
                SeVendePorPeso = seVendePorPeso
            };
        }

        private static decimal ParseDecimalFlexible(string text)
        {
            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            if (decimal.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out var val) ||
                decimal.TryParse(text.Trim(), styles, CultureInfo.GetCultureInfo("es-MX"), out val))
            {
                return val;
            }
            return 0m;
        }

        private void Exportar_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("ExportarOverlay") is Grid overlay)
            {
                overlay.Visibility = Visibility.Visible;
                overlay.Focus();
            }
        }

        private void ExportarOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FindName("ExportarOverlay") is Grid overlay && ReferenceEquals(e.OriginalSource, overlay))
            {
                CerrarExportar();
            }
        }

        private void ExportarOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CerrarExportar();
                e.Handled = true;
            }
        }

        private void ExportarVentas_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var hoy = DateTime.Today;
                var inicioSemana = hoy.AddDays(-6);
                var sales = VentaDB.Load().Where(sale =>
                {
                    var fechaLocal = sale.FechaHora.ToLocalTime().Date;
                    return sale.Estado != "Cancelada" && fechaLocal >= inicioSemana && fechaLocal <= hoy;
                }).ToList();
                ExportarArchivo("Reporte_Ventas_Semana.txt", CrearReporteVentas(sales));
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
        }

        private void ExportarProductos_Click(object sender, RoutedEventArgs e)
        {
            try { ExportarArchivo("Catalogo_Productos.txt", CrearReporteProductos(ProductoDB.Load())); }
            catch (Exception exception) { MostrarErrorExportacion(exception); }
        }

        private void ExportarPrestamos_Click(object sender, RoutedEventArgs e)
        {
            try { ExportarArchivo("Libreta_Prestamos.txt", CrearReportePrestamos(PrestamoDB.Load())); }
            catch (Exception exception) { MostrarErrorExportacion(exception); }
        }

        private void ExportarArchivo(string suggestedName, string content)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Guardar reporte",
                Filter = "Archivo de texto (*.txt)|*.txt",
                DefaultExt = ".txt",
                AddExtension = true,
                FileName = suggestedName
            };
            try
            {
                if (dialog.ShowDialog(this) == true)
                {
                    File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
                    ToastNotification.Show("Reporte exportado exitosamente", ToastType.Success);
                }
            }
            catch (Exception exception)
            {
                ToastNotification.Show($"Error al exportar reporte: {exception.Message}", ToastType.Error);
            }
            finally
            {
                CerrarExportar();
            }
        }

        private static string CrearReporteVentas(IReadOnlyCollection<Venta> sales)
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var today = DateTime.Today;
            var weekStart = today.AddDays(-6);
            var weeklySales = sales.Where(sale =>
            {
                var localDate = sale.FechaHora.ToLocalTime().Date;
                return sale.Estado != "Cancelada" && localDate >= weekStart && localDate <= today;
            }).ToList();

            var report = new StringBuilder();
            report.AppendLine("            RESUMEN DE SEMANA");
            report.AppendLine("######################################");

            var totalWeek = 0m;
            var totalProfitWeek = 0m;

            for (var day = weekStart; day <= today; day = day.AddDays(1))
            {
                var dailySales = weeklySales.Where(sale => sale.FechaHora.ToLocalTime().Date == day).ToList();
                var dailyTotal = dailySales.Sum(sale => sale.Total);
                var dailyProfit = dailySales.Sum(sale => sale.GananciaTotal);

                totalWeek += dailyTotal;
                totalProfitWeek += dailyProfit;

                report.AppendLine($"Día: {day.ToString("dd/MM/yyyy", culture)}");
                report.AppendLine("     Productos vendidos:");

                var itemsDelDia = new List<(string Nombre, int Cantidad, decimal Subtotal)>();
                foreach (var sale in dailySales)
                {
                    if (sale.Productos.Count > 0)
                    {
                        itemsDelDia.AddRange(sale.Productos.Select(p => (p.Nombre, p.Cantidad, p.Subtotal)));
                    }
                    else if (!string.IsNullOrWhiteSpace(sale.ProductosResumen))
                    {
                        foreach (var part in sale.ProductosResumen.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(part, @"^(?<name>.+?)\s+x(?<qty>\d+)$");
                            if (match.Success) itemsDelDia.Add((match.Groups["name"].Value.Trim(), int.Parse(match.Groups["qty"].Value), 0m));
                            else itemsDelDia.Add((part.Trim(), 1, 0m));
                        }
                    }
                }

                var agrupados = itemsDelDia.GroupBy(i => i.Nombre, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key);
                foreach (var g in agrupados)
                {
                    var cant = g.Sum(x => x.Cantidad);
                    var sub = g.Sum(x => x.Subtotal);
                    var precioStr = sub > 0 ? $" | {sub.ToString("C2", culture)}" : string.Empty;
                    report.AppendLine($"        - {g.Key} (x{cant}){precioStr}");
                }

                if (!itemsDelDia.Any()) report.AppendLine("        (Sin ventas registradas)");
                report.AppendLine($"     Total del día: {dailyTotal.ToString("C2", culture)} | Ganancia del día: {dailyProfit.ToString("C2", culture)}");
                report.AppendLine("======================================");
            }
            report.AppendLine($"GRAN TOTAL SEMANAL: {totalWeek.ToString("C2", culture)}");
            report.AppendLine($"GANANCIA TOTAL SEMANAL: {totalProfitWeek.ToString("C2", culture)}");
            report.AppendLine("--------------------------------------");
            return report.ToString();
        }

        private static string CrearReporteProductos(IEnumerable<Producto> products)
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var productList = products.OrderBy(product => product.Id).ToList();
            var report = new StringBuilder();
            report.AppendLine("                      CATÁLOGO DE PRODUCTOS");
            report.AppendLine("###########################################################################################");
            report.AppendLine("ID   | CÓDIGO         | NOMBRE                        | STOCK | COSTO     | GANANCIA  | PRECIO");
            report.AppendLine("-------------------------------------------------------------------------------------------");
            foreach (var product in productList)
            {
                report.AppendLine($"{product.Id,-4} | {product.Codigo,-14} | {product.Nombre,-29} | {product.Stock,-5} | {product.Costo.ToString("C2", culture),-9} | {product.Ganancia.ToString("C2", culture),-9} | {product.Precio.ToString("C2", culture)}");
                report.AppendLine("-------------------------------------------------------------------------------------------");
            }
            report.AppendLine("===========================================================================================");
            report.AppendLine($"Total de productos registrados: {productList.Count}");
            return report.ToString();
        }

        private static string CrearReportePrestamos(IEnumerable<Deudor> debtors)
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var debtorList = debtors.OrderBy(debtor => debtor.Id).ToList();
            var report = new StringBuilder();
            report.AppendLine("            REPORTE DE PRÉSTAMOS Y DEUDAS");
            report.AppendLine("######################################");
            foreach (var debtor in debtorList)
            {
                report.AppendLine($"ID: {debtor.Id}");
                report.AppendLine($"Deudor: {debtor.Nombre}");
                report.AppendLine($"Concepto: {debtor.Concepto}");
                report.AppendLine($"Total Deuda: {debtor.MontoOriginal.ToString("C2", culture)} | Abonado: {debtor.TotalAbonado.ToString("C2", culture)} | Saldo Restante: {debtor.SaldoRestante.ToString("C2", culture)}");
                report.AppendLine($"Estado: {(debtor.EstaSaldada ? "SALDADA" : "PENDIENTE")}");
                report.AppendLine("----------------------------------------------------------------------");
            }
            report.AppendLine("======================================================================");
            report.AppendLine($"TOTAL SALDO PENDIENTE POR COBRAR: {debtorList.Sum(debtor => debtor.SaldoRestante).ToString("C2", culture)}");
            return report.ToString();
        }

        private void MostrarErrorExportacion(Exception exception)
        {
            CerrarExportar();
            ToastNotification.Show($"No se pudo generar el reporte: {exception.Message}", ToastType.Error);
        }

        private void CerrarExportar()
        {
            if (FindName("ExportarOverlay") is Grid overlay) overlay.Visibility = Visibility.Collapsed;
        }

        private bool SolicitarAutorizacion()
        {
            var config = AppConfig.Load();
            if (!config.SolicitarPinAdmin) return true;

            var modal = new AutorizacionModal { Owner = this };
            modal.ShowDialog();
            return modal.Autorizado;
        }
    }
}