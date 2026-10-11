using Microsoft.Win32;
using System;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Punto_de_Venta
{
    public partial class Configuracion : Window
    {
        private AppConfig _config = new();

        public Configuracion()
        {
            InitializeComponent();
            CargarDatos();
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
        private void CargarDatos()
        {
            _config = AppConfig.Load();
            WhatsAppTemplateBox.Text = _config.MensajeWhatsAppRecordatorio;
            // Impresoras del sistema
            PrintersComboBox.Items.Clear();
            PrintersComboBox.Items.Add("(Ninguna / Desactivada)");
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                PrintersComboBox.Items.Add(printer);
            }

            if (!string.IsNullOrEmpty(_config.ImpresoraSeleccionada) && PrintersComboBox.Items.Contains(_config.ImpresoraSeleccionada))
            {
                PrintersComboBox.SelectedItem = _config.ImpresoraSeleccionada;
            }
            else
            {
                PrintersComboBox.SelectedIndex = 0;
            }

            // Ancho papel
            foreach (ComboBoxItem item in PaperWidthComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.AnchoTicketCaracteres.ToString())
                {
                    PaperWidthComboBox.SelectedItem = item;
                    break;
                }
            }

            // Textos ticket
            BusinessNameBox.Text = _config.NombreNegocio;
            BusinessSubtitleBox.Text = _config.GiroNegocio;
            BusinessAddressBox.Text = _config.DireccionNegocio;
            FooterMessageBox.Text = _config.MensajePie;

            // Personalización (Solución error: solo carga el estado sin cambiar el tema)
            DarkModeCheckBox.IsChecked = _config.ModoOscuro;

            // Caja y seguridad
            AskBeforePrintCheckBox.IsChecked = _config.PreguntarAntesDeImprimir;
            AutoPrintCheckBox.IsChecked = _config.ImprimirTicketAutomatico;
            BackupFolderBox.Text = _config.RutaRespaldos;

            // Báscula digital
            EnableScaleCheckBox.IsChecked = _config.BasculaHabilitada;
            CargarPuertosSerie();

            foreach (ComboBoxItem item in ScaleBaudRateComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.BasculaBaudRate.ToString())
                {
                    ScaleBaudRateComboBox.SelectedItem = item;
                    break;
                }
            }

            foreach (ComboBoxItem item in ScaleProtocolComboBox.Items)
            {
                if (item.Content?.ToString() == _config.BasculaProtocolo)
                {
                    ScaleProtocolComboBox.SelectedItem = item;
                    break;
                }
            }

            ActualizarVistaPreviaTicket();
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

        private void TestScale_Click(object sender, RoutedEventArgs e)
        {
            var puerto = ScalePortComboBox.SelectedItem?.ToString() ?? "COM1";
            var baudRate = 9600;
            if (ScaleBaudRateComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var b))
            {
                baudRate = b;
            }

            ScaleTestResultText.Text = "Leyendo báscula...";

            if (BasculaService.ProbarConexion(puerto, baudRate, out var peso, out var error))
            {
                ScaleTestResultText.Text = $"✅ Peso leído con éxito: {peso:0.000} kg";
                ToastNotification.Show($"Báscula conectada: {peso:0.000} kg", ToastType.Success);
            }
            else
            {
                ScaleTestResultText.Text = $"❌ Falló la lectura: {error}";
                ToastNotification.Show($"Error en báscula: {error}", ToastType.Error);
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
        }

        private void TicketField_TextChanged(object sender, TextChangedEventArgs e) => ActualizarVistaPreviaTicket();
        private void PrintersComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void PaperWidthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ActualizarVistaPreviaTicket();

        private void ActualizarVistaPreviaTicket()
        {
            if (PreviewTicketText == null) return;

            var cols = 32;
            if (PaperWidthComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var parsedCols))
            {
                cols = parsedCols;
            }

            var lineSep = new string('-', cols);
            var sb = new System.Text.StringBuilder();

            sb.AppendLine(CentrarTexto(BusinessNameBox?.Text ?? "MISCELANEA JAZ", cols));
            sb.AppendLine(CentrarTexto(BusinessSubtitleBox?.Text ?? "", cols));
            sb.AppendLine(CentrarTexto(BusinessAddressBox?.Text ?? "", cols));
            sb.AppendLine(lineSep);
            sb.AppendLine($"FECHA: {DateTime.Now:dd/MM/yyyy hh:mm tt}");
            sb.AppendLine("FOLIO: #0001");
            sb.AppendLine(lineSep);
            sb.AppendLine(cols == 42 ? "CANT  DESCRIPCION            PRECIO   IMPORTE" : "CANT DESCRIPCION       PRECIO IMPORTE");
            sb.AppendLine("1    Refresco Coca Cola 600  $18.00    $18.00");
            sb.AppendLine("2    Galletas Chokis 70g     $17.00    $34.00");
            sb.AppendLine(lineSep);
            sb.AppendLine($"TOTAL:                         $52.00".PadLeft(cols));
            sb.AppendLine($"PAGO CON:                      $100.00".PadLeft(cols));
            sb.AppendLine($"CAMBIO:                        $48.00".PadLeft(cols));
            sb.AppendLine(lineSep);

            var lineasPie = (FooterMessageBox?.Text ?? "").Split('\n');
            foreach (var l in lineasPie)
            {
                sb.AppendLine(CentrarTexto(l.Trim(), cols));
            }

            PreviewTicketText.Text = sb.ToString();
        }

        private static string CentrarTexto(string texto, int longitud)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            texto = texto.Trim();
            if (texto.Length >= longitud) return texto.Substring(0, longitud);
            int espacios = (longitud - texto.Length) / 2;
            return new string(' ', espacios) + texto;
        }

        private void BrowseBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Seleccionar carpeta para respaldos .ZIP" };
            if (dialog.ShowDialog() == true)
            {
                BackupFolderBox.Text = dialog.FolderName;
            }
        }
        // ■ Arrastre y alternancia de maximizado con doble clic
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

        // ■ Botones de control de ventana
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Impresión
                var printerSel = PrintersComboBox.SelectedItem?.ToString() ?? "";
                _config.ImpresoraSeleccionada = printerSel.StartsWith("(") ? "" : printerSel;

                if (PaperWidthComboBox.SelectedItem is ComboBoxItem wItem && int.TryParse(wItem.Tag?.ToString(), out var width))
                {
                    _config.AnchoTicketCaracteres = width;
                }

                _config.NombreNegocio = BusinessNameBox.Text.Trim();
                _config.GiroNegocio = BusinessSubtitleBox.Text.Trim();
                _config.DireccionNegocio = BusinessAddressBox.Text.Trim();
                _config.MensajePie = FooterMessageBox.Text.Trim();

                // Personalización
                var modoOscuroSeleccionado = DarkModeCheckBox.IsChecked == true;
                _config.ModoOscuro = modoOscuroSeleccionado;

                // Báscula digital
                _config.BasculaHabilitada = EnableScaleCheckBox.IsChecked == true;
                _config.BasculaPuerto = ScalePortComboBox.SelectedItem?.ToString() ?? "COM1";
                if (ScaleBaudRateComboBox.SelectedItem is ComboBoxItem baudItem && int.TryParse(baudItem.Tag?.ToString(), out var baud))
                {
                    _config.BasculaBaudRate = baud;
                }
                _config.BasculaProtocolo = (ScaleProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Torrey / Rhino (Petición P)";

                // Seguridad y caja
                _config.PreguntarAntesDeImprimir = AskBeforePrintCheckBox.IsChecked == true;
                _config.ImprimirTicketAutomatico = AutoPrintCheckBox.IsChecked == true;
                _config.RutaRespaldos = BackupFolderBox.Text.Trim();

                if (!string.IsNullOrWhiteSpace(NewPinBox.Password))
                {
                    _config.EstablecerNuevoPin(NewPinBox.Password.Trim());
                    NewPinBox.Clear();
                }
                _config.MensajeWhatsAppRecordatorio = WhatsAppTemplateBox.Text.Trim();
                // Guardar en disco
                _config.Save();

                // SOLUCIÓN: Solo ahora que se presionó "Guardar cambios" se aplica el tema a la aplicación
                if (Application.Current is App app)
                {
                    app.SetDarkMode(modoOscuroSeleccionado);
                }

                ToastNotification.Show("Configuración guardada exitosamente", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al guardar configuración: {ex.Message}", ToastType.Error);
            }
        }
    }
}