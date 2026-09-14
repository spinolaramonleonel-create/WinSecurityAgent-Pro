using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinSecurityAgent.Core
{
    /// <summary>
    /// Gestor de Modelos LLM Locales y Remotos
    /// Soporta: Ollama, HuggingFace, GGUF, etc.
    /// </summary>
    public class LLMManager
    {
        private readonly string _modelsPath;
        private readonly HttpClient _httpClient;
        private readonly MemoryManager _memoryManager;
        private Dictionary<string, LocalModel> _loadedModels;

        public class LocalModel
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public string Type { get; set; } // GGUF, TensorFlow, PyTorch, etc.
            public long Size { get; set; }
            public DateTime LoadedAt { get; set; }
            public ModelMemoryLocation MemoryLocation { get; set; }
        }

        public enum ModelMemoryLocation
        {
            RAM,
            VRAM,
            Disk,
            Hybrid
        }

        public LLMManager(string modelsPath, MemoryManager memoryManager)
        {
            _modelsPath = modelsPath;
            _memoryManager = memoryManager;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            _loadedModels = new Dictionary<string, LocalModel>();

            // Crear directorio si no existe
            Directory.CreateDirectory(_modelsPath);
        }

        /// <summary>
        /// Descarga un modelo desde HuggingFace
        /// </summary>
        public async Task<LocalModel> DescargarModeloHuggingFace(
            string repositorio,
            string archivo,
            IProgress<DescargaProgreso> progreso = null)
        {
            try
            {
                var url = $"https://huggingface.co/{repositorio}/resolve/main/{archivo}";
                var rutaLocal = Path.Combine(_modelsPath, archivo);

                // Verificar si ya existe
                if (File.Exists(rutaLocal))
                {
                    Console.WriteLine($"[✓] Modelo ya existe: {rutaLocal}");
                    return new LocalModel
                    {
                        Name = archivo,
                        Path = rutaLocal,
                        Type = Path.GetExtension(archivo).ToLower(),
                        Size = new FileInfo(rutaLocal).Length,
                        LoadedAt = DateTime.Now,
                        MemoryLocation = ModelMemoryLocation.Disk
                    };
                }

                Console.WriteLine($"[↓] Descargando: {archivo}");

                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(rutaLocal, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var totalRead = 0L;
                        var buffer = new byte[8192];
                        int read;

                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;

                            var porcentaje = (int)((totalRead * 100) / totalBytes);
                            progreso?.Report(new DescargaProgreso
                            {
                                Porcentaje = porcentaje,
                                BytesDescargados = totalRead,
                                TotalBytes = totalBytes,
                                Archivo = archivo
                            });
                        }
                    }
                }

                Console.WriteLine($"[✓] Descarga completada: {rutaLocal}");

                var modelo = new LocalModel
                {
                    Name = archivo,
                    Path = rutaLocal,
                    Type = Path.GetExtension(archivo).ToLower(),
                    Size = new FileInfo(rutaLocal).Length,
                    LoadedAt = DateTime.Now,
                    MemoryLocation = ModelMemoryLocation.Disk
                };

                _loadedModels[archivo] = modelo;
                return modelo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Error descargando modelo: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Carga un modelo usando Ollama
        /// </summary>
        public async Task<bool> CargarModeloOllama(string nombreModelo)
        {
            try
            {
                var process = new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = $"run {nombreModelo}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(process))
                {
                    await proc.StandardOutput.ReadToEndAsync();
                    proc.WaitForExit(5000);
                    return proc.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Error cargando modelo con Ollama: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Asigna ubicación de memoria al modelo
        /// </summary>
        public async Task<bool> AsignarUbicacionMemoria(
            string nombreModelo,
            ModelMemoryLocation ubicacion,
            long tamanioRAM = 0,
            long tamanioVRAM = 0)
        {
            if (!_loadedModels.ContainsKey(nombreModelo))
                return false;

            var modelo = _loadedModels[nombreModelo];
            modelo.MemoryLocation = ubicacion;

            switch (ubicacion)
            {
                case ModelMemoryLocation.RAM:
                    return await _memoryManager.AllocarRAM(tamanioRAM);

                case ModelMemoryLocation.VRAM:
                    return await _memoryManager.AllocarVRAM(tamanioVRAM);

                case ModelMemoryLocation.Hybrid:
                    var ramAsignada = await _memoryManager.AllocarRAM(tamanioRAM);
                    var vramAsignada = await _memoryManager.AllocarVRAM(tamanioVRAM);
                    return ramAsignada && vramAsignada;

                default:
                    return true; // Disco es default
            }
        }

        /// <summary>
        /// Lista todos los modelos disponibles
        /// </summary>
        public List<LocalModel> ListarModelos()
        {
            var modelos = new List<LocalModel>();

            if (Directory.Exists(_modelsPath))
            {
                var archivos = Directory.GetFiles(_modelsPath, "*.*");
                foreach (var archivo in archivos)
                {
                    var info = new FileInfo(archivo);
                    modelos.Add(new LocalModel
                    {
                        Name = Path.GetFileName(archivo),
                        Path = archivo,
                        Type = Path.GetExtension(archivo),
                        Size = info.Length,
                        LoadedAt = info.LastWriteTime,
                        MemoryLocation = _loadedModels.ContainsKey(Path.GetFileName(archivo)) ?
                            _loadedModels[Path.GetFileName(archivo)].MemoryLocation :
                            ModelMemoryLocation.Disk
                    });
                }
            }

            return modelos;
        }

        /// <summary>
        /// Obtiene información de un modelo de HuggingFace
        /// </summary>
        public async Task<HuggingFaceModelInfo> ObtenerInfoModeloHuggingFace(string repositorio)
        {
            try
            {
                var url = $"https://huggingface.co/api/models/{repositorio}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var contenido = await response.Content.ReadAsStringAsync();
                var documento = JsonDocument.Parse(contenido);
                var raiz = documento.RootElement;

                return new HuggingFaceModelInfo
                {
                    Id = raiz.GetProperty("id").GetString(),
                    Descripcion = raiz.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                    Descargas = raiz.TryGetProperty("downloads", out var dl) ? dl.GetInt64() : 0,
                    Likes = raiz.TryGetProperty("likes", out var l) ? l.GetInt64() : 0,
                    Tags = raiz.TryGetProperty("tags", out var t) ? 
                        t.EnumerateArray().Select(x => x.GetString()).ToList() : new List<string>()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Error obteniendo info de HuggingFace: {ex.Message}");
                return null;
            }
        }
    }

    public class DescargaProgreso
    {
        public int Porcentaje { get; set; }
        public long BytesDescargados { get; set; }
        public long TotalBytes { get; set; }
        public string Archivo { get; set; }
    }

    public class HuggingFaceModelInfo
    {
        public string Id { get; set; }
        public string Descripcion { get; set; }
        public long Descargas { get; set; }
        public long Likes { get; set; }
        public List<string> Tags { get; set; }
    }
}
