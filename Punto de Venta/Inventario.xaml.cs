using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using static OpenTK.Graphics.OpenGL.GL;

namespace Punto_de_Venta
{
    public partial class Inventario : Window
    {
        private readonly DispatcherTimer _clockTimer;
        private Producto? _selectedProduct;
        private bool _loadFailed;
        private bool _isUpdatingForm;
        private string _currentStockFilter = "All";

        public ObservableCollection<Producto> Products { get; } = [];
        public ICollectionView ProductsView { get; }
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
        public Inventario()
        {
            InitializeComponent();
            Language = XmlLanguage.GetLanguage("es-MX");
            ProductsView = CollectionViewSource.GetDefaultView(Products);
            ProductsView.Filter = ProductMatchesSearch;
            DataContext = this;

            try
            {
                foreach (var product in ProductoFileStore.Load())
                {
                    Products.Add(product);
                }
            }
            catch (Exception exception)
            {
                _loadFailed = true;
                ToastNotification.Show($"Error al cargar inventario: {exception.Message}", ToastType.Error);
            }

            UpdateProductCount();
            SetNextId();
            UpdateSelectionActions();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
            _clockTimer.Start();
            Closed += (_, _) => _clockTimer.Stop();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FocusCodeBox();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                FocusSearch();
                e.Handled = true;
                return;
            }
        }

        private void FocusSearch()
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
            SearchBox.SelectAll();
        }

        private void FocusCodeBox()
        {
            ProductCodeBox.Focus();
            Keyboard.Focus(ProductCodeBox);
            ProductCodeBox.SelectAll();
        }

        private void FilterStock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string filterTag)
            {
                _currentStockFilter = filterTag;
                ProductsView.Refresh();
            }
        }

        private bool ProductMatchesSearch(object item)
        {
            if (item is not Producto product)
            {
                return false;
            }

            if (_currentStockFilter == "Low" && !product.EsStockBajo)
            {
                return false;
            }
            if (_currentStockFilter == "Out" && !product.EsStockAgotado)
            {
                return false;
            }

            var query = SearchBox.Text.Trim();
            if (query.Length == 0) return true;

            return product.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   product.Codigo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   product.Nombre.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ProductsView.Refresh();
        }

        private void QuickRestock_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct is null || sender is not Button btn || btn.Tag is not string amountStr)
            {
                ToastNotification.Show("Selecciona un producto para reabastecer", ToastType.Warning);
                return;
            }

            if (!int.TryParse(amountStr, out var addAmount)) return;

            int.TryParse(StockBox.Text.Trim(), out var currentStock);
            var newStock = currentStock + addAmount;

            StockBox.Text = newStock.ToString();
            _selectedProduct.Stock = newStock;

            try
            {
                ProductoFileStore.Save(Products);
                ProductsView.Refresh();
                ToastNotification.Show($"+{addAmount} pzas añadidas ({_selectedProduct.Nombre}). Stock: {newStock}", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al guardar reabastecimiento: {ex.Message}", ToastType.Error);
            }
        }

        private void CostOrMargin_TextChanged(object sender, TextChangedEventArgs e)
        {
            // La suma de Costo + Ganancia ahora se calcula al guardar en TryReadForm
        }

        private void ProductGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedProduct = ProductGrid.SelectedItem as Producto;

            UpdateSelectionActions();

            if (_selectedProduct is null)
            {
                ClearForm();
                return;
            }

            try
            {
                _isUpdatingForm = true;

                ProductIdBox.Text = _selectedProduct.Id.ToString(CultureInfo.InvariantCulture);
                ProductCodeBox.Text = _selectedProduct.Codigo ?? string.Empty;
                ProductNameBox.Text = _selectedProduct.Nombre ?? string.Empty;
                ProductCategoryBox.Text = _selectedProduct.Categoria ?? "N/A";
                StockBox.Text = _selectedProduct.Stock.ToString();
                ProductCostBox.Text = _selectedProduct.Costo.ToString("0.00", CultureInfo.InvariantCulture);
                ProductMarginBox.Text = _selectedProduct.Ganancia.ToString("0.00", CultureInfo.InvariantCulture);

                if (AskQuantityCheckBox != null)
                {
                    AskQuantityCheckBox.IsChecked = _selectedProduct.PedirCantidadAlCobrar;
                }
            }
            finally
            {
                _isUpdatingForm = false;
            }
        }

        private void ProductCodeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var code = ProductCodeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code)) return;

            var existing = Products.FirstOrDefault(p =>
                string.Equals(p.Codigo, code, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                SelectProduct(existing);
                ProductCostBox.Focus();
                ProductCostBox.SelectAll();
            }
            else
            {
                _selectedProduct = null;
                ProductGrid.SelectedItem = null;
                UpdateSelectionActions();
                SetNextId();

                ProductNameBox.Clear();
                ProductCategoryBox.Clear();
                StockBox.Text = "0";
                ProductCostBox.Clear();
                ProductMarginBox.Clear();
                if (AskQuantityCheckBox != null) AskQuantityCheckBox.IsChecked = false;

                ProductNameBox.Focus();
            }

            e.Handled = true;
        }

        private void ProductNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ProductCategoryBox.Focus();
                ProductCategoryBox.SelectAll();
                e.Handled = true;
            }
        }

        private void ProductCategoryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StockBox.Focus();
                StockBox.SelectAll();
                e.Handled = true;
            }
        }

        private void StockBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ProductCostBox.Focus();
                ProductCostBox.SelectAll();
                e.Handled = true;
            }
        }

        private void ProductCostBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ProductMarginBox.Focus();
                ProductMarginBox.SelectAll();
                e.Handled = true;
            }
        }

        private void ProductMarginBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (_selectedProduct is not null)
            {
                ModifyButton_Click(sender, e);
            }
            else
            {
                AddButton_Click(sender, e);
            }

            e.Handled = true;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || !TryReadForm(out var code, out var name, out var cost, out var margin, out var price, out var category, out var stock))
            {
                return;
            }

            if (HasDuplicateName(name))
            {
                ShowValidationError("Ya existe un producto con ese nombre.", "ProductNameBox");
                return;
            }

            int id;
            try
            {
                id = GetNextId();
            }
            catch (OverflowException)
            {
                ShowValidationError("Límite numérico de ID alcanzado.", "ProductNameBox");
                return;
            }

            var newProduct = new Producto
            {
                Id = id,
                Codigo = code,
                Nombre = name,
                Categoria = category,
                Costo = cost,
                Ganancia = margin,
                Precio = price,
                Stock = stock,
                PedirCantidadAlCobrar = AskQuantityCheckBox?.IsChecked == true
            };

            var updatedProducts = Products.Append(newProduct).ToList();
            if (!TrySave(updatedProducts))
            {
                return;
            }

            Products.Add(newProduct);
            SearchBox.Clear();
            ProductsView.Refresh();
            UpdateProductCount();

            ToastNotification.Show($"Producto '{newProduct.Nombre}' registrado", ToastType.Success);

            ClearForm();
            SetNextId();
            FocusCodeBox();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || _selectedProduct is null)
            {
                return;
            }

            var productToDelete = _selectedProduct;

            var confirm = MessageBox.Show(this,
                $"¿Seguro que deseas eliminar '{productToDelete.Nombre}' del inventario?",
                "Confirmar Eliminación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var updatedProducts = Products.Where(product => product.Id != productToDelete.Id).ToList();
            if (!TrySave(updatedProducts))
            {
                return;
            }

            Products.Remove(productToDelete);
            ProductGrid.SelectedItem = null;
            ClearForm();
            UpdateSelectionActions();
            UpdateProductCount();
            SetNextId();
            FocusCodeBox();

            ToastNotification.Show($"Producto '{productToDelete.Nombre}' eliminado", ToastType.Success);
        }

        private bool HasDuplicateCode(string code, int? excludedId = null)
        {
            // Si no tiene código de barras asignado, se permite
            if (string.IsNullOrWhiteSpace(code)) return false;

            return Products.Any(product =>
                product.Id != excludedId &&
                string.Equals(product.Codigo, code, StringComparison.OrdinalIgnoreCase));
        }
        private void ModifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || _selectedProduct is null || !TryReadForm(out var code, out var name, out var cost, out var margin, out var price, out var category, out var stock))
            {
                return;
            }

            if (HasDuplicateName(name, _selectedProduct.Id))
            {
                ShowValidationError("Ya existe otro producto con ese nombre.", "ProductNameBox");
                return;
            }
            if (HasDuplicateCode(code, _selectedProduct.Id))
            {
                ShowValidationError("Ya existe otro producto con ese código de barras.", "ProductCodeBox");
                return;
            }
            var oldProduct = _selectedProduct;
            var updatedProduct = new Producto
            {
                Id = oldProduct.Id,
                Codigo = code,
                Nombre = name,
                Categoria = category,
                Costo = cost,
                Ganancia = margin,
                Precio = price,
                Stock = stock,
                PedirCantidadAlCobrar = AskQuantityCheckBox?.IsChecked == true
            };

            var updatedProducts = Products.Select(product => product.Id == oldProduct.Id ? updatedProduct : product).ToList();
            if (!TrySave(updatedProducts))
            {
                return;
            }

            var index = Products.IndexOf(oldProduct);
            if (index >= 0)
            {
                Products[index] = updatedProduct;
            }

            ProductsView.Refresh();
            SelectProduct(updatedProduct);
            UpdateProductCount();
            ClearForm();
            SetNextId();
            FocusCodeBox();

            ToastNotification.Show($"Producto '{updatedProduct.Nombre}' actualizado", ToastType.Success);
        }

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            ProductGrid.SelectedItem = null;
            _selectedProduct = null;
            UpdateSelectionActions();
            ClearForm();
            SetNextId();
            FocusCodeBox();
        }

        private bool TryReadForm(out string code, out string name, out decimal cost, out decimal margin, out decimal price, out string category, out int stock)
        {
            code = ProductCodeBox.Text.Trim();
            name = ProductNameBox.Text.Trim();
            category = ProductCategoryBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(category))
            {
                category = "N/A";
            }

            var costText = ProductCostBox.Text.Trim();
            var marginText = ProductMarginBox.Text.Trim();

            cost = 0;
            margin = 0;
            price = 0;
            stock = 0;

            if (name.Length == 0)
            {
                ShowValidationError("El nombre del producto es obligatorio.", "ProductNameBox");
                return false;
            }

            if (name.Contains("//", StringComparison.Ordinal) || name.Contains('\r') || name.Contains('\n') ||
                code.Contains("//", StringComparison.Ordinal) || category.Contains("//", StringComparison.Ordinal) ||
                category.Contains('\r') || category.Contains('\n'))
            {
                ShowValidationError("Nombre, código y categoría no pueden contener // ni saltos de línea.", "ProductNameBox");
                return false;
            }

            if (!int.TryParse(StockBox.Text.Trim(), out stock) || stock < 0)
            {
                ShowValidationError("Introduce un stock entero igual o mayor que cero.", "StockBox");
                return false;
            }

            const NumberStyles styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint;
            var usCulture = CultureInfo.GetCultureInfo("en-US");
            var esCulture = CultureInfo.GetCultureInfo("es-ES");

            var parsedCost = decimal.TryParse(costText, styles, usCulture, out cost) ||
                             decimal.TryParse(costText, styles, esCulture, out cost);

            if (!parsedCost || cost < 0)
            {
                ShowValidationError("Introduce un costo decimal válido igual o mayor que cero.", "ProductCostBox");
                return false;
            }

            var parsedMargin = decimal.TryParse(marginText, styles, usCulture, out margin) ||
                               decimal.TryParse(marginText, styles, esCulture, out margin);

            if (!parsedMargin || margin < 0)
            {
                ShowValidationError("Introduce una ganancia decimal válida igual o mayor que cero.", "ProductMarginBox");
                return false;
            }

            price = cost + margin;
            return true;
        }

        private bool HasDuplicateName(string name, int? excludedId = null) =>
            Products.Any(product => product.Id != excludedId && string.Equals(product.Nombre, name, StringComparison.OrdinalIgnoreCase));

        private bool CanWrite()
        {
            if (!_loadFailed)
            {
                return true;
            }

            ToastNotification.Show("El archivo de inventario no está disponible para escritura", ToastType.Error);
            return false;
        }

        private bool TrySave(IEnumerable<Producto> products)
        {
            try
            {
                ProductoFileStore.Save(products);
                return true;
            }
            catch (Exception exception)
            {
                ToastNotification.Show($"Error al guardar productos.txt: {exception.Message}", ToastType.Error);
                return false;
            }
        }

        private void ShowValidationError(string message, string controlName)
        {
            ToastNotification.Show(message, ToastType.Warning);

            if (FindName(controlName) is TextBox control)
            {
                control.Focus();
                control.SelectAll();
            }
        }

        private void UpdateSelectionActions()
        {
            var enabled = _selectedProduct is not null && !_loadFailed;
            DeleteButton.IsEnabled = enabled;
            ModifyButton.IsEnabled = enabled;
        }

        private void UpdateProductCount() =>
            ProductCountText.Text = $"Cantidad de prod: {Products.Count}";

        private int GetNextId() => checked(Products.Select(product => product.Id).DefaultIfEmpty(0).Max() + 1);

        private void SetNextId()
        {
            try
            {
                ProductIdBox.Text = GetNextId().ToString(CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                ProductIdBox.Text = string.Empty;
            }
        }

        private void ClearForm()
        {
            _isUpdatingForm = true;
            try
            {
                ProductCodeBox.Clear();
                ProductNameBox.Clear();
                ProductCategoryBox.Clear();
                StockBox.Text = "0";
                ProductCostBox.Clear();
                ProductMarginBox.Clear();
                if (AskQuantityCheckBox != null)
                {
                    AskQuantityCheckBox.IsChecked = false;
                }
            }
            finally
            {
                _isUpdatingForm = false;
            }
        }

        private void SelectProduct(Producto product)
        {
            ProductGrid.SelectedItem = product;
            ProductGrid.ScrollIntoView(product);
        }
    }
}