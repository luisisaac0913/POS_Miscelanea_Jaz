using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Punto_de_Venta
{
    public partial class Graficas : Window, INotifyPropertyChanged
    {
        private readonly DispatcherTimer _clockTimer;
        private readonly List<Venta> _sales;
        private readonly List<Producto> _catalog;
        private readonly List<CorteCaja> _cortes;
        private readonly List<MovimientoCaja> _movimientos;
        private readonly CultureInfo _mexicanCulture = CultureInfo.GetCultureInfo("es-MX");

        private Period _currentPeriod = Period.Day;
        private ChartView _currentView = ChartView.Finanzas;

        public ISeries[] SeriesPrincipal { get; private set; } = [];
        public Axis[] EjesXPrincipal { get; private set; } = [];
        public Axis[] EjesYPrincipal { get; private set; } = [];

        public ObservableCollection<TopProductoItem> TopProductos { get; } = [];
        public ObservableCollection<CorteAuditoriaItem> CortesAuditoria { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;
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
        public Graficas()
        {
            InitializeComponent();
            DataContext = this;

            try
            {
                _catalog = ProductoFileStore.Load() ?? [];
            }
            catch
            {
                _catalog = [];
            }

            try
            {
                _sales = VentaFileStore.Load()
                    .Where(s => s.Estado != "Cancelada")
                    .ToList();

                foreach (var sale in _sales)
                {
                    HydrateSaleProfit(sale);
                }
            }
            catch (Exception ex)
            {
                _sales = [];
                ToastNotification.Show($"Error al cargar ventas: {ex.Message}", ToastType.Error);
            }

            try
            {
                _cortes = CorteCajaFileStore.Load() ?? [];
            }
            catch
            {
                _cortes = [];
            }

            try
            {
                _movimientos = MovimientoCajaFileStore.Load() ?? [];
            }
            catch
            {
                _movimientos = [];
            }

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
            _clockTimer.Start();
            Closed += (_, _) => _clockTimer.Stop();

            RefreshData();
        }

        private void HydrateSaleProfit(Venta sale)
        {
            if (sale.Productos.Count > 0)
            {
                foreach (var item in sale.Productos)
                {
                    if (item.Ganancia <= 0)
                    {
                        var catalogProd = _catalog.FirstOrDefault(p =>
                            p.Id == item.Id ||
                            string.Equals(p.Nombre, item.Nombre, StringComparison.OrdinalIgnoreCase));

                        if (catalogProd is not null && catalogProd.Ganancia > 0)
                        {
                            item.Costo = catalogProd.Costo;
                            item.Ganancia = catalogProd.Ganancia;
                        }
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(sale.ProductosResumen))
            {
                foreach (var (name, quantity) in ParseLegacySummary(sale.ProductosResumen))
                {
                    var catalogProd = _catalog.FirstOrDefault(p => string.Equals(p.Nombre, name, StringComparison.OrdinalIgnoreCase));
                    var cost = catalogProd?.Costo ?? 0m;
                    var margin = catalogProd?.Ganancia ?? 0m;
                    var price = catalogProd?.Precio ?? (sale.Total > 0 && sale.CantidadTotal > 0 ? sale.Total / sale.CantidadTotal : 0m);

                    sale.Productos.Add(new DetalleVenta
                    {
                        Id = catalogProd?.Id ?? 0,
                        VentaId = sale.Id,
                        Nombre = name,
                        Cantidad = quantity,
                        Costo = cost,
                        Ganancia = margin,
                        Precio = price,
                        Categoria = catalogProd?.Categoria ?? "N/A"
                    });
                }
            }

            sale.NotifyTotalesChanged();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) => DayButton.Focus();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (HelpOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
            {
                HelpOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        #region Apartado de Ayuda
        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpOverlay.Visibility = Visibility.Visible;
            HelpOverlay.Focus();
        }

        private void CloseHelp_Click(object sender, RoutedEventArgs e)
        {
            HelpOverlay.Visibility = Visibility.Collapsed;
        }

        private void HelpOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, HelpOverlay))
            {
                HelpOverlay.Visibility = Visibility.Collapsed;
            }
        }
        #endregion

        private void DayButton_Click(object sender, RoutedEventArgs e)
        {
            _currentPeriod = Period.Day;
            RefreshData();
        }

        private void WeekButton_Click(object sender, RoutedEventArgs e)
        {
            _currentPeriod = Period.Week;
            RefreshData();
        }

        private void MonthButton_Click(object sender, RoutedEventArgs e)
        {
            _currentPeriod = Period.Month;
            RefreshData();
        }

        private void YearButton_Click(object sender, RoutedEventArgs e)
        {
            _currentPeriod = Period.Year;
            RefreshData();
        }

        private void NavFinanzas_Click(object sender, RoutedEventArgs e)
        {
            _currentView = ChartView.Finanzas;
            RefreshData();
        }

        private void NavMetodos_Click(object sender, RoutedEventArgs e)
        {
            _currentView = ChartView.MetodosPago;
            RefreshData();
        }

        private void NavCategorias_Click(object sender, RoutedEventArgs e)
        {
            _currentView = ChartView.Categorias;
            RefreshData();
        }

        private void NavCortesBtn_Click(object sender, RoutedEventArgs e)
        {
            _currentView = ChartView.CortesCaja;
            RefreshData();
        }

        private void RefreshData()
        {
            var today = DateTime.Today;
            var start = _currentPeriod switch
            {
                Period.Day => today,
                Period.Week => today.AddDays(-6),
                Period.Month => new DateTime(today.Year, today.Month, 1),
                Period.Year => new DateTime(today.Year, 1, 1),
                _ => today
            };

            var selectedSales = _sales.Where(s =>
            {
                var fecha = s.FechaHora.Date;
                return fecha >= start && fecha <= today;
            }).ToList();

            var selectedCortes = _cortes.Where(c =>
            {
                var fecha = c.FechaHora.Date;
                return fecha >= start && fecha <= today;
            }).ToList();

            // 1. CÁLCULO DE KPIS CON GASTOS Y BALANCE NETO
            var totalVentas = selectedSales.Sum(s => s.Total);
            var totalGanancia = selectedSales.Sum(s => s.GananciaTotal);
            var totalCosto = Math.Max(0m, totalVentas - totalGanancia);
            var transacciones = selectedSales.Count;
            var ticketPromedio = transacciones > 0 ? totalVentas / transacciones : 0m;
            var margenPct = totalVentas > 0 ? (totalGanancia / totalVentas) * 100m : 0m;

            var totalGastos = _movimientos
                .Where(m => m.FechaHora.Date >= start && m.FechaHora.Date <= today &&
                           (m.Tipo.Equals("Salida", StringComparison.OrdinalIgnoreCase) ||
                            m.Tipo.Equals("Gasto", StringComparison.OrdinalIgnoreCase)))
                .Sum(m => m.Monto);

            var balanceNeto = totalVentas - totalGastos;

            KpiVentaTotal.Text = totalVentas.ToString("C2", _mexicanCulture);
            KpiCostoTotal.Text = totalCosto.ToString("C2", _mexicanCulture);
            KpiGananciaNeta.Text = totalGanancia.ToString("C2", _mexicanCulture);
            KpiGastosTotal.Text = totalGastos.ToString("C2", _mexicanCulture);
            KpiBalanceNeto.Text = balanceNeto.ToString("C2", _mexicanCulture);

            KpiTransacciones.Text = $"{transacciones} venta(s)";
            KpiTicketPromedio.Text = $"Ticket prom: {ticketPromedio.ToString("C2", _mexicanCulture)}";
            KpiMargenPorcentaje.Text = $"Margen: {margenPct:F1}%";


            // 3. ACTUALIZAR ESTILOS DE BOTONES
            ActualizarEstiloBotonPeriodo(DayButton, _currentPeriod == Period.Day);
            ActualizarEstiloBotonPeriodo(WeekButton, _currentPeriod == Period.Week);
            ActualizarEstiloBotonPeriodo(MonthButton, _currentPeriod == Period.Month);
            ActualizarEstiloBotonPeriodo(YearButton, _currentPeriod == Period.Year);

            ActualizarEstiloBotonSidebar(NavFinanzasBtn, _currentView == ChartView.Finanzas);
            ActualizarEstiloBotonSidebar(NavMetodosBtn, _currentView == ChartView.MetodosPago);
            ActualizarEstiloBotonSidebar(NavCategoriasBtn, _currentView == ChartView.Categorias);
            ActualizarEstiloBotonSidebar(NavCortesBtn, _currentView == ChartView.CortesCaja);

            // 4. VISIBILIDAD GRÁFICA VS TABLA AUDITORÍA
            if (_currentView == ChartView.CortesCaja)
            {
                MainChartView.Visibility = Visibility.Collapsed;
                CortesContainerView.Visibility = Visibility.Visible;
                CargarTablaCortes(selectedCortes);
            }
            else
            {
                MainChartView.Visibility = Visibility.Visible;
                CortesContainerView.Visibility = Visibility.Collapsed;

                switch (_currentView)
                {
                    case ChartView.Finanzas:
                        CargarGraficaFinanciera(totalVentas, totalCosto, totalGanancia, totalGastos);
                        break;
                    case ChartView.MetodosPago:
                        CargarGraficaMetodosPago(selectedSales);
                        break;
                    case ChartView.Categorias:
                        CargarGraficaCategorias(selectedSales);
                        break;
                }
            }

            // 5. ACTUALIZAR TOP PRODUCTOS
            CargarTopProductos(selectedSales);
        }

        private void CargarGraficaFinanciera(decimal venta, decimal costo, decimal ganancia, decimal gastos)
        {
            ChartTitleText.Text = "Venta Total vs Costo vs Ganancia vs Gastos";

            SeriesPrincipal =
            [
                new ColumnSeries<double>
                {
                    Name = "Venta Total",
                    Values = [(double)venta],
                    Fill = new SolidColorPaint(new SKColor(0, 122, 255)),
                    MaxBarWidth = 40
                },
                new ColumnSeries<double>
                {
                    Name = "Inversion",
                    Values = [(double)costo],
                    Fill = new SolidColorPaint(new SKColor(142, 142, 147)),
                    MaxBarWidth = 40
                },
                new ColumnSeries<double>
                {
                    Name = "Ganancia Bruta",
                    Values = [(double)ganancia],
                    Fill = new SolidColorPaint(new SKColor(52, 199, 89)),
                    MaxBarWidth = 40
                },
                new ColumnSeries<double>
                {
                    Name = "Gastos de Caja",
                    Values = [(double)gastos],
                    Fill = new SolidColorPaint(new SKColor(239, 68, 68)),
                    MaxBarWidth = 40
                }
            ];

            EjesXPrincipal =
            [
                new Axis
                {
                    Labels = ["Total del Periodo"],
                    LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()),
                    TextSize = 13
                }
            ];

            EjesYPrincipal =
            [
                new Axis
                {
                    MinLimit = 0,
                    Labeler = v => v.ToString("C0", _mexicanCulture),
                    LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()),
                    TextSize = 12
                }
            ];

            NotifyChartProperties();
        }

        private void CargarGraficaMetodosPago(List<Venta> sales)
        {
            ChartTitleText.Text = "Metodo de pago (Dinero recibido)";

            var efectivo = (double)sales.Where(s => s.MetodoPago == "Efectivo").Sum(s => s.Total);
            var tarjeta = (double)sales.Where(s => s.MetodoPago == "Tarjeta").Sum(s => s.Total);
            var transferencia = (double)sales.Where(s => s.MetodoPago == "Transferencia").Sum(s => s.Total);
            var fiado = (double)sales.Where(s => s.Estado == "Fiado" || s.MetodoPago == "Fiado").Sum(s => s.Total);

            SeriesPrincipal =
            [
                new ColumnSeries<double>
                {
                    Name = "Efectivo",
                    Values = [efectivo],
                    Fill = new SolidColorPaint(new SKColor(52, 199, 89)),
                    MaxBarWidth = 38
                },
                new ColumnSeries<double>
                {
                    Name = "Tarjeta",
                    Values = [tarjeta],
                    Fill = new SolidColorPaint(new SKColor(0, 122, 255)),
                    MaxBarWidth = 38
                },
                new ColumnSeries<double>
                {
                    Name = "Transferencia",
                    Values = [transferencia],
                    Fill = new SolidColorPaint(new SKColor(175, 82, 222)),
                    MaxBarWidth = 38
                },
                new ColumnSeries<double>
                {
                    Name = "Fiado",
                    Values = [fiado],
                    Fill = new SolidColorPaint(new SKColor(255, 149, 0)),
                    MaxBarWidth = 38
                }
            ];

            EjesXPrincipal =
            [
                new Axis
                {
                    Labels = ["Metodos de Pago"],
                    LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()),
                    TextSize = 13
                }
            ];

            EjesYPrincipal =
            [
                new Axis
                {
                    MinLimit = 0,
                    Labeler = v => v.ToString("C0", _mexicanCulture),
                    LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()),
                    TextSize = 12
                }
            ];

            NotifyChartProperties();
        }

        private void CargarGraficaCategorias(List<Venta> sales)
        {
            ChartTitleText.Text = "Productos mas vendidos";

            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in sales)
            {
                if (s.Productos.Count > 0)
                {
                    foreach (var p in s.Productos)
                    {
                        var cat = !string.IsNullOrWhiteSpace(p.Categoria) && p.Categoria != "N/A"
                            ? p.Categoria.Trim()
                            : (_catalog.FirstOrDefault(c => c.Id == p.Id)?.Categoria ?? "General");

                        dict[cat] = dict.GetValueOrDefault(cat) + p.Cantidad;
                    }
                }
                else
                {
                    foreach (var (name, qty) in ParseLegacySummary(s.ProductosResumen))
                    {
                        var cat = _catalog.FirstOrDefault(c => string.Equals(c.Nombre, name, StringComparison.OrdinalIgnoreCase))?.Categoria ?? "General";
                        dict[cat] = dict.GetValueOrDefault(cat) + qty;
                    }
                }
            }

            var sorted = dict.OrderByDescending(c => c.Value).Take(7).ToList();
            if (sorted.Count == 0) sorted.Add(new KeyValuePair<string, int>("Sin ventas", 0));

            SeriesPrincipal =
            [
                new ColumnSeries<double>
                {
                    Name = "Piezas Vendidas",
                    Values = sorted.Select(c => (double)c.Value).ToArray(),
                    Fill = new SolidColorPaint(new SKColor(0, 122, 255)),
                    MaxBarWidth = 44
                }
            ];

            EjesXPrincipal =
            [
                new Axis
                {
                    Labels = sorted.Select(c => c.Key).ToArray(),
                    LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()),
                    TextSize = 13
                }
            ];

            EjesYPrincipal =
            [
                new Axis
                {
                    MinLimit = 0,
                    LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()),
                    TextSize = 12
                }
            ];

            NotifyChartProperties();
        }

        private void CargarTablaCortes(List<CorteCaja> cortes)
        {
            ChartTitleText.Text = "Historial de Corte de Caja";
            CortesAuditoria.Clear();

            if (cortes.Count == 0)
            {
                return;
            }

            foreach (var c in cortes.OrderByDescending(x => x.FechaHora))
            {
                var dif = c.Diferencia;
                string difTexto;
                Brush bgBrush;
                Brush fgBrush;

                if (dif == 0m)
                {
                    difTexto = "$0.00 Exacto";
                    bgBrush = new SolidColorBrush(Color.FromArgb(30, 52, 199, 89));
                    fgBrush = new SolidColorBrush(Color.FromRgb(40, 160, 70));
                }
                else if (dif > 0m)
                {
                    difTexto = $"+{dif.ToString("C2", _mexicanCulture)} Sobrante";
                    bgBrush = new SolidColorBrush(Color.FromArgb(30, 255, 149, 0));
                    fgBrush = new SolidColorBrush(Color.FromRgb(215, 120, 0));
                }
                else
                {
                    difTexto = $"{dif.ToString("C2", _mexicanCulture)} Faltante";
                    bgBrush = new SolidColorBrush(Color.FromArgb(30, 255, 59, 48));
                    fgBrush = new SolidColorBrush(Color.FromRgb(215, 40, 30));
                }

                CortesAuditoria.Add(new CorteAuditoriaItem(
                    c.FechaHora.ToString("dd/MM/yyyy h:mm tt", CultureInfo.InvariantCulture),
                    $"Turno #{c.Id}",
                    c.TotalEsperado.ToString("C2", _mexicanCulture),
                    c.TotalContado.ToString("C2", _mexicanCulture),
                    difTexto,
                    bgBrush,
                    fgBrush
                ));
            }
        }

        private void CargarTopProductos(List<Venta> sales)
        {
            TopProductos.Clear();
            var dict = new Dictionary<string, (int Qty, decimal Ingreso)>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in sales)
            {
                if (s.Productos.Count > 0)
                {
                    foreach (var p in s.Productos)
                    {
                        var cur = dict.GetValueOrDefault(p.Nombre);
                        dict[p.Nombre] = (cur.Qty + p.Cantidad, cur.Ingreso + (p.Precio * p.Cantidad));
                    }
                }
                else
                {
                    foreach (var (name, qty) in ParseLegacySummary(s.ProductosResumen))
                    {
                        var cur = dict.GetValueOrDefault(name);
                        dict[name] = (cur.Qty + qty, cur.Ingreso);
                    }
                }
            }

            var top = dict.OrderByDescending(x => x.Value.Ingreso)
                          .ThenByDescending(x => x.Value.Qty)
                          .Take(5)
                          .ToList();

            for (int i = 0; i < top.Count; i++)
            {
                TopProductos.Add(new TopProductoItem(i + 1, top[i].Key, top[i].Value.Qty, top[i].Value.Ingreso));
            }

            for (int i = top.Count; i < 5; i++)
            {
                TopProductos.Add(new TopProductoItem(i + 1, "Sin datos", 0, 0m));
            }
        }

        private void ActualizarEstiloBotonPeriodo(Button btn, bool selected)
        {
            if (selected)
            {
                btn.SetResourceReference(Button.BackgroundProperty, "ThemeSurfaceBackground");
                btn.SetResourceReference(Button.ForegroundProperty, "ThemeTextForeground");
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.SetResourceReference(Button.ForegroundProperty, "ThemeMutedForeground");
            }
        }

        private void ActualizarEstiloBotonSidebar(Button btn, bool selected)
        {
            if (selected)
            {
                btn.SetResourceReference(Button.BackgroundProperty, "ThemeInputBackground");
                btn.SetResourceReference(Button.ForegroundProperty, "ThemeAccent");
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.SetResourceReference(Button.ForegroundProperty, "ThemeTextForeground");
            }
        }

        private SKColor ObtenerColorTextoEje()
        {
            if (Application.Current is App app && app.IsDarkMode)
            {
                return new SKColor(245, 245, 247);
            }
            return new SKColor(28, 28, 30);
        }

        private void NotifyChartProperties()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeriesPrincipal)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EjesXPrincipal)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EjesYPrincipal)));
        }

        private static IEnumerable<(string Name, int Quantity)> ParseLegacySummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) yield break;
            foreach (var entry in summary.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(entry, @"^(.*?)\s+x(\d+)$", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[2].Value, out var qty))
                {
                    yield return (match.Groups[1].Value.Trim(), qty);
                }
                else
                {
                    yield return (entry.Trim(), 1);
                }
            }
        }

        private enum Period { Day, Week, Month, Year }
        private enum ChartView { Finanzas, MetodosPago, Categorias, CortesCaja }
    }

    public sealed record TopProductoItem(int Posicion, string Nombre, int Cantidad, decimal Ingreso);

    public sealed record CorteAuditoriaItem(
        string FechaHoraFormateada,
        string FolioTexto,
        string EsperadoFormateado,
        string ContadoFormateado,
        string DiferenciaFormateada,
        Brush BadgeBackground,
        Brush BadgeForeground
    );
}