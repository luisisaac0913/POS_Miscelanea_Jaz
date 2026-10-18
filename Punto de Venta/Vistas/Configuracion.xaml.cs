using Microsoft.Win32;
using Punto_de_Venta.Servicios;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Punto_de_Venta
{
    public partial class Configuracion : Window
    {
        private AppConfig _config = new();
        private bool _isTestingScale = false;

        public Configuracion()
        {
            InitializeComponent();
            Loaded += Configuracion_Loaded;
        }

        private async void Configuracion_Loaded(object sender, RoutedEventArgs e)
        {
            CargarDatosFormulario();
            await CargarDispositivosAsync();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void NavWhatsApp_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelWhatsApp, false);

        private void CargarDatosFormulario()
        {
            _config = AppConfig.Load();
            WhatsAppTemplateBox.Text = _config.MensajeWhatsAppRecordatorio ?? string.Empty;

            // Ancho de papel
            foreach (ComboBoxItem item in PaperWidthComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.AnchoTicketCaracteres.ToString())
                {
                    PaperWidthComboBox.SelectedItem = item;
                    break;
                }
            }
            if (PaperWidthComboBox.SelectedItem == null && PaperWidthComboBox.Items.Count > 0)
            {
                PaperWidthComboBox.SelectedIndex = 0;
            }

            // Datos del ticket
            BusinessNameBox.Text = _config.NombreNegocio ?? string.Empty;
            BusinessSubtitleBox.Text = _config.GiroNegocio ?? string.Empty;
            BusinessAddressBox.Text = _config.DireccionNegocio ?? string.Empty;
            FooterMessageBox.Text = _config.MensajePie ?? string.Empty;

            // Personalización
            DarkModeCheckBox.IsChecked = _config.ModoOscuro;

            // Impresión y respaldos
            AskBeforePrintCheckBox.IsChecked = _config.PreguntarAntesDeImprimir;
            AutoPrintCheckBox.IsChecked = _config.ImprimirTicketAutomatico;
            BackupFolderBox.Text = _config.RutaRespaldos ?? string.Empty;

            // Báscula digital
            EnableScaleCheckBox.IsChecked = _config.BasculaHabilitada;

            foreach (ComboBoxItem item in ScaleBaudRateComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.BasculaBaudRate.ToString())
                {
                    ScaleBaudRateComboBox.SelectedItem = item;
                    break;
                }
            }
            if (ScaleBaudRateComboBox.SelectedItem == null && ScaleBaudRateComboBox.Items.Count > 0)
            {
                ScaleBaudRateComboBox.SelectedIndex = 0;
            }

            foreach (ComboBoxItem item in ScaleProtocolComboBox.Items)
            {
                var content = item.Content?.ToString() ?? string.Empty;
                if (string.Equals(content, _config.BasculaProtocolo, StringComparison.OrdinalIgnoreCase) ||
                    content.StartsWith(_config.BasculaProtocolo ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    ScaleProtocolComboBox.SelectedItem = item;
                    break;
                }
            }
            if (ScaleProtocolComboBox.SelectedItem == null && ScaleProtocolComboBox.Items.Count > 0)
            {
                ScaleProtocolComboBox.SelectedIndex = 0;
            }

            ActualizarVistaPreviaTicket();
        }

        private async Task CargarDispositivosAsync()
        {
            PrintersComboBox.Items.Clear();
            PrintersComboBox.Items.Add("Cargando impresoras...");
            PrintersComboBox.SelectedIndex = 0;
            PrintersComboBox.IsEnabled = false;

            var listaImpresoras = await Task.Run(() =>
            {
                var resultado = new List<string>();
                try
                {
                    foreach (string printer in PrinterSettings.InstalledPrinters)
                    {
                        resultado.Add(printer);
                    }
                }
                catch
                {
                    // Manejo silencioso en entornos donde el spooler esté deshabilitado
                }
                return resultado;
            });

            PrintersComboBox.Items.Clear();
            PrintersComboBox.Items.Add("(Ninguna / Desactivada)");
            foreach (var printer in listaImpresoras)
            {
                PrintersComboBox.Items.Add(printer);
            }
            PrintersComboBox.IsEnabled = true;

            if (!string.IsNullOrEmpty(_config.ImpresoraSeleccionada) && PrintersComboBox.Items.Contains(_config.ImpresoraSeleccionada))
            {
                PrintersComboBox.SelectedItem = _config.ImpresoraSeleccionada;
            }
            else
            {
                PrintersComboBox.SelectedIndex = 0;
            }

            CargarPuertosSerie();
        }

        private void CargarPuertosSerie()
        {
            ScalePortComboBox.Items.Clear();
            var puertos = BasculaService.ObtenerPuertosDisponibles();

            foreach (var p in puertos)
            {
                ScalePortComboBox.Items.Add(p);
            }

            if (ScalePortComboBox.Items.Contains(_config.BasculaPuerto))
            {
                ScalePortComboBox.SelectedItem = _config.BasculaPuerto;
            }
            else if (ScalePortComboBox.Items.Count > 0)
            {
                ScalePortComboBox.SelectedIndex = 0;
            }
            else
            {
                ScalePortComboBox.Items.Add("COM1");
                ScalePortComboBox.SelectedIndex = 0;
            }
        }

        private void RefreshScalePorts_Click(object sender, RoutedEventArgs e)
        {
            CargarPuertosSerie();
            ToastNotification.Show("Puertos COM actualizados", ToastType.Info);
        }

        private async void TestScale_Click(object sender, RoutedEventArgs e)
        {
            if (_isTestingScale) return;

            var puerto = ScalePortComboBox.SelectedItem?.ToString() ?? "COM1";
            var baudRate = 9600;
            if (ScaleBaudRateComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var b))
            {
                baudRate = b;
            }

            _isTestingScale = true;
            ScaleTestResultText.Text = "Conectando y leyendo bascula...";
            ScaleTestResultText.Foreground = (Brush)FindResource("ThemeMutedForeground");

            var (exito, peso, error) = await Task.Run(() =>
            {
                bool ok = BasculaService.ProbarConexion(puerto, baudRate, out decimal p, out string err);
                return (ok, p, err);
            });

            _isTestingScale = false;

            if (exito)
            {
                ScaleTestResultText.Text = $"✅ Lectura exitosa: {peso:0.000} kg";
                ScaleTestResultText.Foreground = (Brush)FindResource("SystemGreen");
                ToastNotification.Show($"Bascula conectada: {peso:0.000} kg", ToastType.Success);
            }
            else
            {
                ScaleTestResultText.Text = $"❌ Fallo: {error}";
                ScaleTestResultText.Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                ToastNotification.Show($"Error en bascula: {error}", ToastType.Error);
            }
        }

        private void NavImpresion_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelImpresion, true);
        private void NavPersonalizacion_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelPersonalizacion, false);
        private void NavBascula_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelBascula, false);
        private void NavSeguridad_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelSeguridad, false);
        private void NavAtajos_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelAtajos, false);

        private void MostrarPanel(StackPanel panel, bool verPreviewTicket)
        {
            PanelImpresion.Visibility = Visibility.Collapsed;
            PanelPersonalizacion.Visibility = Visibility.Collapsed;
            PanelBascula.Visibility = Visibility.Collapsed;
            PanelSeguridad.Visibility = Visibility.Collapsed;
            PanelAtajos.Visibility = Visibility.Collapsed;
            PanelWhatsApp.Visibility = Visibility.Collapsed;

            panel.Visibility = Visibility.Visible;
            PreviewColumn.Width = verPreviewTicket ? new GridLength(300) : new GridLength(0);
            PreviewCardBorder.Visibility = verPreviewTicket ? Visibility.Visible : Visibility.Collapsed;

            // Resaltar visualmente el botón activo en el sidebar
            ActualizarBotonSidebar(NavImpresionBtn, panel == PanelImpresion);
            ActualizarBotonSidebar(NavPersonalizacionBtn, panel == PanelPersonalizacion);
            ActualizarBotonSidebar(NavBasculaBtn, panel == PanelBascula);
            ActualizarBotonSidebar(NavWhatsAppBtn, panel == PanelWhatsApp);
            ActualizarBotonSidebar(NavSeguridadBtn, panel == PanelSeguridad);
            ActualizarBotonSidebar(NavAtajosBtn, panel == PanelAtajos);
        }

        private static void ActualizarBotonSidebar(Button btn, bool activo)
        {
            if (btn == null) return;
            if (activo)
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

        private void TicketField_TextChanged(object sender, TextChangedEventArgs e) => ActualizarVistaPreviaTicket();
        private void PaperWidthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ActualizarVistaPreviaTicket();

        private void ActualizarVistaPreviaTicket()
        {
            if (PreviewTicketText == null) return;

            int cols = 32;
            if (PaperWidthComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int parsedCols))
            {
                cols = parsedCols;
            }

            var lineSep = new string('-', cols);
            var sb = new StringBuilder();

            sb.AppendLine(CentrarTexto(BusinessNameBox?.Text ?? "MISCELANEA JAZ", cols));
            sb.AppendLine(CentrarTexto(BusinessSubtitleBox?.Text ?? string.Empty, cols));
            sb.AppendLine(CentrarTexto(BusinessAddressBox?.Text ?? string.Empty, cols));
            sb.AppendLine(lineSep);
            sb.AppendLine($"FECHA: {DateTime.Now:dd/MM/yyyy hh:mm tt}");
            sb.AppendLine("FOLIO: #0001");
            sb.AppendLine(lineSep);

            if (cols == 42)
            {
                sb.AppendLine("CANT  DESCRIPCION            PRECIO   IMPORTE");
                sb.AppendLine("1     Refresco Coca Cola 600 $18.00    $18.00");
                sb.AppendLine("2     Galletas Chokis 70g    $17.00    $34.00");
            }
            else
            {
                sb.AppendLine("CANT DESCRIPCION       PRECIO IMPORTE");
                sb.AppendLine("1    Refresco Coca 600 $18.00  $18.00");
                sb.AppendLine("2    Galletas Chokis   $17.00  $34.00");
            }

            sb.AppendLine(lineSep);
            sb.AppendLine(AlinearExtremos("TOTAL:", "$52.00", cols));
            sb.AppendLine(AlinearExtremos("PAGO CON:", "$100.00", cols));
            sb.AppendLine(AlinearExtremos("CAMBIO:", "$48.00", cols));
            sb.AppendLine(lineSep);

            var lineasPie = (FooterMessageBox?.Text ?? string.Empty).Split('\n');
            foreach (var l in lineasPie)
            {
                if (!string.IsNullOrWhiteSpace(l))
                {
                    sb.AppendLine(CentrarTexto(l.Trim(), cols));
                }
            }

            PreviewTicketText.Text = sb.ToString();
        }

        private static string CentrarTexto(string texto, int longitud)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            texto = texto.Trim();
            if (texto.Length >= longitud) return texto[..longitud];
            int espacios = (longitud - texto.Length) / 2;
            return new string(' ', espacios) + texto;
        }

        private static string AlinearExtremos(string izquierda, string derecha, int anchoTotal)
        {
            int espacios = anchoTotal - (izquierda.Length + derecha.Length);
            if (espacios < 1) espacios = 1;
            return izquierda + new string(' ', espacios) + derecha;
        }

        private void BrowseBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // En .NET 8+ OpenFolderDialog está en Microsoft.Win32
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Seleccionar carpeta para respaldos automáticos",
                    Multiselect = false
                };

                if (dialog.ShowDialog(this) == true)
                {
                    string rutaSeleccionada = dialog.FolderName;

                    // Validar que la carpeta exista o se pueda crear
                    if (!Directory.Exists(rutaSeleccionada))
                    {
                        Directory.CreateDirectory(rutaSeleccionada);
                    }

                    // Probar permiso de escritura inmediato
                    string pruebaArchivo = Path.Combine(rutaSeleccionada, ".permiso_test");
                    File.WriteAllText(pruebaArchivo, "ok");
                    File.Delete(pruebaArchivo);

                    BackupFolderBox.Text = rutaSeleccionada;
                    ToastNotification.Show("Carpeta de respaldos configurada correctamente", ToastType.Success);
                }
            }
            catch (UnauthorizedAccessException)
            {
                ToastNotification.Show("No tienes permisos de escritura en esa carpeta. Elige otra.", ToastType.Error);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al seleccionar carpeta: {ex.Message}", ToastType.Error);
            }
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
        // Exclusividad lógica de impresión para evitar comportamientos contradictorios
        private void AskBeforePrintCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (AutoPrintCheckBox != null && AutoPrintCheckBox.IsChecked == true)
            {
                AutoPrintCheckBox.IsChecked = false;
            }
        }

        private void AutoPrintCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (AskBeforePrintCheckBox != null && AskBeforePrintCheckBox.IsChecked == true)
            {
                AskBeforePrintCheckBox.IsChecked = false;
            }
        }
        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var printerSel = PrintersComboBox.SelectedItem?.ToString() ?? string.Empty;
                _config.ImpresoraSeleccionada = (printerSel.StartsWith("(") || printerSel.StartsWith("Cargando")) ? string.Empty : printerSel;

                if (PaperWidthComboBox.SelectedItem is ComboBoxItem wItem && int.TryParse(wItem.Tag?.ToString(), out var width))
                {
                    _config.AnchoTicketCaracteres = width;
                }

                _config.NombreNegocio = BusinessNameBox.Text.Trim();
                _config.GiroNegocio = BusinessSubtitleBox.Text.Trim();
                _config.DireccionNegocio = BusinessAddressBox.Text.Trim();
                _config.MensajePie = FooterMessageBox.Text.Trim();

                var modoOscuroSeleccionado = DarkModeCheckBox.IsChecked == true;
                _config.ModoOscuro = modoOscuroSeleccionado;

                _config.BasculaHabilitada = EnableScaleCheckBox.IsChecked == true;
                _config.BasculaPuerto = ScalePortComboBox.SelectedItem?.ToString() ?? "COM1";
                if (ScaleBaudRateComboBox.SelectedItem is ComboBoxItem baudItem && int.TryParse(baudItem.Tag?.ToString(), out var baud))
                {
                    _config.BasculaBaudRate = baud;
                }
                _config.BasculaProtocolo = (ScaleProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Torrey / Rhino (Petición P)";

                _config.PreguntarAntesDeImprimir = AskBeforePrintCheckBox.IsChecked == true;
                _config.ImprimirTicketAutomatico = AutoPrintCheckBox.IsChecked == true;
                _config.RutaRespaldos = BackupFolderBox.Text.Trim();

                // Validación de PIN
                if (!string.IsNullOrWhiteSpace(NewPinBox.Password))
                {
                    var nuevoPin = NewPinBox.Password.Trim();
                    if (nuevoPin.Length < 4)
                    {
                        ToastNotification.Show("El PIN debe tener al menos 4 dígitos numericos", ToastType.Warning);
                        NewPinBox.Focus();
                        return;
                    }

                    _config.EstablecerNuevoPin(nuevoPin);
                    NewPinBox.Clear();
                }

                _config.MensajeWhatsAppRecordatorio = WhatsAppTemplateBox.Text.Trim();
                _config.Save();

                if (Application.Current is App app)
                {
                    app.SetDarkMode(modoOscuroSeleccionado);
                }

                ToastNotification.Show("Configuracion guardada exitosamente", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al guardar configuracion: {ex.Message}", ToastType.Error);
            }
        }
    }
}