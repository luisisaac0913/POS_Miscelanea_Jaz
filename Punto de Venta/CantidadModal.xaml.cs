using System.Windows;
using System.Windows.Input;

namespace Punto_de_Venta
{
    public partial class CantidadModal : Window
    {
        public int CantidadResult { get; private set; } = 1;
        public bool Confirmado { get; private set; }

        public CantidadModal(string nombreProducto)
        {
            InitializeComponent();
            ProductTitleText.Text = nombreProducto;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            QuantityBox.Text = "50"; // Sugerencia inicial comun para recargas
            QuantityBox.Focus();
            QuantityBox.SelectAll();
        }

        private void QuantityBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirmar();
                e.Handled = true;
            }
        }

        private void Accept_Click(object sender, RoutedEventArgs e) => Confirmar();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmado = false;
            Close();
        }

        private void Confirmar()
        {
            if (int.TryParse(QuantityBox.Text.Trim(), out var cant) && cant > 0)
            {
                CantidadResult = cant;
                Confirmado = true;
                Close();
            }
            else
            {
                QuantityBox.SelectAll();
                QuantityBox.Focus();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Confirmado = false;
                Close();
                e.Handled = true;
            }
        }
    }
}