using System;
using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Punto_de_Venta
{
    public static class BasculaService
    {
        public static string[] ObtenerPuertosDisponibles()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>
        /// Realiza una prueba sincrónica (usada comúnmente en la pantalla de Configuración).
        /// </summary>
        public static bool ProbarConexion(string puerto, int baudRate, out decimal pesoObtenido, out string mensajeError)
        {
            pesoObtenido = 0m;
            mensajeError = string.Empty;

            if (string.IsNullOrWhiteSpace(puerto))
            {
                mensajeError = "No se ha seleccionado ningún puerto COM.";
                return false;
            }

            SerialPort? port = null;
            try
            {
                port = new SerialPort(puerto, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 1200,
                    WriteTimeout = 1200
                };

                port.Open();

                // Enviar comando estándar de petición 'P' + CR (Torrey L-EQ, Rhino BAR, etc.)
                port.DiscardInBuffer();
                port.Write("P\r");

                Thread.Sleep(220);

                var respuesta = port.ReadExisting();
                if (string.IsNullOrWhiteSpace(respuesta))
                {
                    try { respuesta = port.ReadLine(); } catch { }
                }

                port.Close();

                if (TryParsePeso(respuesta, out pesoObtenido))
                {
                    return true;
                }

                mensajeError = string.IsNullOrWhiteSpace(respuesta)
                    ? "La báscula no respondió en el tiempo límite."
                    : $"Respuesta no válida de la báscula: '{respuesta.Trim()}'";
                return false;
            }
            catch (Exception ex)
            {
                mensajeError = ex.Message;
                return false;
            }
            finally
            {
                if (port != null && port.IsOpen)
                {
                    port.Close();
                }
            }
        }

        /// <summary>
        /// Lee el peso de forma asíncrona para usar en la pantalla de Cobro sin congelar la ventana.
        /// </summary>
        public static async Task<(bool Exito, decimal Peso, string Error)> ObtenerPesoAsync()
        {
            var config = AppConfig.Load();

            if (!config.BasculaHabilitada)
            {
                return (false, 0m, "La báscula está desactivada en la configuración.");
            }

            return await Task.Run(() =>
            {
                var puerto = config.BasculaPuerto;
                var baudRate = config.BasculaBaudRate;
                var esModoContinuo = config.BasculaProtocolo.Contains("Continuo", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(puerto))
                {
                    return (false, 0m, "No se ha configurado un puerto COM para la báscula.");
                }

                SerialPort? port = null;
                try
                {
                    port = new SerialPort(puerto, baudRate, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 1000,
                        WriteTimeout = 1000
                    };

                    port.Open();
                    port.DiscardInBuffer();

                    if (!esModoContinuo)
                    {
                        // Enviar petición de peso
                        port.Write("P\r");
                    }

                    Thread.Sleep(200);

                    var respuesta = port.ReadExisting();
                    if (string.IsNullOrWhiteSpace(respuesta))
                    {
                        try { respuesta = port.ReadLine(); } catch { }
                    }

                    port.Close();

                    if (TryParsePeso(respuesta, out var peso))
                    {
                        return (true, peso, string.Empty);
                    }

                    return (false, 0m, string.IsNullOrWhiteSpace(respuesta)
                        ? "Sin respuesta de la báscula."
                        : $"Trama no reconocida: '{respuesta.Trim()}'.");
                }
                catch (Exception ex)
                {
                    return (false, 0m, ex.Message);
                }
                finally
                {
                    if (port != null && port.IsOpen)
                    {
                        port.Close();
                    }
                }
            });
        }

        public static bool TryParsePeso(string respuesta, out decimal peso)
        {
            peso = 0m;
            if (string.IsNullOrWhiteSpace(respuesta)) return false;

            // Busca el primer patrón decimal en la trama (ej: " 1.250 kg", "ST,GS,  2.500kg", "P: 0.750")
            var match = Regex.Match(respuesta, @"[-+]?[0-9]*\.?[0-9]+");
            if (match.Success)
            {
                return decimal.TryParse(match.Value, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out peso) ||
                       decimal.TryParse(match.Value, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.GetCultureInfo("es-MX"), out peso);
            }

            return false;
        }
    }
}