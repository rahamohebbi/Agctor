using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.IntegrationTests.TestHelpers
{
    public static class DebugHelper
    {
        public static async Task<bool> VerifyOllamaConnectivity(string ollamaUrl, string model, TestContext testContext)
        {
            testContext.WriteLine("=== Ollama Connectivity Check ===");
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            try
            {
                // Check if Ollama is running
                var response = await httpClient.GetAsync($"{ollamaUrl}/api/tags");
                testContext.WriteLine($"Ollama connection status: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    testContext.WriteLine($"Available models response: {content}");
                    
                    // Check if the specific model is available
                    if (content.Contains(model))
                    {
                        testContext.WriteLine($"✅ Model '{model}' is available");
                        return true;
                    }
                    else
                    {
                        testContext.WriteLine($"⚠️ Model '{model}' not found in available models");
                        testContext.WriteLine($"💡 Try running: ollama pull {model}");
                        return false;
                    }
                }
                else
                {
                    testContext.WriteLine($"❌ Ollama not accessible at {ollamaUrl}");
                    testContext.WriteLine("💡 Make sure to run: ollama serve");
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                testContext.WriteLine($"❌ Timeout connecting to Ollama at {ollamaUrl}");
                testContext.WriteLine("💡 Is Ollama running and responsive? Try: ollama serve");
                return false;
            }
            catch (HttpRequestException ex)
            {
                testContext.WriteLine($"❌ HTTP error connecting to Ollama: {ex.Message}");
                testContext.WriteLine($"💡 Is Ollama running? Try: ollama serve");
                return false;
            }
            catch (Exception ex)
            {
                testContext.WriteLine($"❌ Unexpected error: {ex.Message}");
                return false;
            }
        }

        public static void LogAgentState(IActor actor, TestContext testContext, string context = "")
        {
            var prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";
            testContext.WriteLine($"{prefix}Actor State: {actor.State}");
            testContext.WriteLine($"{prefix}Actor ID: {actor.Id}");
            testContext.WriteLine($"{prefix}Actor Type: {actor.ActorType}");
        }

        public static void LogMessageEnvelope(IMessageEnvelope envelope, TestContext testContext, string context = "")
        {
            var prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";
            testContext.WriteLine($"{prefix}=== Message Envelope ===");
            testContext.WriteLine($"{prefix}ID: {envelope.Id}");
            testContext.WriteLine($"{prefix}Payload Type: {envelope.Payload?.GetType().Name ?? "null"}");
            testContext.WriteLine($"{prefix}Payload: {envelope.Payload}");
            
            if (envelope.Headers != null && envelope.Headers.Count > 0)
            {
                testContext.WriteLine($"{prefix}Headers:");
                foreach (var header in envelope.Headers)
                {
                    testContext.WriteLine($"{prefix}  {header.Key}: {header.Value}");
                }
            }
            
            if (envelope.Metadata != null && envelope.Metadata.Count > 0)
            {
                testContext.WriteLine($"{prefix}Metadata:");
                foreach (var meta in envelope.Metadata)
                {
                    testContext.WriteLine($"{prefix}  {meta.Key}: {meta.Value}");
                }
            }
        }

        public static async Task<TimeSpan> MeasureExecutionTime(Func<Task> action)
        {
            var stopwatch = Stopwatch.StartNew();
            await action();
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }

        public static string GetDiagnosticCommands()
        {
            return @"
=== Debugging Commands ===

1. Check if Ollama is running:
   ps aux | grep ollama

2. Start Ollama:
   ollama serve

3. Check available models:
   ollama list

4. Pull the test model:
   ollama pull mistral

5. Test Ollama directly:
   curl http://localhost:11434/api/tags

6. Test model generation:
   curl -X POST http://localhost:11434/api/generate \
     -H 'Content-Type: application/json' \
     -d '{""model"":""mistral"",""prompt"":""Hello"",""stream"":false}'

7. Check Ollama logs:
   journalctl -u ollama (on systemd systems)
   or check console where 'ollama serve' is running
";
        }

        public static void PrintTroubleshootingGuide(TestContext testContext)
        {
            testContext.WriteLine(GetDiagnosticCommands());
        }
    }
}
