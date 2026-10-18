using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Punto_de_Venta.Modelos
{
    public class Venta : INotifyPropertyChanged
    {
        private string _productosResumenGuardado = string.Empty;
        private int _cantidadTotalGuardada;
        private string _estado = "Abierta";
        public string MetodoPago { get; set; } = "Efectivo";
        public int Id { get; set; }
        public DateTime FechaHora { get; set; } = DateTime.Now;
        public decimal Total { get; set; }
        public decimal PagoCon { get; set; }
        public decimal Cambio
        {
            get
            {
                if (Estado == "Fiado" || PagoCon < Total)
                {
                    return 0m;
                }
                return PagoCon - Total;
            }
            set { } // Setter vacío para permitir deserialización o lectura sin romper
        }
        public string Categoria { get; set; } = "N/A";
        // "Abierta"   -> Venta normal del turno actual (fondo blanco o neutro)
        // "Cerrada"   -> Venta incluida en un Cierre de Caja (fondo verde)
        // "Cancelada" -> Venta marcada como Devolucion (fondo rojo)
        public string Estado
        {
            get => _estado;
            set
            {
                if (_estado != value)
                {
                    _estado = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<DetalleVenta> Productos { get; set; } = new List<DetalleVenta>();

        public string ProductosResumen
        {
            get => Productos.Count > 0
                ? string.Join(", ", Productos.Select(product => $"{product.Nombre} x{product.Cantidad}"))
                : _productosResumenGuardado;
            set => _productosResumenGuardado = value ?? string.Empty;
        }

        public int CantidadTotal
        {
            get => Productos.Count > 0 ? Productos.Sum(product => product.Cantidad) : _cantidadTotalGuardada;
            set => _cantidadTotalGuardada = value;
        }

        // Calculos automaticos de Costo y Ganancia para el nuevo panel inferior
        public decimal CostoTotal =>
            Productos.Count > 0 ? Productos.Sum(product => product.Costo * product.Cantidad) : 0m;

        public decimal GananciaTotal =>
            Productos.Count > 0 ? Productos.Sum(product => product.Ganancia * product.Cantidad) : 0m;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void NotifyTotalesChanged()
        {
            OnPropertyChanged(nameof(CantidadTotal));
            OnPropertyChanged(nameof(ProductosResumen));
            OnPropertyChanged(nameof(CostoTotal));
            OnPropertyChanged(nameof(GananciaTotal));
            OnPropertyChanged(nameof(Cambio));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class DetalleVenta
    {
        public int Id { get; set; }
        public int VentaId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal Costo { get; set; }
        public decimal Ganancia { get; set; }
        public decimal Precio { get; set; }
        public decimal Subtotal => Precio * Cantidad;
        public string Categoria { get; set; } = "N/A";
    }
}