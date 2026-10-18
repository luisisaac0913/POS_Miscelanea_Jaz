using System;
using System.Collections.Generic;

namespace Punto_de_Venta.Modelos
{
    public enum TipoPromocion
    {
        Volumen,    // Ej. 3 o más piezas a $15 c/u (o 3x$45)
        Combo,      // Ej. Producto A + Producto B = $30
        PorcentajeSegundo // Ej. 2do artículo con 50% de descuento
    }

    public class Promocion
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty; // Ej. "3 Coca Cola por $45"
        public TipoPromocion Tipo { get; set; } = TipoPromocion.Volumen;
        public bool Activa { get; set; } = true;

        // Vigencia opcional
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }

        // Parámetros para regla de VOLUMEN
        public int ProductoIdPrincipal { get; set; }
        public int CantidadMinima { get; set; } = 1;     // Ej. 3
        public decimal PrecioEspecial { get; set; }       // Ej. $15.00 c/u o total del paquete

        // Parámetros para regla de COMBO
        public int ProductoIdSecundario { get; set; }
        public decimal PrecioComboTotal { get; set; }     // Ej. $30.00 por ambos

        // Parámetros para regla de PORCENTAJE (Ej. 50% en la 2da pieza)
        public decimal PorcentajeDescuento { get; set; }  // Ej. 0.50m (50%)

        public bool EstaVigente()
        {
            if (!Activa) return false;
            var hoy = DateTime.Today;
            if (FechaInicio.HasValue && hoy < FechaInicio.Value.Date) return false;
            if (FechaFin.HasValue && hoy > FechaFin.Value.Date) return false;
            return true;
        }
    }
}