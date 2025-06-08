using AgctorSDK.Core.Tools.LanguageExecutors;
using System;
using System.Threading.Tasks;

namespace CodeExecutorTester
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Code Executor Tester");
            Console.WriteLine("====================");
            
            // Create the factory
            var factory = new LanguageExecutorFactory();
            
            // Test C# Execution
            await TestLanguage(factory, "csharp", @"
using System;
class Program 
{
    static void Main() 
    {
        Console.WriteLine(""Hello from C#!"");
        for (int i = 1; i <= 5; i++)
        {
            Console.WriteLine($""C# Counter: {i}"");
        }
    }
}");

            // Test Python Execution
            await TestLanguage(factory, "python", @"
print('Hello from Python!')
for i in range(1, 6):
    print(f'Python Counter: {i}')
");

            // Test Alias Handling
            Console.WriteLine("\nTesting language aliases:");
            Console.WriteLine("------------------------");
            
            var csharpExecutor = factory.GetExecutor("c#");
            Console.WriteLine($"'c#' resolves to: {csharpExecutor?.Language ?? "null"}");
            
            var pythonExecutor = factory.GetExecutor("py");
            Console.WriteLine($"'py' resolves to: {pythonExecutor?.Language ?? "null"}");

            var rubyExecutor = factory.GetExecutor("ruby");
            Console.WriteLine($"'ruby' resolves to: {rubyExecutor?.Language ?? "null"}");
            
            // Test custom executor
            Console.WriteLine("\nTesting custom executor registration:");
            Console.WriteLine("------------------------------------");
            
            // Create and register a dummy executor
            var mockExecutor = new MockLanguageExecutor("ruby");
            factory.RegisterExecutor(mockExecutor);
            
            // Test the mock executor
            var executor = factory.GetExecutor("ruby");
            Console.WriteLine($"'ruby' now resolves to: {executor?.Language ?? "null"}");
            
            if (executor != null)
            {
                var result = await executor.ExecuteCodeAsync("puts 'Hello from Ruby!'");
                Console.WriteLine($"Success: {result.Success}");
                Console.WriteLine($"Output: {result.Output}");
                Console.WriteLine($"Error: {result.Error}");
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
        
        static async Task TestLanguage(ILanguageExecutorFactory factory, string language, string code)
        {
            Console.WriteLine($"\nTesting {language} execution:");
            Console.WriteLine("------------------------");
            
            var executor = factory.GetExecutor(language);
            if (executor == null)
            {
                Console.WriteLine($"No executor found for {language}");
                return;
            }
            
            Console.WriteLine($"Executing code with {executor.Language} executor:");
            var result = await executor.ExecuteCodeAsync(code);
            
            Console.WriteLine($"Success: {result.Success}");
            if (!result.Success)
            {
                Console.WriteLine($"Error: {result.Error}");
            }
            
            Console.WriteLine("Output:");
            Console.WriteLine("-------");
            Console.WriteLine(result.Output);
        }
    }
    
    // A simple mock executor for testing
    class MockLanguageExecutor : ILanguageExecutor
    {
        public string Language { get; }
        
        public MockLanguageExecutor(string language)
        {
            Language = language;
        }
        
        public Task<(bool Success, string Output, string Error)> ExecuteCodeAsync(string code)
        {
            return Task.FromResult((true, $"Mock output for {Language}: {code}", string.Empty));
        }
    }
}
