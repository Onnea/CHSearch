using Ghostscript.NET.Rasterizer;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;

namespace Onnea
{
    public class PdfToImageConverter
    {
        /// <summary>
        /// Fills a given MemoryStream with images of the pdf pages and
        /// yields the lengths of these pages.
        /// </summary>
        public static IEnumerable<byte[]> CovertPdfToImage( byte[] pdf, int desired_x_dpi = 96,
                                                                        int desired_y_dpi = 96)
        {
            using ( var rasterizer = new GhostscriptRasterizer() )
            {
                rasterizer.Open( new MemoryStream( pdf ) );

                for ( var pageNumber = 1; pageNumber <= rasterizer.PageCount; pageNumber++ )
                {
                    var img = rasterizer.GetPage( desired_x_dpi, desired_y_dpi, pageNumber );
                    var ms = new MemoryStream();
                    img.Save( ms, ImageFormat.Tiff );
                    yield return ms.GetBuffer();
                }
            }
        }
    }
}
