using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace HSBot.Cards
{
    static class FileExtractor
    {
        public static void Extract(String fromFile, String toDirectory)
        {
            if (!Directory.Exists(toDirectory))
                Directory.CreateDirectory(toDirectory);
            if (!File.Exists(fromFile) || !Directory.Exists(toDirectory))
                throw new IOException("Cannot read source file or target directory");

            FileStream input = File.OpenRead(fromFile);

            Console.WriteLine("Extracting card data...");

            Queue<long> xmlOffsets = new Queue<long>();

            while (input.Position < input.Length)
            {
   
                input.SeekForAscii("<?xml");
                xmlOffsets.Enqueue(input.Position - "<?xml".Length);
                input.SeekForAscii("\0");
                xmlOffsets.Enqueue(input.Position - 1);

            }
            input.Close();
            input = File.OpenRead(fromFile);
            for (int count = 0; xmlOffsets.Count > 0; ++count)
            {
                long start = xmlOffsets.Dequeue();
                long end = xmlOffsets.Dequeue();
                input.Seek(start, SeekOrigin.Begin);
                byte[] data = new byte[end - start];
                input.Read(data, 0, (int)(end - start));
                try
                {
                    FileStream output = File.Create(Path.Combine(toDirectory, String.Format("{0}.xml", count)));
                    output.Write(data, 0, data.Length);
                    output.Close();
                } catch (IOException e)
                {
                    Console.WriteLine("Exception writing card file: "+e);
                }
                Console.WriteLine("Created {0}", Path.Combine(toDirectory, String.Format("{0}.xml", count)));
            }
            Console.WriteLine("Done extracting cards.");
        }

        public static void SeekForAscii(this Stream stream, String text)
        {
            
            int textPtr = 0;
            int b;
            while (true)
            {
                b = stream.ReadByte();
                if (b < 0) break;
                if (b == text[textPtr])
                {
                    ++textPtr;
                    if (textPtr == text.Length)
                        return;
                }
                else
                    textPtr = 0;

            }
        }
    }
}
