using System;
using System.Linq;
using Windows.Media.Ocr;
using Windows.Globalization;

namespace TestOcr;

class Program
{
    static void Main()
    {
        Console.WriteLine("Available OCR Languages:");
        foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
        {
            Console.WriteLine($"- {lang.LanguageTag} ({lang.DisplayName})");
        }
        
        var arabic = new Language("ar-SA");
        if (OcrEngine.IsLanguageSupported(arabic))
        {
            Console.WriteLine("Arabic is supported!");
        }
        else
        {
            Console.WriteLine("Arabic IS NOT supported. Need to install language pack.");
        }
    }
}
