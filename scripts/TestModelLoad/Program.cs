using System;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace TestModelLoad;

class Program
{
    static void Main()
    {
        string path = @"C:\WorkAuditModels\Gemma3";
        Console.WriteLine($"Attempting to load model from: {path}");
        
        try
        {
            using var model = new Model(path);
            Console.WriteLine("Model loaded successfully!");
            
            using var processor = new MultiModalProcessor(model);
            Console.WriteLine("Processor loaded successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR during load:");
            Console.WriteLine(ex.ToString());
            
            if (ex is OnnxRuntimeGenAIException oex)
            {
                // Inspect any inner details if available (usually just message)
            }
        }
    }
}
