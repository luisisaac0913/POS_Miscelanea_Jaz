using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Punto_de_Venta
{
    public partial class Promociones : Window
    {
        public ObservableCollection<Promocion> ListaPromociones { get; } = [];

        public Promociones()
        {
            InitializeComponent();
            CargarPromociones();
        }

        private void CargarPromociones()
        {
            ListaPromociones.Clear();
            foreach (var p in PromocionDbStore.LoadAll())
            {
                ListaPromociones.Add(p);
            }
            PromocionesGrid.ItemsSource = ListaPromociones;
        }

        private void ModernTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void TipoPromoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TipoPromoCombo.SelectedItem is not ComboBoxItem item) return;
            string tipo = item.Tag?.ToString() ?? "Volumen";

            if (PanelProducto2 == null || PanelVolumen == null || PanelPrecioCombo == null || PanelPorcentaje == null) return;

            switch (tipo)
            {
                case "Volumen":
                    LabelProd1.Text = "ID Producto Principal";
                    PanelProducto2.Visibility = Visibility.Collapsed;
                    PanelVolumen.Visibility = Visibility.Visible;
                    PanelPrecioCombo.Visibility = Visibility.Collapsed;
                    PanelPorcentaje.Visibility = Visibility.Collapsed;
                    break;

                case "Combo":
                    LabelProd1.Text = "ID Primer Producto (A)";
                    PanelProducto2.Visibility = Visibility.Visible;
                    PanelVolumen.Visibility = Visibility.Collapsed;
                    PanelPrecioCombo.Visibility = Visibility.Visible;
                    PanelPorcentaje.Visibility = Visibility.Collapsed;
                    break;

                case "PorcentajeSegundo":
                    LabelProd1.Text = "ID Producto";
                    PanelProducto2.Visibility = Visibility.Collapsed;
                    PanelVolumen.Visibility = Visibility.Collapsed;
                    PanelPrecioCombo.Visibility = Visibility.Collapsed;
                    PanelPorcentaje.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void PromocionesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private static decimal ParseDecimalSeguro(string text)
        {
            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            if (decimal.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out var val) ||
                decimal.TryParse(text.Trim(), styles, CultureInfo.GetCultureInfo("es-MX"), out val))
            {
                return val;
            }
            return 0m;
        }

        private void GuardarPromo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NombrePromoBox.Text))
            {
                ToastNotification.Show("Ingresa un nombre para la promoción", ToastType.Warning);
                NombrePromoBox.Focus();
                return;
            }

            if (!int.TryParse(ProdId1Box.Text.Trim(), out var pId1) || pId1 <= 0)
            {
                ToastNotification.Show("Ingresa un ID válido para el primer producto", ToastType.Warning);
                ProdId1Box.Focus();
                return;
            }

            var tipoStr = (TipoPromoCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Volumen";
            var tipo = Enum.Parse<TipoPromocion>(tipoStr);

            var nueva = new Promocion
            {
                Nombre = NombrePromoBox.Text.Trim(),
                Tipo = tipo,
                Activa = true,
                ProductoIdPrincipal = pId1
            };

            if (tipo == TipoPromocion.Volumen)
            {
                int.TryParse(CantidadMinBox.Text.Trim(), out var cant);
                var precioEsp = ParseDecimalSeguro(PrecioEspecialBox.Text);

                if (cant < 2)
                {
                    ToastNotification.Show("La cantidad mínima para volumen debe ser al menos 2", ToastType.Warning);
                    CantidadMinBox.Focus();
                    return;
                }

                if (precioEsp <= 0)
                {
                    ToastNotification.Show("El precio especial debe ser mayor a $0.00", ToastType.Warning);
                    PrecioEspecialBox.Focus();
                    return;
                }

                nueva.CantidadMinima = cant;
                nueva.PrecioEspecial = precioEsp;
            }
            else if (tipo == TipoPromocion.Combo)
            {
                if (!int.TryParse(ProdId2Box.Text.Trim(), out var pId2) || pId2 <= 0)
                {
                    ToastNotification.Show("Ingresa el ID válido para el segundo producto del combo", ToastType.Warning);
                    ProdId2Box.Focus();
                    return;
                }

                var precioCombo = ParseDecimalSeguro(PrecioComboTotalBox.Text);
                if (precioCombo <= 0)
                {
                    ToastNotification.Show("El precio del combo debe ser mayor a $0.00", ToastType.Warning);
                    PrecioComboTotalBox.Focus();
                    return;
                }

                nueva.ProductoIdSecundario = pId2;
                nueva.PrecioComboTotal = precioCombo;
            }
            else if (tipo == TipoPromocion.PorcentajeSegundo)
            {
                var pct = ParseDecimalSeguro(PorcentajeDescuentoBox.Text);
                if (pct <= 0 || pct > 100)
                {
                    ToastNotification.Show("El porcentaje debe ser entre 1 y 100", ToastType.Warning);
                    PorcentajeDescuentoBox.Focus();
                    return;
                }

                nueva.PorcentajeDescuento = pct > 1 ? pct / 100m : pct;
            }

            if (PromocionDbStore.Save(nueva, out string error))
            {
                ToastNotification.Show("Promoción guardada con éxito", ToastType.Success);
                NombrePromoBox.Clear();
                ProdId1Box.Clear();
                if (ProdId2Box != null) ProdId2Box.Clear();
                PrecioEspecialBox.Text = "0.00";
                if (PrecioComboTotalBox != null) PrecioComboTotalBox.Text = "0.00";
                CargarPromociones();
            }
            else
            {
                ToastNotification.Show($"Error al guardar: {error}", ToastType.Error);
            }
        }
    }
}