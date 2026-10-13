using System;
using System.IO;
using System.Text;

namespace Punto_de_Venta
{
    public static class EtiquetaPrinter
    {
        public static byte[] GenerarBytesEtiqueta(string negocio, string nombreProducto, string codigo, decimal precio, int copias = 1)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // Encoding nativo en .NET Core / .NET (compatible con ESC/POS y acentos estándar)
            var encoding = Encoding.Latin1;

            for (int i = 0; i < copias; i++)
            {
                // 1. Inicializar impresora (ESC @)
                bw.Write(new byte[] { 0x1B, 0x40 });

                // 2. Alinear al centro (ESC a 1)
                bw.Write(new byte[] { 0x1B, 0x61, 0x01 });

                // 3. Nombre del negocio
                if (!string.IsNullOrWhiteSpace(negocio))
                {
                    bw.Write(new byte[] { 0x1B, 0x45, 0x00 }); // Negrita OFF
                    bw.Write(encoding.GetBytes(negocio + "\n"));
                }

                // 4. Nombre del producto (Negrita)
                bw.Write(new byte[] { 0x1B, 0x45, 0x01 }); // Negrita ON
                bw.Write(encoding.GetBytes(nombreProducto + "\n\n"));
                bw.Write(new byte[] { 0x1B, 0x45, 0x00 }); // Negrita OFF

                // 5. Configurar Código de Barras Code 128
                // GS h 60 (Altura: 60 puntos)
                bw.Write(new byte[] { 0x1D, 0x68, 60 });

                // GS w 2 (Grosor de barras)
                bw.Write(new byte[] { 0x1D, 0x77, 2 });

                // GS H 2 (Texto legible HRI abajo)
                bw.Write(new byte[] { 0x1D, 0x48, 0x02 });

                // GS f 0 (Fuente estándar)
                bw.Write(new byte[] { 0x1D, 0x66, 0x00 });

                // GS k 73 [longitud] [datos] -> CODE 128 (Subconjunto B '{B')
                byte[] datosCodigo = encoding.GetBytes("{B" + codigo);
                bw.Write(new byte[] { 0x1D, 0x6B, 73, (byte)datosCodigo.Length });
                bw.Write(datosCodigo);

                bw.Write(encoding.GetBytes("\n"));

                // 6. Precio en tamaño doble
                bw.Write(new byte[] { 0x1B, 0x21, 0x30 });
                bw.Write(new byte[] { 0x1B, 0x45, 0x01 });
                bw.Write(encoding.GetBytes($"\n${precio:0.00}\n"));

                // Restaurar texto normal
                bw.Write(new byte[] { 0x1B, 0x21, 0x00 });
                bw.Write(new byte[] { 0x1B, 0x45, 0x00 });

                // 7. Avance de papel (Feed)
                bw.Write(new byte[] { 0x1B, 0x64, 0x03 });

                // 8. Corte parcial de papel
                bw.Write(new byte[] { 0x1D, 0x56, 0x42, 0x00 });
            }

            return ms.ToArray();
        }
    }
}