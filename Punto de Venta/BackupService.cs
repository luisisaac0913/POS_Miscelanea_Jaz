using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Punto_de_Venta
{
    public static class BackupService
    {
        private const int DiasRetencion = 15;

        public static string AppDataFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiscelaneaJaz"
        );

        public static string ObtenerCarpetaRespaldos(string? rutaPersonalizada = null)
        {
            if (!string.IsNullOrWhiteSpace(rutaPersonalizada) && Directory.Exists(rutaPersonalizada))
            {
                return rutaPersonalizada;
            }

            return Path.Combine(AppDataFolder, "Backups");
        }

        public static void GenerarRespaldo(string prefijo = "Auto", string? rutaPersonalizada = null)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    return;
                }

                var destinoCarpeta = ObtenerCarpetaRespaldos(rutaPersonalizada);
                Directory.CreateDirectory(destinoCarpeta);

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
                var zipName = $"Backup_{prefijo}_{timestamp}.zip";
                var destinationZip = Path.Combine(destinoCarpeta, zipName);

                if (File.Exists(destinationZip))
                {
                    return;
                }

                var extensionesValidas = new[] { ".txt", ".json" };
                var archivos = Directory.GetFiles(AppDataFolder)
                    .Where(file => extensionesValidas.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .ToList();

                if (archivos.Count == 0)
                {
                    return;
                }

                using (var zipArchive = ZipFile.Open(destinationZip, ZipArchiveMode.Create))
                {
                    foreach (var rutaArchivo in archivos)
                    {
                        var nombreArchivo = Path.GetFileName(rutaArchivo);
                        using var fileStream = new FileStream(rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var entry = zipArchive.CreateEntry(nombreArchivo, CompressionLevel.Optimal);
                        using var entryStream = entry.Open();
                        fileStream.CopyTo(entryStream);
                    }
                }

                LimpiarRespaldosAntiguos(destinoCarpeta);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupService] Error al generar respaldo: {ex.Message}");
            }
        }

        private static void LimpiarRespaldosAntiguos(string carpeta)
        {
            try
            {
                var limiteFecha = DateTime.Now.AddDays(-DiasRetencion);
                var archivosZip = Directory.GetFiles(carpeta, "Backup_*.zip");

                foreach (var zip in archivosZip)
                {
                    var fileInfo = new FileInfo(zip);
                    if (fileInfo.CreationTime < limiteFecha)
                    {
                        fileInfo.Delete();
                    }
                }
            }
            catch
            {
                // Ignorar si un archivo está en uso
            }
        }
    }
}