using Microsoft.Win32;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
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
    /// <summary>
    /// Lógica de interacción para MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _clockTimer;
        private bool _isShuttingDown;

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

        // Navegación a Módulos Principales
        private void Cobro_Click(object sender, RoutedEventArgs e) => ShowSection(new Cobro());

        private void Inventario_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion())
            {
                ShowSection(new Inventario());
            }
        }

        private void Ventas_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion())
            {
                ShowSection(new Ventas());
            }
        }

        private void Prestamos_Click(object sender, RoutedEventArgs e) => ShowSection(new Prestamos());

        private void Graficas_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion())
            {
                ShowSection(new Graficas());
            }
        }

        private void Configuracion_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion())
            {
                ShowSection(new Configuracion());
            }
        }

        private void Actualizar_Click(object sender, RoutedEventArgs e) => Configuracion_Click(sender, e);

        // ■ NUEVO: Apertura de la ventana de Promociones protegida con PIN
        private void Promociones_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion())
            {
                ShowSection(new Promociones());
            }
        }

        private void ShowSection(Window section)
        {
            var isMaximized = WindowState == WindowState.Maximized;
            section.Owner = this;

            if (isMaximized)
            {
                section.WindowState = WindowState.Maximized;
            }
            else
            {
                section.WindowStartupLocation = WindowStartupLocation.Manual;
                section.Left = Left;
                section.Top = Top;
                section.Width = Width;
                section.Height = Height;
                section.WindowState = WindowState.Normal;
            }

            Hide();

            try
            {
                section.ShowDialog();
            }
            finally
            {
                if (section.WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Maximized;
                }
                else
                {
                    WindowState = WindowState.Normal;
                    Left = section.Left;
                    Top = section.Top;
                    Width = section.Width;
                    Height = section.Height;
                }

                Show();
                Activate();
                Focus();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ExportarOverlay.Visibility == Visibility.Visible)
            {
                return;
            }

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

                // Atajo 8 para abrir Promociones directamente desde teclado
                case Key.D8:
                    Promociones_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }

        #region Importación y Carga de Catálogo
        private void BtnSubirLista_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar lista de productos"
            };

            if (openFileDialog.ShowDialog() != true) return;

            try
            {
                string carpetaApp = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MiscelaneaJaz"
                );
                Directory.CreateDirectory(carpetaApp);
                string rutaDestino = Path.Combine(carpetaApp, "productos.txt");

                var lineasNuevas = File.ReadAllLines(openFileDialog.FileName);
                var productosExistentes = File.Exists(rutaDestino)
                    ? File.ReadAllLines(rutaDestino).ToList()
                    : new List<string>();

                var catalogoPorId = new Dictionary<int, Producto>();

                foreach (var linea in productosExistentes)
                {
                    if (string.IsNullOrWhiteSpace(linea)) continue;
                    var prod = ParsearLineaProducto(linea);
                    if (prod != null && prod.Id > 0)
                    {
                        catalogoPorId[prod.Id] = prod;
                    }
                }

                int maxId = catalogoPorId.Keys.DefaultIfEmpty(0).Max();

                foreach (var linea in lineasNuevas)
                {
                    if (string.IsNullOrWhiteSpace(linea)) continue;
                    var prod = ParsearLineaProducto(linea);
                    if (prod == null) continue;

                    if (prod.Id > 0 && catalogoPorId.ContainsKey(prod.Id))
                    {
                        catalogoPorId[prod.Id] = prod;
                    }
                    else if (prod.Id <= 0 || catalogoPorId.ContainsKey(prod.Id))
                    {
                        maxId++;
                        prod.Id = maxId;
                        catalogoPorId[prod.Id] = prod;
                    }
                    else
                    {
                        if (prod.Id > maxId) maxId = prod.Id;
                        catalogoPorId[prod.Id] = prod;
                    }
                }

                var lineasAGuardar = catalogoPorId.Values
                    .OrderBy(p => p.Id)
                    .Select(p => string.Join("//",
                        p.Id.ToString(CultureInfo.InvariantCulture),
                        p.Codigo.Trim(),
                        p.Nombre.Trim(),
                        string.IsNullOrWhiteSpace(p.Categoria) ? "N/A" : p.Categoria.Trim(),
                        p.Costo.ToString("0.00", CultureInfo.InvariantCulture),
                        p.Ganancia.ToString("0.00", CultureInfo.InvariantCulture),
                        p.Precio.ToString("0.00", CultureInfo.InvariantCulture),
                        p.Stock.ToString(CultureInfo.InvariantCulture),
                        p.PedirCantidadAlCobrar.ToString().ToLowerInvariant()));

                SafeFileStore.WriteAllLinesAtomic(rutaDestino, lineasAGuardar, Encoding.UTF8);
                ToastNotification.Show($"Se guardaron {catalogoPorId.Count} productos correctamente", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al cargar archivo: {ex.Message}", ToastType.Error);
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
            decimal costo = 0m;
            decimal ganancia = 0m;
            decimal precio = 0m;
            int stock = 0;
            bool pedirCantidad = false;

            if (partes.Length >= 9)
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
                PedirCantidadAlCobrar = pedirCantidad
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
        #endregion

        #region Módulo de Exportación de Reportes
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

                var sales = VentaFileStore.Load()
                    .Where(sale =>
                    {
                        var fechaLocal = sale.FechaHora.ToLocalTime().Date;
                        return sale.Estado != "Cancelada" && fechaLocal >= inicioSemana && fechaLocal <= hoy;
                    })
                    .ToList();

                ExportarArchivo("Reporte_Ventas_Semana.txt", CrearReporteVentas(sales));
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
        }

        private void ExportarProductos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportarArchivo("Catalogo_Productos.txt", CrearReporteProductos(ProductoFileStore.Load()));
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
        }

        private void ExportarPrestamos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportarArchivo("Libreta_Prestamos.txt", CrearReportePrestamos(PrestamoFileStore.Load()));
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
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
            var weeklySales = sales
                .Where(sale =>
                {
                    var localDate = sale.FechaHora.ToLocalTime().Date;
                    return sale.Estado != "Cancelada" && localDate >= weekStart && localDate <= today;
                })
                .ToList();

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
                            if (match.Success)
                            {
                                itemsDelDia.Add((match.Groups["name"].Value.Trim(), int.Parse(match.Groups["qty"].Value), 0m));
                            }
                            else
                            {
                                itemsDelDia.Add((part.Trim(), 1, 0m));
                            }
                        }
                    }
                }

                var agrupados = itemsDelDia
                    .GroupBy(i => i.Nombre, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key);

                foreach (var g in agrupados)
                {
                    var cant = g.Sum(x => x.Cantidad);
                    var sub = g.Sum(x => x.Subtotal);
                    var precioStr = sub > 0 ? $" | {sub.ToString("C2", culture)}" : string.Empty;
                    report.AppendLine($"        - {g.Key} (x{cant}){precioStr}");
                }

                if (!itemsDelDia.Any())
                {
                    report.AppendLine("        (Sin ventas registradas)");
                }

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

        private bool SolicitarAutorizacion()
        {
            var modal = new AutorizacionModal
            {
                Owner = this
            };
            modal.ShowDialog();
            return modal.Autorizado;
        }

        private void MostrarErrorExportacion(Exception exception)
        {
            CerrarExportar();
            ToastNotification.Show($"No se pudo generar el reporte: {exception.Message}", ToastType.Error);
        }

        private void CerrarExportar()
        {
            if (FindName("ExportarOverlay") is Grid overlay)
            {
                overlay.Visibility = Visibility.Collapsed;
            }
        }
        #endregion
    }
}