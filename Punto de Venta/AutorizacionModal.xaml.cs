using System.Windows;
using System.Windows.Input;

namespace Punto_de_Venta
{
    public partial class AutorizacionModal : Window
    {
        public bool Autorizado { get; private set; }

        public AutorizacionModal()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PinBox.Focus();
        }

        private void PinBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ValidarAcceso();
                e.Handled = true;
            }
        }

        private void Accept_Click(object sender, RoutedEventArgs e) => ValidarAcceso();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Autorizado = false;
            Close();
        }

        private void ValidarAcceso()
        {
            var config = AppConfig.Load();
            if (config.ValidarPin(PinBox.Password))
            {
                Autorizado = true;
                Close();
            }
            else
            {
                ErrorMessage.Text = "PIN incorrecto.";
                PinBox.Clear();
                PinBox.Focus();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Autorizado = false;
                Close();
                e.Handled = true;
            }
        }
    }
}