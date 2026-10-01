using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Traduce
{
    internal static class ProviderTests
    {
        private sealed class FakeHttp : HttpMessageHandler
        {
            public string Body, Authorization, AnthropicKey;
            public Uri Uri;
            public string Reply;
            public HttpStatusCode Code = HttpStatusCode.OK;
            public bool Wait;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Uri = request.RequestUri; Body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
                Authorization = request.Headers.Authorization == null ? "" : request.Headers.Authorization.ToString();
                AnthropicKey = request.Headers.Contains("x-api-key") ? request.Headers.GetValues("x-api-key").Single() : "";
                if (Wait) await Task.Delay(10000, token);
                return new HttpResponseMessage(Code) { Content = new StringContent(Reply ?? "") };
            }
        }
        public static async Task Run(Action<bool, string> check, ImageInput image)
        {
            var legacy = Settings.Decode("{\"CodexPath\":\"fixture.exe\",\"Model\":\"legacy-model\",\"Shortcut\":1}");
            check(legacy.Provider == "codex" && legacy.ConfigurationComplete && legacy.Profile("codex").Model == "legacy-model" && legacy.Profile("codex").CliPath == "fixture.exe", "Legacy Codex settings migrate without losing the model, executable or onboarding state");
            var settings = new Settings { Provider = "openai" };
            settings.Profile("openai").SetKey("test-key-not-a-real-secret");
            string json = JsonData.Serializer().Serialize(settings);
            check(!json.Contains("test-key-not-a-real-secret") && Settings.Decode(json).Profile("openai").ReadKey() == "test-key-not-a-real-secret", "API key persists only as Windows CurrentUser protected data");
            settings.Profile("anthropic").SetKey("separate-fixture-key");
            check(settings.Clone().Profile("openai").ReadKey() != settings.Profile("anthropic").ReadKey(), "Providers keep separate credentials");
            settings.Profile("openai").SetKey(""); check(settings.Profile("openai").ReadKey() == "", "Deleting a key removes the protected credential");
            foreach (string endpoint in new[] { "http://example.com/v1", "https://user:password@example.com/v1", "https://example.com/v1?key=secret", "file:///tmp/server", "https://example.com/#secret" })
            {
                var custom = new Settings { Provider = "custom" }; custom.Profile("custom").BaseUrl = endpoint;
                bool rejected = false; try { Providers.Endpoint(custom); } catch (ArgumentException) { rejected = true; }
                check(rejected, "Unsafe credential destination rejected: " + new Uri(endpoint).Scheme);
            }
            var local = new Settings { Provider = "custom" }; local.Profile("custom").BaseUrl = "http://127.0.0.1:11434/v1";
            check(Providers.Endpoint(local).IsLoopback, "Local OpenAI-compatible servers can use loopback HTTP");
            foreach (string id in new[] { "openai", "anthropic", "gemini", "openrouter", "ollama", "custom" })
            {
                var configured = new Settings { Provider = id };
                var profile = configured.Profile(id); profile.Model = "fixture-vision-model"; profile.SetKey("provider-fixture-key"); profile.BaseUrl = "https://example.com/v1";
                string protocol = Providers.Get(id).Protocol;
                string response = protocol == "responses" ? "{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\",\"summary\":[]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Hola\"}]}]}" : protocol == "anthropic" ? "{\"content\":[{\"type\":\"text\",\"text\":\"Hola\"}],\"stop_reason\":\"end_turn\"}" : "{\"choices\":[{\"message\":{\"content\":\"Hola\"},\"finish_reason\":\"stop\"}]}";
                foreach (var picture in new[] { (ImageInput)null, image })
                {
                    using (var http = new FakeHttp { Reply = response })
                    {
                        string translated = await new ApiTranslator(http).Translate("Hello", picture, configured, CancellationToken.None);
                        check(translated == "Hola" && http.Body.Contains("fixture-vision-model") && http.Body.Contains("Hello"), id + " HTTP contract returns only translated text");
                        check((picture == null) != http.Body.Contains(Convert.ToBase64String(image.Png)), id + " sends an image only for the image route");
                        check(protocol == "anthropic" ? http.AnthropicKey == "provider-fixture-key" : http.Authorization == "Bearer provider-fixture-key", id + " authenticates only to its configured API");
                        check(!http.Body.Contains("provider-fixture-key"), id + " never places its key in the request body");
                        if (protocol == "responses") check(http.Body.Contains("\"store\":false") && http.Uri.AbsolutePath == "/v1/responses", "OpenAI uses stateless Responses requests");
                    }
                }
            }
            settings.Profile("openai").SetKey("key-that-must-not-leak");
            using (var http = new FakeHttp { Code = HttpStatusCode.Unauthorized, Reply = "key-that-must-not-leak: private text" })
            {
                try { await new ApiTranslator(http).Translate("private source", null, settings, CancellationToken.None); throw new Exception("Accepted HTTP 401"); }
                catch (InvalidOperationException e) { check(!e.Message.Contains("key-that-must-not-leak") && !e.Message.Contains("private"), "HTTP errors do not expose credentials or provider response bodies"); }
            }
            using (var http = new FakeHttp { Wait = true })
            using (var cancellation = new CancellationTokenSource(60))
            {
                try { await new ApiTranslator(http).Translate("Hello", null, settings, cancellation.Token); throw new Exception("Ignored API cancellation"); }
                catch (OperationCanceledException) { check(true, "Cancelling a translation cancels the HTTP request"); }
            }
            check(ApiTranslator.HttpError(302).Contains("redirección"), "API redirects require an explicit corrected endpoint");
            string[] args = CliConnections.ClaudeArguments("haiku");
            check(args.Contains("--safe-mode") && args.Contains("--no-session-persistence") && args[Array.IndexOf(args, "--tools") + 1] == "", "Claude Code uses official auth with customizations and tools disabled");
            string input = CliConnections.ClaudeInput("Translate this", image);
            check(input.Contains("base64") && input.Contains(Convert.ToBase64String(image.Png)), "Claude Code receives images in structured stdin, without image-reading tools");
            check(CliConnections.ClaudeResult("{\"type\":\"system\"}\n{\"type\":\"result\",\"is_error\":false,\"result\":\"Hola\"}\n") == "Hola", "Claude Code streaming envelope extracts only the completed result");
            bool incomplete = false;
            try { ApiTranslator.Parse("responses", "{\"status\":\"incomplete\"}"); } catch (InvalidOperationException) { incomplete = true; }
            check(incomplete, "Incomplete provider output is not cached as a successful translation");
        }
    }
}
