using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinSecurityAgent.Core
{
    /// <summary>
    /// Gestor de APIs externas (OpenAI, Anthropic, Google, etc.)
    /// </summary>
    public class ApiManager
    {
        private readonly Dictionary<string, string> _apiKeys = new();
        private readonly HttpClient _httpClient = new();
        private string _proveedorActual = "local";

        public enum ProveedorAPI
        {
            Local,
            OpenAI,
            Anthropic,
            Google,
            HuggingFace,
            LocalAI
        }

        /// <summary>
        /// Registra una API key de forma encriptada
        /// </summary>
        public void RegistrarApiKey(ProveedorAPI proveedor, string apiKey)
        {
            var clave = proveedor.ToString();
            var apiKeyEncriptada = EncriptarApiKey(apiKey);
            _apiKeys[clave] = apiKeyEncriptada;
            Console.WriteLine($"[✓] API key registrada: {proveedor}");
        }

        /// <summary>
        /// Encripta una API key
        /// </summary>
        private string EncriptarApiKey(string apiKey)
        {
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Key = System.Text.Encoding.UTF8.GetBytes("WinSecurityAgentPro123".PadRight(32).Substring(0, 32));
                aes.IV = System.Text.Encoding.UTF8.GetBytes("WinSecAgent2024!".PadRight(16).Substring(0, 16));

                var cipher = aes.CreateEncryptor(aes.Key, aes.IV);
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var cs = new System.Security.Cryptography.CryptoStream(ms, cipher, System.Security.Cryptography.CryptoStreamMode.Write))
                    {
                        using (var sw = new System.IO.StreamWriter(cs))
                        {
                            sw.Write(apiKey);
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
        }

        /// <summary>
        /// Desencripta una API key
        /// </summary>
        private string DesencriptarApiKey(string apiKeyEncriptada)
        {
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Key = System.Text.Encoding.UTF8.GetBytes("WinSecurityAgentPro123".PadRight(32).Substring(0, 32));
                aes.IV = System.Text.Encoding.UTF8.GetBytes("WinSecAgent2024!".PadRight(16).Substring(0, 16));

                var decipher = aes.CreateDecryptor(aes.Key, aes.IV);
                using (var ms = new System.IO.MemoryStream(Convert.FromBase64String(apiKeyEncriptada)))
                {
                    using (var cs = new System.Security.Cryptography.CryptoStream(ms, decipher, System.Security.Cryptography.CryptoStreamMode.Read))
                    {
                        using (var sr = new System.IO.StreamReader(cs))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Llama a OpenAI API
        /// </summary>
        public async Task<string> LlamarOpenAI(string prompt, string modelo = "gpt-4")
        {
            try
            {
                if (!_apiKeys.ContainsKey("OpenAI"))
                    throw new Exception("API Key de OpenAI no configurada");

                var apiKey = DesencriptarApiKey(_apiKeys["OpenAI"]);
                var request = new
                {
                    model = modelo,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.7,
                    max_tokens = 2000
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json"
                );

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var response = await _httpClient.PostAsync(
                    "https://api.openai.com/v1/chat/completions",
                    content
                );

                response.EnsureSuccessStatusCode();
                var resultado = await response.Content.ReadAsStringAsync();
                var documento = JsonDocument.Parse(resultado);
                
                return documento.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Error en OpenAI: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Llama a Anthropic Claude API
        /// </summary>
        public async Task<string> LlamarClaude(string prompt, string modelo = "claude-3-opus-20240229")
        {
            try
            {
                if (!_apiKeys.ContainsKey("Anthropic"))
                    throw new Exception("API Key de Anthropic no configurada");

                var apiKey = DesencriptarApiKey(_apiKeys["Anthropic"]);
                var request = new
                {
                    model = modelo,
                    max_tokens = 2048,
                    messages = new[] { new { role = "user", content = prompt } }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json"
                );

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
                _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                var response = await _httpClient.PostAsync(
                    "https://api.anthropic.com/v1/messages",
                    content
                );

                response.EnsureSuccessStatusCode();
                var resultado = await response.Content.ReadAsStringAsync();
                var documento = JsonDocument.Parse(resultado);
                
                return documento.RootElement
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Error en Anthropic: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Establece el proveedor actual
        /// </summary>
        public void EstablecerProveedor(ProveedorAPI proveedor)
        {
            _proveedorActual = proveedor.ToString();
            Console.WriteLine($"[✓] Proveedor establecido: {proveedor}");
        }

        /// <summary>
        /// Obtiene el proveedor actual
        /// </summary>
        public string ObtenerProveedorActual() => _proveedorActual;
    }
}
