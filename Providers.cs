using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Traduce
{
    internal sealed class ProviderDefinition
    {
        public string Id, Name, Protocol, Endpoint, DefaultModel, HelpUrl, Hint;
        public bool IsCli { get { return Protocol == "cli"; } }
        public override string ToString() { return Name; }
    }

    internal static class Providers
    {
        public static readonly ProviderDefinition[] All = {
            new ProviderDefinition { Id="codex", Name="ChatGPT · Codex", Protocol="cli", DefaultModel=Settings.DefaultModel, HelpUrl="https://developers.openai.com/codex/cli", Hint="Usa el cliente oficial de Codex y tu cuenta de ChatGPT. Sujeto a los límites y modelos de tu plan." },
            new ProviderDefinition { Id="claude-code", Name="Claude Code · cuenta oficial", Protocol="cli", DefaultModel="haiku", HelpUrl="https://code.claude.com/docs/en/setup", Hint="Ejecuta Claude Code oficial en tu ordenador. Accede con tu propia cuenta desde su navegador; Traduce no recibe sus credenciales." },
            new ProviderDefinition { Id="openai", Name="OpenAI · API", Protocol="responses", Endpoint="https://api.openai.com/v1", DefaultModel="gpt-6-luna", HelpUrl="https://platform.openai.com/api-keys", Hint="Pega tu clave de OpenAI Platform. La API se factura por separado de ChatGPT." },
            new ProviderDefinition { Id="anthropic", Name="Claude · API", Protocol="anthropic", Endpoint="https://api.anthropic.com/v1", DefaultModel="claude-haiku-4-5", HelpUrl="https://platform.claude.com/settings/keys", Hint="Pega tu clave de Claude Console. La API se factura por separado de la suscripción de Claude." },
            new ProviderDefinition { Id="gemini", Name="Google Gemini · API", Protocol="chat", Endpoint="https://generativelanguage.googleapis.com/v1beta/openai", DefaultModel="gemini-3.8-flash", HelpUrl="https://aistudio.google.com/api-keys", Hint="Pega tu clave de Google AI Studio. Las cuotas y la facturación son las de Gemini API." },
            new ProviderDefinition { Id="openrouter", Name="OpenRouter · API", Protocol="chat", Endpoint="https://openrouter.ai/api/v1", DefaultModel="", HelpUrl="https://openrouter.ai/settings/keys", Hint="Carga y elige un modelo de tu cuenta de OpenRouter. Para imágenes necesita admitir visión." },
            new ProviderDefinition { Id="ollama", Name="Ollama · local", Protocol="chat", Endpoint="http://localhost:11434/v1", DefaultModel="", HelpUrl="https://ollama.com/download/windows", Hint="Ejecuta Ollama en este ordenador y elige un modelo que tengas descargado. Para imágenes necesita visión; el texto OCR funciona con modelos de texto." },
            new ProviderDefinition { Id="custom", Name="Otra API compatible con OpenAI", Protocol="chat", DefaultModel="", HelpUrl="https://github.com/Yev94/traduce#proveedores", Hint="Introduce la URL base (por ejemplo https://servidor/v1), el modelo y su clave. HTTP sin cifrar solo se admite en localhost." }
        };
        public static ProviderDefinition Get(string id)
        {
            var provider = All.FirstOrDefault(p => p.Id == (string.IsNullOrEmpty(id) ? "codex" : id));
            if (provider == null) throw new ArgumentException("Elige un proveedor válido en Conexión.");
            return provider;
        }
        public static string Model(Settings settings)
        {
            var provider = Get(settings.Provider); var profile = settings.Profile(provider.Id);
            return string.IsNullOrWhiteSpace(profile.Model) ? provider.DefaultModel : profile.Model.Trim();
        }
        public static Uri Endpoint(Settings settings)
        {
            var provider = Get(settings.Provider);
            string url = provider.Id == "custom" ? settings.Profile(provider.Id).BaseUrl : provider.Endpoint;
            Uri endpoint;
            if (!Uri.TryCreate((url ?? "").Trim().TrimEnd('/') + "/", UriKind.Absolute, out endpoint) ||
                !(endpoint.Scheme == "https" || (endpoint.Scheme == "http" && endpoint.IsLoopback)) ||
                endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
                throw new ArgumentException("Usa una URL base HTTPS sin clave, usuario ni parámetros. HTTP solo está permitido para localhost.");
            return endpoint;
        }
        public static void Validate(Settings settings)
        {
            var provider = Get(settings.Provider);
            if (string.IsNullOrWhiteSpace(Model(settings))) throw new ArgumentException("Selecciona o escribe el nombre del modelo.");
            if (provider.IsCli) return;
            var endpoint = Endpoint(settings);
            if (provider.Id != "ollama" && !(provider.Id == "custom" && endpoint.IsLoopback) && settings.Profile(provider.Id).ReadKey().Length == 0)
                throw new ArgumentException("Pega tu clave API en Conexión.");
        }
    }

    internal static class JsonData
    {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 40 * 1024 * 1024 }; }
        public static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(object value, string key) { object found; return Map(value).TryGetValue(key, out found) ? found : null; }
        public static string Text(object value, string key) { return Get(value, key) as string ?? ""; }
        public static IEnumerable<object> Items(object value) { return value is IEnumerable && !(value is string) ? ((IEnumerable)value).Cast<object>() : Enumerable.Empty<object>(); }
    }

    internal sealed class ApiTranslator
    {
        private readonly HttpMessageHandler testHandler;
        internal ApiTranslator(HttpMessageHandler handler = null) { testHandler = handler; }
        internal static object Payload(string protocol, string model, string text, ImageInput image)
        {
            var content = new List<object>();
            if (protocol == "responses")
            {
                content.Add(new { type = "input_text", text = text });
                if (image != null) content.Add(new { type = "input_image", image_url = "data:image/png;base64," + Convert.ToBase64String(image.Png) });
                var payload = new Dictionary<string, object> { { "model", model }, { "instructions", Translator.Instructions }, { "input", new[] { new { role = "user", content = content } } }, { "store", false } };
                if (model == "gpt-6-luna") payload["reasoning"] = new { effort = "none" };
                return payload;
            }
            content.Add(new { type = "text", text = text });
            if (protocol == "anthropic")
            {
                if (image != null) content.Add(new { type = "image", source = new { type = "base64", media_type = "image/png", data = Convert.ToBase64String(image.Png) } });
                return new { model = model, system = Translator.Instructions, max_tokens = 16384, messages = new[] { new { role = "user", content = content } } };
            }
            if (image != null) content.Add(new { type = "image_url", image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(image.Png) } });
            return new { model = model, stream = false, messages = new object[] { new { role = "system", content = Translator.Instructions }, new { role = "user", content = content } } };
        }
        internal static string Parse(string protocol, string json)
        {
            var result = JsonData.Serializer().DeserializeObject(json);
            IEnumerable<object> content;
            if (protocol == "responses")
            {
                if (JsonData.Text(result, "status") == "incomplete" || JsonData.Text(result, "status") == "failed") throw new InvalidOperationException("La respuesta está incompleta. Prueba un recorte más pequeño u otro modelo.");
                content = JsonData.Items(JsonData.Get(result, "output")).Where(item => JsonData.Text(item, "type") == "message").SelectMany(item => JsonData.Items(JsonData.Get(item, "content")));
            }
            else if (protocol == "anthropic")
            {
                if (JsonData.Text(result, "stop_reason") == "max_tokens") throw new InvalidOperationException("La respuesta ha alcanzado el límite del modelo. Prueba un recorte más pequeño.");
                content = JsonData.Items(JsonData.Get(result, "content"));
            }
            else
            {
                var choice = JsonData.Items(JsonData.Get(result, "choices")).FirstOrDefault();
                if (JsonData.Text(choice, "finish_reason") == "length") throw new InvalidOperationException("La respuesta está incompleta. Prueba un recorte más pequeño.");
                var raw = JsonData.Get(JsonData.Get(choice, "message"), "content");
                if (raw is string) return RequireText((string)raw);
                content = JsonData.Items(raw);
            }
            return RequireText(string.Join("\n", content.Where(item => JsonData.Text(item, "type") == "text" || JsonData.Text(item, "type") == "output_text").Select(item => JsonData.Text(item, "text"))));
        }
        private static string RequireText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("El proveedor no ha devuelto texto. Comprueba que el modelo admita esta entrada.");
            return text.Trim();
        }
        internal static string HttpError(int code)
        {
            if (code == 401 || code == 403) return "El proveedor rechazó la clave o sus permisos. Revisa Conexión.";
            if (code == 429) return "El proveedor ha alcanzado su cuota o límite de uso. Revisa tu cuenta o espera e inténtalo de nuevo.";
            if (code == 400 || code == 404 || code == 422) return "El proveedor no admite esta petición. Comprueba el modelo, la URL y el soporte de imágenes.";
            if (code >= 300 && code < 400) return "El proveedor devolvió una redirección. Corrige la URL base; no se reenviarán tus credenciales.";
            return "El proveedor no pudo completar la petición (HTTP " + code + "). Inténtalo más tarde.";
        }
        private async Task<string> Send(Settings settings, string path, object body, CancellationToken token)
        {
            var provider = Providers.Get(settings.Provider);
            using (var client = testHandler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(testHandler, false))
            using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, new Uri(Providers.Endpoint(settings), path)))
            {
                client.Timeout = TimeSpan.FromSeconds(180); client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
                string key = settings.Profile(provider.Id).ReadKey();
                if (provider.Protocol == "anthropic") { request.Headers.TryAddWithoutValidation("x-api-key", key); request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01"); }
                else if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                if (body != null) request.Content = new StringContent(JsonData.Serializer().Serialize(body), Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token))
                    {
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(HttpError((int)response.StatusCode));
                        return await response.Content.ReadAsStringAsync();
                    }
                }
                catch (TaskCanceledException) { token.ThrowIfCancellationRequested(); throw new TimeoutException("El proveedor ha tardado demasiado. Inténtalo de nuevo."); }
                catch (HttpRequestException) { throw new InvalidOperationException("No se pudo conectar con el proveedor. Revisa la URL, la conexión y el certificado del servidor."); }
            }
        }
        public async Task<string> Translate(string text, ImageInput image, Settings settings, CancellationToken token)
        {
            Providers.Validate(settings); var provider = Providers.Get(settings.Provider);
            string path = provider.Protocol == "responses" ? "responses" : provider.Protocol == "anthropic" ? "messages" : "chat/completions";
            var json = await Send(settings, path, Payload(provider.Protocol, Providers.Model(settings), text, image), token);
            try { return Parse(provider.Protocol, json); }
            catch (ArgumentException) { throw new InvalidOperationException("El proveedor devolvió una respuesta con un formato no compatible."); }
        }
        public async Task<string[]> Models(Settings settings, CancellationToken token)
        {
            string json = await Send(settings, "models", null, token);
            try
            {
                var response = JsonData.Serializer().DeserializeObject(json);
                return JsonData.Items(JsonData.Get(response, "data")).Select(item => JsonData.Text(item, "id")).Where(id => id.Length > 0).Distinct().OrderBy(id => id).ToArray();
            }
            catch (ArgumentException) { throw new InvalidOperationException("El proveedor devolvió una lista de modelos con un formato no compatible."); }
        }
    }
}
