using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace Punto_de_Venta.Modelos
{
    public class Deudor : INotifyPropertyChanged
    {
        private string _nombre = string.Empty;
        private string _concepto = string.Empty;
        private decimal _montoOriginal;
        private decimal _totalAbonado;
        public double PorcentajeAbonado => MontoOriginal > 0
    ? (double)Math.Min(100m, Math.Round((TotalAbonado / MontoOriginal) * 100m, 1))
    : 100.0;
        public int Id { get; set; }
        public string Nombre
        {
            get => _nombre;
            set
            {
                if (_nombre != value)
                {
                    _nombre = value;
                    OnPropertyChanged();
                }
            }
        }
        public string Telefono { get; set; } = string.Empty;
        public string Concepto
        {
            get => _concepto;
            set
            {
                if (_concepto != value)
                {
                    _concepto = value;
                    OnPropertyChanged();
                }
            }
        }
        public decimal MontoOriginal
        {
            get => _montoOriginal;
            set
            {
                if (_montoOriginal != value)
                {
                    _montoOriginal = value;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public decimal TotalAbonado
        {
            get => _totalAbonado;
            set
            {
                if (_totalAbonado != value)
                {
                    _totalAbonado = value;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public decimal SaldoRestante => Math.Max(0, MontoOriginal - TotalAbonado);
        public decimal SaldoPendiente
        {
            get => SaldoRestante;
            set => MontoOriginal = TotalAbonado + value;
        }
        public bool EstaSaldada => SaldoRestante <= 0;
        public bool EstaAbonada => TotalAbonado > 0 && SaldoRestante > 0;
        public bool SinAbonos => TotalAbonado == 0 && SaldoRestante > 0;

        public List<Abono> Abonos { get; set; } = [];
        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyBalanceState()
        {
            OnPropertyChanged(nameof(SaldoRestante));
            OnPropertyChanged(nameof(SaldoPendiente));
            OnPropertyChanged(nameof(EstaSaldada));
            OnPropertyChanged(nameof(EstaAbonada));
            OnPropertyChanged(nameof(SinAbonos));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    public class Abono
    {
        public int Id { get; set; }
        public int DeudorId { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;
        public decimal Monto { get; set; }                
    }
}
