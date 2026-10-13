using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public partial class Ventas : Window
    {
        private readonly DispatcherTimer _clockTimer;
        private readonly List<Producto> _catalog = [];
        private List<MovimientoCaja> _movimientos = [];
        private Venta? _selectedSale;
        private bool _loadFailed;

        // Rango de fechas seleccionado (Día, Semana, Mes, Custom)
        private string _periodoSeleccionado = "Dia";
        private DateTime _filtroInicio;
        private DateTime _filtroFin;

        public ObservableCollection<Venta> Sales { get; } = [];
        public ICollectionView SalesView { get; }

        public Ventas()
        {
            InitializeComponent();
            Language = XmlLanguage.GetLanguage("es-MX");

            SalesView = CollectionViewSource.GetDefaultView(Sales);

            // 1. ORDEN DESCENDENTE: Las más recientes SIEMPRE arriba
            SalesView.SortDescriptions.Clear();
            SalesView.SortDescriptions.Add(new SortDescription(nameof(Venta.FechaHora), ListSortDirection.Descending));
            SalesView.SortDescriptions.Add(new SortDescription(nameof(Venta.Id), ListSortDirection.Descending));

            // Filtro combinado de periodo temporal + texto de búsqueda
            SalesView.Filter = MatchesFilter;
            DataContext = this;

            ActualizarRangoPeriodo("Dia");

            // 1. Cargar catálogo para enriquecer ganancias
            try
            {
                _catalog = ProductoFileStore.Load();
            }
            catch
            {
                _catalog = [];
            }

            // 2. Cargar movimientos de caja para gastos
            CargarMovimientosCaja();

            // 3. Cargar ventas e insertar en orden descendente
            try
            {
                var loadedSales = VentaFileStore.Load()
                    .OrderByDescending(s => s.FechaHora)
                    .ThenByDescending(s => s.Id);

                foreach (var sale in loadedSales)
                {
                    HydrateSaleProfit(sale);
                    Sales.Add(sale);
                }
            }
            catch (Exception exception)
            {
                _loadFailed = true;
                MessageBox.Show(this, exception.Message, "No se pudo cargar el historial", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (FindName("ReturnButton") is Button returnBtn)
            {
                returnBtn.IsEnabled = false;
            }

            ActualizarEstiloBotonesPeriodo();
            RefreshMetrics();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) =>
            {
                if (FindName("ClockText") is TextBlock clock)
                {
                    clock.Text = DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
                }
            };
            _clockTimer.Start();
            Closed += (_, _) => _clockTimer.Stop();
        }

        private void CargarMovimientosCaja()
        {
            try
            {
                _movimientos = MovimientoCajaFileStore.Load() ?? [];
            }
            catch
            {
                _movimientos = [];
            }
        }

        // ========================================================
        // FILTROS TEMPORALES ESTILO GRÁFICAS (Día, Semana, Mes, Custom)
        // ========================================================
        public void BtnPeriodo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string periodo)
            {
                _periodoSeleccionado = periodo;
                ActualizarRangoPeriodo(periodo);
                ActualizarEstiloBotonesPeriodo();

                if (FindName("CustomDatePanel") is FrameworkElement customPanel)
                {
                    customPanel.Visibility = periodo == "Custom" ? Visibility.Visible : Visibility.Collapsed;
                }

                SalesView.Refresh();
                RefreshMetrics();
            }
        }

        public void DatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_periodoSeleccionado == "Custom")
            {
                var dpDesde = FindName("DatePickerDesde") as DatePicker;
                var dpHasta = FindName("DatePickerHasta") as DatePicker;

                _filtroInicio = dpDesde?.SelectedDate?.Date ?? DateTime.Today;
                _filtroFin = (dpHasta?.SelectedDate?.Date ?? DateTime.Today).AddDays(1).AddTicks(-1);

                SalesView.Refresh();
                RefreshMetrics();
            }
        }

        private void ActualizarRangoPeriodo(string periodo)
        {
            var ahora = DateTime.Now;

            switch (periodo)
            {
                case "Dia":
                    _filtroInicio = DateTime.Today;
                    _filtroFin = DateTime.Today.AddDays(1).AddTicks(-1);
                    break;

                case "Semana":
                    var diff = (7 + (ahora.DayOfWeek - DayOfWeek.Monday)) % 7;
                    _filtroInicio = ahora.Date.AddDays(-1 * diff);
                    _filtroFin = _filtroInicio.AddDays(7).AddTicks(-1);
                    break;

                case "Mes":
                    _filtroInicio = new DateTime(ahora.Year, ahora.Month, 1);
                    _filtroFin = _filtroInicio.AddMonths(1).AddTicks(-1);
                    break;

                case "Custom":
                    var dpDesde = FindName("DatePickerDesde") as DatePicker;
                    var dpHasta = FindName("DatePickerHasta") as DatePicker;
                    _filtroInicio = dpDesde?.SelectedDate?.Date ?? DateTime.Today;
                    _filtroFin = (dpHasta?.SelectedDate?.Date ?? DateTime.Today).AddDays(1).AddTicks(-1);
                    break;
            }
        }

        private void ActualizarEstiloBotonesPeriodo()
        {
            SetPillStyle(FindName("BtnPeriodoDia") as Button, _periodoSeleccionado == "Dia");
            SetPillStyle(FindName("BtnPeriodoSemana") as Button, _periodoSeleccionado == "Semana");
            SetPillStyle(FindName("BtnPeriodoMes") as Button, _periodoSeleccionado == "Mes");
            SetPillStyle(FindName("BtnPeriodoCustom") as Button, _periodoSeleccionado == "Custom");
        }

        private static void SetPillStyle(Button? btn, bool active)
        {
            if (btn == null) return;
            if (active)
            {
                btn.SetResourceReference(Button.BackgroundProperty, "ThemeSurfaceBackground");
                btn.SetResourceReference(Button.ForegroundProperty, "ThemeAccent");
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.SetResourceReference(Button.ForegroundProperty, "ThemeMutedForeground");
            }
        }

        // ========================================================
        // FILTRADO COMBINADO (RANGO DE FECHAS + BÚSQUEDA)
        // ========================================================
        private bool MatchesFilter(object item)
        {
            if (item is not Venta sale)
            {
                return false;
            }

            // 1. Filtrado de fecha
            if (sale.FechaHora < _filtroInicio || sale.FechaHora > _filtroFin)
            {
                return false;
            }

            // 2. Filtrado por texto
            var searchBox = FindName("SearchBox") as TextBox;
            var query = searchBox?.Text.Trim() ?? string.Empty;

            if (query.Length == 0)
            {
                return true;
            }

            return sale.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   sale.FechaHora.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-MX")).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   sale.FechaHora.ToString("d", CultureInfo.GetCultureInfo("es-MX")).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   sale.ProductosResumen.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (FindName("SearchBox") is TextBox searchBox)
            {
                if (FindName("SearchPlaceholder") is TextBlock placeholder)
                {
                    placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            SalesView.Refresh();
        }

        // ========================================================
        // RECÁLCULO DINÁMICO DE TOTALES, GANANCIAS Y GASTOS (CORREGIDO)
        // ========================================================
        private void RefreshMetrics()
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");

            // Ventas del periodo seleccionado (excluyendo únicamente canceladas)
            var periodSales = Sales
                .Where(s => s.FechaHora >= _filtroInicio && s.FechaHora <= _filtroFin && !string.Equals(s.Estado, "Cancelada", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Total real vendido en el periodo (incluye Contado, Banco y Fiado legítimo)
            var totalVentas = periodSales.Sum(s => s.Total);
            var totalItems = periodSales.Sum(s => s.CantidadTotal);
            var totalProfit = periodSales.Sum(s => s.GananciaTotal);

            // Gastos / Salidas en el periodo seleccionado
            var totalGastos = _movimientos
                .Where(m => m.FechaHora >= _filtroInicio && m.FechaHora <= _filtroFin &&
                           (m.Tipo.Equals("Salida", StringComparison.OrdinalIgnoreCase) ||
                            m.Tipo.Equals("Gasto", StringComparison.OrdinalIgnoreCase)))
                .Sum(m => m.Monto);

            // Balance comercial = Total de ventas realizadas - Gastos
            var balanceNeto = totalVentas - totalGastos;

            if (FindName("TotalSalesTodayText") is TextBlock salesText)
            {
                salesText.Text = $"Total ventas: {totalVentas.ToString("C2", culture)}";
            }

            if (FindName("TotalProfitText") is TextBlock profitText)
            {
                profitText.Text = $"Ganancia: {totalProfit.ToString("C2", culture)}";
            }

            if (FindName("TotalProductsSoldText") is TextBlock itemsText)
            {
                itemsText.Text = $"Prod. vendidos: {totalItems}";
            }

            if (FindName("MetricGastosText") is TextBlock gastosText)
            {
                gastosText.Text = totalGastos.ToString("C2", culture);
            }

            if (FindName("MetricBalanceText") is TextBlock balanceText)
            {
                balanceText.Text = balanceNeto.ToString("C2", culture);
            }
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
                var parsedItems = ParseProductSummary(sale.ProductosResumen);
                foreach (var (name, quantity) in parsedItems)
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

        private void Window_Loaded(object sender, RoutedEventArgs e) => FocusSearch();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void FocusSearch()
        {
            if (FindName("SearchBox") is TextBox searchBox)
            {
                searchBox.Focus();
                Keyboard.Focus(searchBox);
            }
        }

        private void SalesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedSale = ((DataGrid)FindName("SalesGrid")).SelectedItem as Venta;
            if (FindName("ReturnButton") is Button returnBtn)
            {
                returnBtn.IsEnabled = !_loadFailed && (_selectedSale?.Estado == "Abierta" || _selectedSale?.Estado == "Fiado");
            }
        }

        private void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSale is null)
            {
                ToastNotification.Show("Selecciona una venta de la lista", ToastType.Warning);
                return;
            }

            string details;
            if (_selectedSale.Productos.Count > 0)
            {
                details = string.Join(Environment.NewLine, _selectedSale.Productos.Select(p =>
                    $"• {p.Nombre} — {p.Cantidad} pza(s) a {p.Precio.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))} (Ganancia: {(p.Ganancia * p.Cantidad).ToString("C2", CultureInfo.GetCultureInfo("es-MX"))})"));
            }
            else
            {
                var detailLines = ParseProductSummary(_selectedSale.ProductosResumen);
                details = detailLines.Count == 0
                    ? "No hay productos guardados en el resumen de esta venta."
                    : string.Join(Environment.NewLine, detailLines.Select(item => $"• {item.Name} — {item.Quantity} pieza(s)"));
            }

            var profitTotal = _selectedSale.GananciaTotal;
            var text = $"Venta #{_selectedSale.Id}\n" +
                       $"Fecha: {_selectedSale.FechaHora.ToString("dd/MM/yyyy hh:mm tt", CultureInfo.GetCultureInfo("es-MX"))}\n" +
                       $"Estado: {_selectedSale.Estado}\n\n" +
                       $"{details}\n\n" +
                       $"Total: {_selectedSale.Total.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}\n" +
                       $"Ganancia total de esta venta: {profitTotal.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}";

            MessageBox.Show(this, text, "Datos de venta", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ReturnButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || _selectedSale is null)
            {
                return;
            }

            if (_selectedSale.Estado == "Fiado")
            {
                var res = MessageBox.Show(this,
                    "Esta venta fue fiada. Al cancelarla se repondrá el inventario, pero recuerda ajustar la deuda manualmente en Préstamos si aplica.\n\n¿Deseas continuar?",
                    "Devolución de Fiado",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (res != MessageBoxResult.Yes) return;
            }

            if (_selectedSale.Estado != "Abierta" && _selectedSale.Estado != "Fiado")
            {
                ToastNotification.Show("Solo se pueden devolver ventas abiertas del turno o fiadas", ToastType.Warning);
                return;
            }

            var result = MessageBox.Show(this,
                "¿Deseas marcar esta venta como devuelta/cancelada?",
                "Confirmar devolución",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var previousState = _selectedSale.Estado;
            _selectedSale.Estado = "Cancelada";

            if (!TrySave())
            {
                _selectedSale.Estado = previousState;
                return;
            }

            try
            {
                var catalog = ProductoFileStore.Load();

                if (_selectedSale.Productos.Count > 0)
                {
                    foreach (var item in _selectedSale.Productos)
                    {
                        var prod = catalog.FirstOrDefault(p => p.Id == item.Id || string.Equals(p.Nombre, item.Nombre, StringComparison.OrdinalIgnoreCase));
                        if (prod != null)
                        {
                            prod.Stock += item.Cantidad;
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(_selectedSale.ProductosResumen))
                {
                    foreach (var (name, quantity) in ParseProductSummary(_selectedSale.ProductosResumen))
                    {
                        var prod = catalog.FirstOrDefault(p => string.Equals(p.Nombre, name, StringComparison.OrdinalIgnoreCase));
                        if (prod != null)
                        {
                            prod.Stock += quantity;
                        }
                    }
                }

                ProductoFileStore.Save(catalog);
                ToastNotification.Show($"Venta #{_selectedSale.Id} cancelada. Stock reincorporado", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al reponer inventario: {ex.Message}", ToastType.Error);
            }

            SalesView.Refresh();
            RefreshMetrics();
            if (FindName("ReturnButton") is Button returnBtn)
            {
                returnBtn.IsEnabled = false;
            }
        }

        // ■ Arrastre de ventana y alternancia de maximizado con doble clic
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

        // ■ Botones de ventana
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void CashCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite())
            {
                return;
            }

            var today = DateTime.Today;
            var culture = CultureInfo.GetCultureInfo("es-MX");

            // 1. DETERMINAR INICIO DEL TURNO ACTUAL
            // Si ya hubo un corte hoy, el turno actual inicia a partir de la fecha/hora de ese corte.
            var cortesDelDia = CorteCajaFileStore.Load()
                .Where(c => c.FechaHora.Date == today)
                .OrderByDescending(c => c.FechaHora)
                .ToList();

            DateTime inicioTurno = cortesDelDia.Count > 0 ? cortesDelDia.First().FechaHora : today;

            // 2. VENTAS DEL TURNO ACTUAL (Excluyendo canceladas)
            var turnoValidSales = Sales
                .Where(sale => sale.FechaHora >= inicioTurno && !string.Equals(sale.Estado, "Cancelada", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Ventas pendientes de cerrar
            var openSales = turnoValidSales
                .Where(sale => string.Equals(sale.Estado, "Abierta", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var turnoCreditSales = turnoValidSales
                .Where(sale => string.Equals(sale.Estado, "Fiado", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(sale.MetodoPago, "Fiado", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 3. AGRUPAR IMPORTES POR MÉTODO DE PAGO
            var cashSales = turnoValidSales
                .Where(s => !string.Equals(s.Estado, "Fiado", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(s.MetodoPago, "Efectivo", StringComparison.OrdinalIgnoreCase))
                .Sum(s => s.Total);

            var cardSales = turnoValidSales
                .Where(s => !string.Equals(s.Estado, "Fiado", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(s.MetodoPago, "Tarjeta", StringComparison.OrdinalIgnoreCase))
                .Sum(s => s.Total);

            var transferSales = turnoValidSales
                .Where(s => !string.Equals(s.Estado, "Fiado", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(s.MetodoPago, "Transferencia", StringComparison.OrdinalIgnoreCase))
                .Sum(s => s.Total);

            var creditSales = turnoCreditSales.Sum(s => s.Total);
            var profitTotal = turnoValidSales.Sum(s => s.GananciaTotal);

            // 4. MOVIMIENTOS DE CAJA DEL TURNO ACTUAL
            CargarMovimientosCaja();
            var turnoMovements = _movimientos
                .Where(m => m.FechaHora >= inicioTurno)
                .ToList();

            var fondo = turnoMovements
                .Where(m => string.Equals(m.Tipo, "Fondo", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Monto);

            var entradas = turnoMovements
                .Where(m => string.Equals(m.Tipo, "Entrada", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Monto);

            var salidas = turnoMovements
                .Where(m => string.Equals(m.Tipo, "Salida", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(m.Tipo, "Gasto", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Monto);

            // TOTAL EN CAJÓN FÍSICO (Efectivo neto esperado)
            var totalEnCajon = fondo + cashSales + entradas - salidas;

            if (turnoValidSales.Count == 0 && turnoMovements.Count == 0)
            {
                ToastNotification.Show("No hay ventas registradas ni movimientos en este turno", ToastType.Info);
                return;
            }

            // 5. ARQUEO CIEGO: Conteo físico
            var modalCiego = new CorteCiegoModal(totalEnCajon) { Owner = this };
            modalCiego.ShowDialog();

            if (!modalCiego.Realizado)
            {
                return;
            }

            var contadoFisico = modalCiego.TotalContadoResult;
            var diferencia = contadoFisico - totalEnCajon;
            var diferenciaTexto = diferencia == 0m
                ? "$0.00 (Exacto)"
                : (diferencia > 0m ? $"+{diferencia.ToString("C2", culture)} (Sobrante)" : $"{diferencia.ToString("C2", culture)} (Faltante)");

            // 6. RESUMEN DE AUDITORÍA
            var desglose = $"--- RESUMEN DE CORTE DE CAJA ---\n\n" +
                           $"Fondo Inicial:           {fondo.ToString("C2", culture)}\n" +
                           $"(+) Ventas en Efectivo:  {cashSales.ToString("C2", culture)}\n" +
                           $"(+) Entradas Extras:     {entradas.ToString("C2", culture)}\n" +
                           $"(-) Gastos / Retiros:    {salidas.ToString("C2", culture)}\n" +
                           $"---------------------------------\n" +
                           $"EFECTIVO EN SISTEMA:     {totalEnCajon.ToString("C2", culture)}\n" +
                           $"EFECTIVO CONTADO:        {contadoFisico.ToString("C2", culture)}\n" +
                           $"DIFERENCIA:              {diferenciaTexto}\n\n" +
                           $"--- BANCO / ELECTRÓNICO ---\n" +
                           $"Terminal / Tarjeta:      {cardSales.ToString("C2", culture)}\n" +
                           $"Transferencias:          {transferSales.ToString("C2", culture)}\n\n" +
                           $"[Ventas Fiadas Hoy]:     {creditSales.ToString("C2", culture)}\n" +
                           $"Ganancia total del día:  {profitTotal.ToString("C2", culture)}\n\n" +
                           $"¿Deseas confirmar el corte de caja?";

            var confirmation = MessageBox.Show(this, desglose, "Confirmar Cierre de Caja", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            // 7. PERSISTIR ARQUEO EN CORTES.TXT
            try
            {
                var cortes = CorteCajaFileStore.Load();
                var nextCorteId = cortes.Select(c => c.Id).DefaultIfEmpty(0).Max() + 1;
                BackupService.GenerarRespaldo("Corte");
                cortes.Add(new CorteCaja
                {
                    Id = nextCorteId,
                    FechaHora = DateTime.Now,
                    FondoInicial = fondo,
                    VentasEfectivo = cashSales,
                    EntradasEfectivo = entradas,
                    SalidasEfectivo = salidas,
                    TotalEsperado = totalEnCajon,
                    TotalContado = contadoFisico,
                    Observaciones = diferenciaTexto
                });

                CorteCajaFileStore.Save(cortes);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al registrar auditoría: {ex.Message}", ToastType.Error);
            }

            // 8. CIERRE DE VENTAS DEL TURNO
            if (openSales.Count > 0)
            {
                foreach (var sale in openSales)
                {
                    sale.Estado = "Cerrada";
                }

                if (!TrySave())
                {
                    foreach (var sale in openSales)
                    {
                        sale.Estado = "Abierta";
                    }
                    return;
                }
            }

            ToastNotification.Show("Corte de caja finalizado exitosamente", ToastType.Success);
            SalesView.Refresh();
            RefreshMetrics();
        }

        private bool CanWrite()
        {
            if (!_loadFailed)
            {
                return true;
            }

            ToastNotification.Show("No se encontró ventas.txt, cambios bloqueados", ToastType.Error);
            return false;
        }

        private bool TrySave()
        {
            try
            {
                VentaFileStore.Save(Sales);
                _loadFailed = false;
                return true;
            }
            catch (Exception exception)
            {
                ToastNotification.Show($"Error al guardar ventas: {exception.Message}", ToastType.Error);
                return false;
            }
        }

        private static List<(string Name, int Quantity)> ParseProductSummary(string summary)
        {
            var products = new List<(string Name, int Quantity)>();
            foreach (var part in summary.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(part, @"^(?<name>.+?)\s+x(?<quantity>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success && int.TryParse(match.Groups["quantity"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity))
                {
                    products.Add((match.Groups["name"].Value.Trim(), quantity));
                }
                else
                {
                    products.Add((part.Trim(), 1));
                }
            }

            return products;
        }
    }
}