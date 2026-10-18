using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public partial class Prestamos : Window
    {
        private readonly DispatcherTimer _clockTimer;
        private Deudor? _selectedDebtor;
        private bool _loadFailed;
        private bool _successStatusVisible;

        public ObservableCollection<Deudor> Debtors { get; } = [];
        public ICollectionView DebtorsView { get; }

        public Prestamos()
        {
            InitializeComponent();
            Language = XmlLanguage.GetLanguage("es-MX");
            DebtorsView = CollectionViewSource.GetDefaultView(Debtors);
            DebtorsView.Filter = MatchesSearch;
            DataContext = this;

            try
            {
                foreach (var debtor in PrestamoFileStore.Load())
                {
                    Debtors.Add(debtor);
                }
            }
            catch (Exception exception)
            {
                _loadFailed = true;
                ToastNotification.Show($"Error al cargar la libreta: {exception.Message}", ToastType.Error);
            }

            UpdateTotalLent();
            SetNextId();
            UpdateSelectionButtons();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
            _clockTimer.Start();
            Closed += (_, _) => _clockTimer.Stop();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) => FocusSearch();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (HistorialOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    HistorialOverlay.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && _successStatusVisible)
            {
                StatusText.Text = string.Empty;
                _successStatusVisible = false;
                e.Handled = true;
            }
        }

        #region Salto Secuencial de Campos con Enter
        private void DebtorField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (sender == DebtorNameBox)
            {
                DebtorPhoneBox.Focus();
                DebtorPhoneBox.SelectAll();
            }
            else if (sender == DebtorPhoneBox)
            {
                ConceptBox.Focus();
                ConceptBox.SelectAll();
            }
            else if (sender == ConceptBox)
            {
                AmountBox.Focus();
                AmountBox.SelectAll();
            }
            else if (sender == AmountBox)
            {
                AddButton_Click(this, new RoutedEventArgs());
            }

            e.Handled = true;
        }

        private void PaymentAmountBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyPayment();
                e.Handled = true;
            }
        }
        #endregion

        private void FocusSearch()
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }

        private bool MatchesSearch(object item)
        {
            if (item is not Deudor debtor) return false;

            var query = SearchBox.Text.Trim();
            return query.Length == 0 ||
                   debtor.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   debtor.Nombre.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   debtor.Telefono.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   debtor.Concepto.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            DebtorsView.Refresh();
        }

        private void DebtorGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedDebtor = DebtorGrid.SelectedItem as Deudor;
            UpdateSelectionButtons();
            StatusText.Text = string.Empty;
            _successStatusVisible = false;

            if (_selectedDebtor is null)
            {
                ClearForm();
                SetNextId();
                return;
            }

            DebtorIdBox.Text = _selectedDebtor.Id.ToString(CultureInfo.InvariantCulture);
            DebtorNameBox.Text = _selectedDebtor.Nombre;
            DebtorPhoneBox.Text = _selectedDebtor.Telefono;
            ConceptBox.Text = _selectedDebtor.Concepto;
            AmountBox.Text = _selectedDebtor.MontoOriginal.ToString("0.00", CultureInfo.InvariantCulture);
            PaymentAmountBox.Clear();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || !TryReadLoanForm(out var name, out var concept, out var amount))
            {
                return;
            }

            var phone = DebtorPhoneBox.Text.Trim();

            var pendingDebtor = Debtors
                .Where(debtor => string.Equals(debtor.Nombre, name, StringComparison.OrdinalIgnoreCase) && debtor.SaldoRestante > 0)
                .OrderByDescending(debtor => debtor.Id)
                .FirstOrDefault();

            if (pendingDebtor is not null)
            {
                var updatedDebtor = CopyDebtor(pendingDebtor);
                updatedDebtor.MontoOriginal += amount;
                updatedDebtor.Concepto = CombineConcepts(updatedDebtor.Concepto, concept);
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    updatedDebtor.Telefono = phone;
                }

                var updatedDebtors = Debtors.Select(debtor => debtor.Id == pendingDebtor.Id ? updatedDebtor : debtor).ToList();
                if (!TrySave(updatedDebtors))
                {
                    return;
                }

                ReplaceDebtor(pendingDebtor, updatedDebtor);
                ClearSearchAndSelect(updatedDebtor);
                UpdateTotalLent();
                ShowStatus("Monto sumado a la cuenta existente del cliente.", true);
                return;
            }

            int id;
            try
            {
                id = GetNextId();
            }
            catch (OverflowException)
            {
                ShowStatus("No se puede asignar un ID nuevo: se alcanzó el límite numérico.", false);
                return;
            }

            var newDebtor = new Deudor
            {
                Id = id,
                Nombre = name,
                Telefono = phone,
                Concepto = concept,
                MontoOriginal = amount,
                TotalAbonado = 0
            };

            if (!TrySave(Debtors.Append(newDebtor)))
            {
                return;
            }

            Debtors.Add(newDebtor);
            ClearSearchAndSelect(newDebtor);
            UpdateTotalLent();
            SetNextId();
            ShowStatus("Préstamo añadido.", true);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!CanWrite() || _selectedDebtor is null)
                {
                    return;
                }

                var debtorToDelete = _selectedDebtor;

                // 1. Desvincular la selección visual ANTES de remover datos para evitar el crash del DataGrid
                DebtorGrid.SelectedItem = null;
                _selectedDebtor = null;

                // 2. Eliminar en SQLite (Transaccional: Deudor + sus Abonos relacionados)
                using (var conn = DatabaseHelper.GetConnection())
                using (var tr = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmdAbonos = conn.CreateCommand())
                        {
                            cmdAbonos.Transaction = tr;
                            cmdAbonos.CommandText = "DELETE FROM Abonos WHERE DeudorId = @id;";
                            cmdAbonos.Parameters.AddWithValue("@id", debtorToDelete.Id);
                            cmdAbonos.ExecuteNonQuery();
                        }

                        using (var cmdDeudor = conn.CreateCommand())
                        {
                            cmdDeudor.Transaction = tr;
                            cmdDeudor.CommandText = "DELETE FROM Deudores WHERE Id = @id;";
                            cmdDeudor.Parameters.AddWithValue("@id", debtorToDelete.Id);
                            cmdDeudor.ExecuteNonQuery();
                        }

                        tr.Commit();
                    }
                    catch (Exception exDb)
                    {
                        tr.Rollback();
                        ToastNotification.Show($"Error al eliminar en base de datos: {exDb.Message}", ToastType.Error);
                        return;
                    }
                }

                // 3. Remover de la colección en memoria
                Debtors.Remove(debtorToDelete);

                // 4. Limpieza y refresco de interfaz
                ClearForm();
                SetNextId();
                UpdateSelectionButtons();
                UpdateTotalLent();

                // 5. Notificación Toast flotante moderna
                ToastNotification.Show("Registro de deudor eliminado correctamente.", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error inesperado: {ex.Message}", ToastType.Error);
            }
        }

        private void ModifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || _selectedDebtor is null || !TryReadLoanForm(out var name, out var concept, out var amount))
            {
                return;
            }

            if (amount < _selectedDebtor.TotalAbonado)
            {
                ShowStatus("La deuda original no puede ser menor que el total abonado.", false);
                AmountBox.Focus();
                return;
            }

            var oldDebtor = _selectedDebtor;
            var updatedDebtor = CopyDebtor(oldDebtor);
            updatedDebtor.Nombre = name;
            updatedDebtor.Telefono = DebtorPhoneBox.Text.Trim();
            updatedDebtor.Concepto = concept;
            updatedDebtor.MontoOriginal = amount;

            var updatedDebtors = Debtors.Select(debtor => debtor.Id == oldDebtor.Id ? updatedDebtor : debtor).ToList();
            if (!TrySave(updatedDebtors))
            {
                return;
            }

            ReplaceDebtor(oldDebtor, updatedDebtor);
            if (DebtorsView.Contains(updatedDebtor))
            {
                SelectDebtor(updatedDebtor);
            }
            UpdateTotalLent();
            ShowStatus("Préstamo modificado.", true);
        }

        private void PayButton_Click(object sender, RoutedEventArgs e) => ApplyPayment();

        private void ApplyPayment()
        {
            if (!CanWrite() || _selectedDebtor is null)
            {
                ShowStatus("Selecciona un cliente antes de registrar un abono.", false);
                return;
            }

            if (_selectedDebtor.SaldoRestante <= 0)
            {
                ShowStatus("Este préstamo ya está completamente saldado.", false);
                return;
            }

            var culture = CultureInfo.GetCultureInfo("es-MX");
            var paymentText = PaymentAmountBox.Text.Trim();

            if (!TryParseAmount(paymentText, out var payment) || payment <= 0)
            {
                ShowStatus("Introduce un monto de abono válido y mayor a $0.00.", false);
                PaymentAmountBox.Focus();
                return;
            }

            if (payment > _selectedDebtor.SaldoRestante)
            {
                ShowStatus($"El abono ({payment.ToString("C2", culture)}) no puede superar la deuda restante ({_selectedDebtor.SaldoRestante.ToString("C2", culture)}).", false);
                PaymentAmountBox.Focus();
                return;
            }

            var oldDebtor = _selectedDebtor;
            var updatedDebtor = CopyDebtor(oldDebtor);

            var todosLosAbonos = Debtors.SelectMany(d => d.Abonos).ToList();
            var nextAbonoId = todosLosAbonos.Count > 0 ? todosLosAbonos.Max(a => a.Id) + 1 : 1;

            var nuevoAbono = new Abono
            {
                Id = nextAbonoId,
                DeudorId = updatedDebtor.Id,
                Fecha = DateTime.Now,
                Monto = payment
            };

            updatedDebtor.Abonos.Insert(0, nuevoAbono);
            updatedDebtor.TotalAbonado += payment;

            var updatedDebtors = Debtors.Select(debtor => debtor.Id == oldDebtor.Id ? updatedDebtor : debtor).ToList();
            if (!TrySave(updatedDebtors))
            {
                return;
            }

            try
            {
                var movements = MovimientoCajaFileStore.Load();
                var nextMovementId = movements.Select(m => m.Id).DefaultIfEmpty(0).Max() + 1;

                movements.Add(new MovimientoCaja
                {
                    Id = nextMovementId,
                    FechaHora = DateTime.Now,
                    Tipo = "Entrada",
                    Monto = payment,
                    Concepto = $"Abono de préstamo: {updatedDebtor.Nombre}"
                });

                MovimientoCajaFileStore.Save(movements);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al registrar entrada en caja: {ex.Message}", ToastType.Warning);
            }

            ReplaceDebtor(oldDebtor, updatedDebtor);
            SelectDebtor(updatedDebtor);
            UpdateTotalLent();
            UpdateSelectionButtons();

            PaymentAmountBox.Clear();

            var mensaje = updatedDebtor.SaldoRestante == 0
                ? $"Préstamo saldado por completo. Entrada de {payment.ToString("C2", culture)} guardada en caja."
                : $"Abono de {payment.ToString("C2", culture)} registrado con éxito. Entrada guardada en caja.";

            ShowStatus(mensaje, true);
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

        // ■ Controles de ventana
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        #region Recordatorios WhatsApp
        private void WhatsAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDebtor == null) return;

            var telefono = DebtorPhoneBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(telefono))
            {
                ShowStatus("Ingresa el número celular del cliente (10 dígitos).", false);
                DebtorPhoneBox.Focus();
                return;
            }

            var digitos = new string(telefono.Where(char.IsDigit).ToArray());
            if (digitos.Length == 10)
            {
                digitos = "52" + digitos;
            }

            if (digitos.Length < 10)
            {
                ShowStatus("El número celular no es válido.", false);
                DebtorPhoneBox.Focus();
                return;
            }

            var config = AppConfig.Load();
            var plantilla = !string.IsNullOrWhiteSpace(config.MensajeWhatsAppRecordatorio)
                ? config.MensajeWhatsAppRecordatorio
                : "Hola {CLIENTE}, te recordamos de {NEGOCIO} que tienes un saldo pendiente de {SALDO} por concepto de: {CONCEPTO}. ¡Agradecemos tu pronto pago!";

            var mensaje = plantilla
                .Replace("{CLIENTE}", _selectedDebtor.Nombre)
                .Replace("{SALDO}", _selectedDebtor.SaldoRestante.ToString("C2", CultureInfo.GetCultureInfo("es-MX")))
                .Replace("{CONCEPTO}", _selectedDebtor.Concepto)
                .Replace("{NEGOCIO}", config.NombreNegocio);

            var url = $"https://web.whatsapp.com/send?phone={digitos}&text={Uri.EscapeDataString(mensaje)}";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                ToastNotification.Show($"Abriendo WhatsApp para {_selectedDebtor.Nombre}", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo abrir el navegador: {ex.Message}", ToastType.Error);
            }
        }
        #endregion

        #region Historial de Abonos
        private void ViewHistoryButton_Click(object sender, RoutedEventArgs e) => MostrarHistorial();
        private void DebtorGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => MostrarHistorial();

        private void MostrarHistorial()
        {
            if (_selectedDebtor is null) return;

            var culture = CultureInfo.GetCultureInfo("es-MX");

            HistoryClientName.Text = $"Historial: {_selectedDebtor.Nombre}";
            HistorySummaryText.Text = $"Original: {_selectedDebtor.MontoOriginal.ToString("C2", culture)} | Total Abonado: {_selectedDebtor.TotalAbonado.ToString("C2", culture)} | Pendiente: {_selectedDebtor.SaldoRestante.ToString("C2", culture)}";

            AbonosListControl.ItemsSource = _selectedDebtor.Abonos;
            NoAbonosMessage.Visibility = _selectedDebtor.Abonos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            HistorialOverlay.Visibility = Visibility.Visible;
            HistorialOverlay.Focus();
        }

        private void CloseHistory_Click(object sender, RoutedEventArgs e)
        {
            HistorialOverlay.Visibility = Visibility.Collapsed;
        }

        private void HistorialOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, HistorialOverlay))
            {
                HistorialOverlay.Visibility = Visibility.Collapsed;
            }
        }
        #endregion

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            DebtorGrid.SelectedItem = null;
            _selectedDebtor = null;
            ClearForm();
            SetNextId();
            UpdateSelectionButtons();
            DebtorNameBox.Focus();
        }

        private bool TryReadLoanForm(out string name, out string concept, out decimal amount)
        {
            name = DebtorNameBox.Text.Trim();
            concept = ConceptBox.Text.Trim();
            amount = 0;

            if (name.Length == 0)
            {
                ShowStatus("El nombre del deudor es obligatorio.", false);
                DebtorNameBox.Focus();
                return false;
            }

            if (ContainsDelimiterOrNewLine(name) || ContainsDelimiterOrNewLine(concept) || ContainsDelimiterOrNewLine(DebtorPhoneBox.Text))
            {
                ShowStatus("Los campos no pueden contener // ni saltos de línea.", false);
                DebtorNameBox.Focus();
                return false;
            }

            if (!TryParseAmount(AmountBox.Text, out amount) || amount <= 0)
            {
                ShowStatus("Introduce una deuda válida y mayor que cero.", false);
                AmountBox.Focus();
                return false;
            }

            return true;
        }

        private static bool TryParseAmount(string text, out decimal amount)
        {
            const NumberStyles styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint;
            return decimal.TryParse(text, styles, CultureInfo.GetCultureInfo("en-US"), out amount) ||
                   decimal.TryParse(text, styles, CultureInfo.GetCultureInfo("es-MX"), out amount) ||
                   decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out amount);
        }

        private bool CanWrite()
        {
            if (!_loadFailed) return true;

            ShowStatus("No se guardaron cambios porque prestamos.txt contiene datos inválidos.", false);
            return false;
        }

        private bool TrySave(IEnumerable<Deudor> debtors)
        {
            try
            {
                PrestamoFileStore.Save(debtors);
                return true;
            }
            catch (Exception exception)
            {
                ShowStatus($"No se pudo guardar prestamos.txt: {exception.Message}", false);
                return false;
            }
        }

        private void ReplaceDebtor(Deudor oldDebtor, Deudor updatedDebtor)
        {
            var index = Debtors.IndexOf(oldDebtor);
            if (index >= 0)
            {
                Debtors[index] = updatedDebtor;
            }
        }

        private void SelectDebtor(Deudor debtor)
        {
            DebtorGrid.SelectedItem = debtor;
            DebtorGrid.ScrollIntoView(debtor);
        }

        private void ClearSearchAndSelect(Deudor debtor)
        {
            SearchBox.Clear();
            DebtorsView.Refresh();
            SelectDebtor(debtor);
        }

        private static Deudor CopyDebtor(Deudor debtor) => new()
        {
            Id = debtor.Id,
            Nombre = debtor.Nombre,
            Telefono = debtor.Telefono,
            Concepto = debtor.Concepto,
            MontoOriginal = debtor.MontoOriginal,
            TotalAbonado = debtor.TotalAbonado,
            Abonos = debtor.Abonos.ToList()
        };

        private static string CombineConcepts(string current, string added) =>
            string.Join("; ", new[] { current.Trim(), added.Trim() }.Where(value => value.Length > 0));

        private static bool ContainsDelimiterOrNewLine(string value) =>
            value.Contains("//", StringComparison.Ordinal) || value.Contains('\r') || value.Contains('\n');

        private int GetNextId() => checked(Debtors.Select(debtor => debtor.Id).DefaultIfEmpty(0).Max() + 1);

        private void SetNextId()
        {
            if (_selectedDebtor is not null)
            {
                DebtorIdBox.Text = _selectedDebtor.Id.ToString(CultureInfo.InvariantCulture);
                return;
            }

            try
            {
                DebtorIdBox.Text = GetNextId().ToString(CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                DebtorIdBox.Clear();
            }
        }

        private void UpdateSelectionButtons()
        {
            var hasSelection = _selectedDebtor is not null && !_loadFailed;
            DeleteButton.IsEnabled = hasSelection;
            ModifyButton.IsEnabled = hasSelection;
            PayButton.IsEnabled = hasSelection && _selectedDebtor!.SaldoRestante > 0;
            ViewHistoryButton.IsEnabled = hasSelection;
            WhatsAppButton.IsEnabled = hasSelection && _selectedDebtor!.SaldoRestante > 0;
        }

        private void UpdateTotalLent()
        {
            var total = Debtors.Where(debtor => debtor.SaldoRestante > 0).Sum(debtor => debtor.SaldoRestante);
            TotalLentText.Text = $"Total prestado: {total.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}";
        }

        private void ClearForm()
        {
            DebtorNameBox.Clear();
            DebtorPhoneBox.Clear();
            ConceptBox.Clear();
            AmountBox.Clear();
            PaymentAmountBox.Clear();
            StatusText.Text = string.Empty;
            _successStatusVisible = false;
        }

        private void ShowStatus(string message, bool succeeded)
        {
            StatusText.Text = message;
            StatusText.Foreground = succeeded ? System.Windows.Media.Brushes.ForestGreen : System.Windows.Media.Brushes.Firebrick;
            _successStatusVisible = succeeded;

            ToastNotification.Show(message, succeeded ? ToastType.Success : ToastType.Warning);
        }
    }
}