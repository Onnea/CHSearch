using System;
using Tesseract;

namespace Onnea
{
    public class ImageOCR
    {
        public static Tuple<string, float> OCR( byte[] tiffImage )
        {
            string text = null;
            float confidence = 0.0F;
            try
            {
                using ( var engine = new TesseractEngine( @"./tessdata", "eng", EngineMode.Default ) )
                using ( var img = Pix.LoadTiffFromMemory( tiffImage ) )
                using ( var page = engine.Process( img ) )
                {
                    text = page.GetText();
                    confidence = page.GetMeanConfidence();
                }
            }
            catch ( Exception e )
            {
                Console.WriteLine( "Unexpected Error: " + e.Message );
                Console.WriteLine( "Details: " );
                Console.WriteLine( e.ToString() );
            }

            return Tuple.Create(text, confidence);
        }
    }
}
